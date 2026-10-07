using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave3;

public sealed class AuthOtpConcurrencyTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	private const string Email = "customer.seed@tranxit.test";
	private const string Code = "223344";
	private const string NewPassword = "Changedpass1!";

	[Theory(DisplayName = "T-AUTH-5.DelayedOtpWriteCannotUndoReset")]
	[InlineData("send-code")]
	[InlineData("verify-code")]
	public async Task DelayedOtpWriteCannotUndoReset(string operation)
	{
		// UC-AUTH-2, UC-AUTH-5: pause after the stale user has been read, and
		// after mail delivery for send-code, immediately before its SQL write.
		await SetCodeAsync();
		var pause = new OtpWritePause();
		await using var factory = new AccountServiceFactory(Fixture, pause);
		using var client = factory.CreateClient();
		var delayed = client.PostAsJsonAsync($"/api/{operation}", new { email = Email, code = Code });
		await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
		try
		{
			using var reset = await ResetAsync(AccountClient);
			reset.StatusCode.Should().Be(HttpStatusCode.OK);
		}
		finally
		{
			pause.Release.TrySetResult();
		}
		using var staleResponse = await delayed;
		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.SingleAsync(user => user.Id == 1);
		BCrypt.Net.BCrypt.EnhancedVerify(NewPassword, user.PasswordHash).Should().BeTrue(
			"a delayed OTP writer must never overwrite the newly reset password with its old user snapshot");
		user.VerificationCode.Should().BeNull("a send begun before the reset must not install an obsolete OTP after it");
		user.CodeSentAtUtc.Should().BeNull();
		staleResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await staleResponse.ReadApiResultAsync<bool>()).IsSuccess.Should().BeFalse();
	}

	[Theory(DisplayName = "T-AUTH-2.ConcurrentOtpConsumersSingleWinner")]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentOtpConsumersSingleWinner(bool secondIsReset)
	{
		// Force both verification/reset requests to see the same unused hash.
		await SetCodeAsync();
		var barrier = new OtpReadBarrier();
		await using var factory = new AccountServiceFactory(Fixture, barrier);
		using var client = factory.CreateClient();
		var first = VerifyAsync(client);
		var second = secondIsReset ? ResetAsync(client) : VerifyAsync(client);
		var responses = await Task.WhenAll(first, second);
		try
		{
			barrier.Arrivals.Should().Be(2);
			responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.BadRequest]);
			await using var db = Fixture.CreateAccountDbContext();
			var user = await db.Users.SingleAsync(user => user.Id == 1);
			user.VerificationCode.Should().BeNull();
			user.CodeSentAtUtc.Should().BeNull();
			var expectedPassword = secondIsReset && responses[1].StatusCode == HttpStatusCode.OK ? NewPassword : "Password1!";
			BCrypt.Net.BCrypt.EnhancedVerify(expectedPassword, user.PasswordHash).Should().BeTrue();
		}
		finally
		{
			foreach (var response in responses) response.Dispose();
		}
	}

	[Theory(DisplayName = "T-AUTH-2.StaleOtpWriteCannotReplaceNewerCode")]
	[InlineData("send-code")]
	[InlineData("verify-code")]
	public async Task StaleOtpWriteCannotReplaceNewerCode(string operation)
	{
		await SetCodeAsync();
		var pause = new OtpWritePause();
		await using var factory = new AccountServiceFactory(Fixture, pause);
		using var client = factory.CreateClient();
		var delayed = client.PostAsJsonAsync($"/api/{operation}", new { email = Email, code = Code });
		await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
		string? replacementHash;
		DateTime? replacementTime;
		try
		{
			using var resent = await AccountClient.PostAsJsonAsync("/api/send-code", new { email = Email });
			resent.StatusCode.Should().Be(HttpStatusCode.OK);
			await using var db = Fixture.CreateAccountDbContext();
			var replaced = await db.Users.SingleAsync(user => user.Id == 1);
			replacementHash = replaced.VerificationCode;
			replacementTime = replaced.CodeSentAtUtc;
		}
		finally
		{
			pause.Release.TrySetResult();
		}
		using var stale = await delayed;
		stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		await using var verificationDb = Fixture.CreateAccountDbContext();
		var after = await verificationDb.Users.SingleAsync(user => user.Id == 1);
		after.VerificationCode.Should().Be(replacementHash);
		after.CodeSentAtUtc.Should().Be(replacementTime);
	}

	[Fact(DisplayName = "T-AUTH-2.VerificationWithoutTimestampRejected")]
	public async Task VerificationWithoutTimestampRejected()
	{
		await SetCodeAsync();
		await using var db = Fixture.CreateAccountDbContext();
		await db.Users.Where(user => user.Id == 1).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.CodeSentAtUtc, (DateTime?)null));
		using var response = await VerifyAsync(AccountClient);
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await db.Users.SingleAsync(user => user.Id == 1)).VerificationCode.Should().NotBeNull();
	}

	private async Task SetCodeAsync()
	{
		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.VerificationCode = BCrypt.Net.BCrypt.EnhancedHashPassword(Code);
		user.CodeSentAtUtc = DateTime.UtcNow;
		await db.SaveChangesAsync();
	}

	private static Task<HttpResponseMessage> VerifyAsync(HttpClient client) =>
		client.PostAsJsonAsync("/api/verify-code", new { email = Email, code = Code });
	private static Task<HttpResponseMessage> ResetAsync(HttpClient client) =>
		client.PostAsJsonAsync("/api/reset-password", new { email = Email, code = Code, password = NewPassword, confirmPassword = NewPassword });

	private sealed class OtpWritePause : DbCommandInterceptor
	{
		private int _arrivals;
		public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private async Task PauseAsync(DbCommand command, CancellationToken cancellationToken)
		{
			if (command.CommandText.Contains("UPDATE") && command.CommandText.Contains("[Users]") &&
				Interlocked.Increment(ref _arrivals) == 1)
			{
				Arrived.TrySetResult();
				await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
			}
		}
		public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
			CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
		{
			await PauseAsync(command, cancellationToken);
			return result;
		}
		public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
			CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			await PauseAsync(command, cancellationToken);
			return result;
		}
	}

	private sealed class OtpReadBarrier : DbCommandInterceptor
	{
		private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _arrivals;
		public int Arrivals => _arrivals;
		public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
			DbDataReader result, CancellationToken cancellationToken = default)
		{
			if (command.CommandText.Contains("FROM [Users]") && command.CommandText.Contains("[NormalizedEmail]"))
			{
				if (Interlocked.Increment(ref _arrivals) == 2) _bothRead.TrySetResult();
				await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
			}
			return result;
		}
	}
}
