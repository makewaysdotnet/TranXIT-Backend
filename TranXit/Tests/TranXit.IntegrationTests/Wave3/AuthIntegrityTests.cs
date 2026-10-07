extern alias AccountService;

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using TranXit.IntegrationTests.Infrastructure;
using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;

namespace TranXit.IntegrationTests.Wave3;

public sealed class AuthIntegrityTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	private const string Email = "customer.seed@tranxit.test";
	private const string Code = "112233";

	[Theory(DisplayName = "T-AUTH-3.CreationPasswordPolicyCanLogin")]
	[InlineData("Validpass1#")]
	[InlineData("Validpass1$")]
	[InlineData("Validpass1&")]
	[InlineData("Validpass1{")]
	public async Task CreationPasswordPolicyCanLogin(string password)
	{
		// UC-AUTH-1, UC-AUTH-2, UC-AUTH-3, UC-AUTH-5
		const string email = "password.policy@tranxit.test";
		using var registered = await AccountClient.PostAsJsonAsync("/api/register", new
		{
			email, password, confirmPassword = password, username = "Password policy", phone = "123456789", role = "Customer"
		});
		registered.StatusCode.Should().Be(HttpStatusCode.OK);
		await using (var db = Fixture.CreateAccountDbContext())
		{
			var user = await db.Users.SingleAsync(user => user.Email == email);
			user.VerificationCode = BCrypt.Net.BCrypt.EnhancedHashPassword("123456");
			await db.SaveChangesAsync();
		}
		using var verified = await AccountClient.PostAsJsonAsync("/api/verify-code", new { email, code = "123456" });
		verified.StatusCode.Should().Be(HttpStatusCode.OK);
		using var firstLogin = await AccountClient.PostAsJsonAsync("/api/login", new { email, password });
		firstLogin.StatusCode.Should().Be(HttpStatusCode.OK);

		await SetCodeAsync();
		using var reset = await ResetAsync(AccountClient, password);
		reset.StatusCode.Should().Be(HttpStatusCode.OK);
		using var resetLogin = await AccountClient.PostAsJsonAsync("/api/login", new { email = Email, password });
		resetLogin.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact(DisplayName = "T-AUTH-5.ConcurrentResetConsumesCodeOnce")]
	public async Task ConcurrentResetConsumesCodeOnce()
	{
		// UC-AUTH-5, UC-AUTH-10: force both requests to read the same unused OTP.
		await SetCodeAsync();
		var barrier = new ResetReadBarrier();
		await using var factory = new AccountServiceFactory(Fixture, barrier);
		using var client = factory.CreateClient();
		using (var scope = factory.Services.CreateScope())
		{
			var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AccountDbContext>>();
			options.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().Contain(barrier);
		}
		var responses = await Task.WhenAll(ResetAsync(client, "Firstpass1!"), ResetAsync(client, "Secondpass1!"));
		try
		{
			barrier.Arrivals.Should().Be(2);
			responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.BadRequest]);
			await using var db = Fixture.CreateAccountDbContext();
			var user = await db.Users.FindAsync(1);
			user!.VerificationCode.Should().BeNull();
			var winningPassword = responses[0].StatusCode == HttpStatusCode.OK ? "Firstpass1!" : "Secondpass1!";
			BCrypt.Net.BCrypt.EnhancedVerify(winningPassword, user.PasswordHash).Should().BeTrue();
		}
		finally
		{
			foreach (var response in responses) response.Dispose();
		}
	}

	[Fact(DisplayName = "T-AUTH-5.ResetRevocationFailureRollsBackPasswordAndCode")]
	public async Task ResetRevocationFailureRollsBackPasswordAndCode()
	{
		// UC-AUTH-5, UC-AUTH-10: a real SQL write failure, not a mocked service.
		using var loginResponse = await AccountClient.PostAsJsonAsync("/api/login", new { email = Email, password = "Password1!" });
		loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		var login = (await loginResponse.ReadApiResultAsync<LoginValue>()).Value!;
		await SetCodeAsync();
		await using var db = Fixture.CreateAccountDbContext();
		var before = await db.Users.AsNoTracking().SingleAsync(user => user.Id == 1);
		await db.Database.ExecuteSqlRawAsync("ALTER TABLE [RefreshTokens] ADD CONSTRAINT [CK_TestResetRevocationFailure] CHECK ([RevokedAtUtc] IS NULL);");
		try
		{
			using var reset = await ResetAsync(AccountClient, "Changedpass1!");
			reset.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
			var after = await db.Users.AsNoTracking().SingleAsync(user => user.Id == 1);
			after.PasswordHash.Should().Be(before.PasswordHash);
			after.VerificationCode.Should().Be(before.VerificationCode);
			after.CodeSentAtUtc.Should().Be(before.CodeSentAtUtc);
			(await db.RefreshTokens.CountAsync(token => token.RevokedAtUtc == null)).Should().Be(1);
		}
		finally
		{
			await db.Database.ExecuteSqlRawAsync("ALTER TABLE [RefreshTokens] DROP CONSTRAINT [CK_TestResetRevocationFailure];");
		}
		using var retry = await ResetAsync(AccountClient, "Changedpass1!");
		retry.StatusCode.Should().Be(HttpStatusCode.OK);
		using var refresh = await AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken = login.RefreshToken });
		refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Theory(DisplayName = "T-AUTH-10.ConcurrentSessionIssuanceCannotEscapeReset")]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ConcurrentSessionIssuanceCannotEscapeReset(bool rotate)
	{
		// Pause issuance after password validation. Reset must serialize with this
		// login, then revoke its refresh token; it cannot commit around the issuance.
		await SetCodeAsync();
		string? oldRefresh = null;
		if (rotate)
		{
			using var seedLogin = await AccountClient.PostAsJsonAsync("/api/login", new { email = Email, password = "Password1!" });
			seedLogin.StatusCode.Should().Be(HttpStatusCode.OK);
			oldRefresh = (await seedLogin.ReadApiResultAsync<LoginValue>()).Value!.RefreshToken;
		}
		var barrier = new LoginIssuanceBarrier();
		await using var factory = new AccountServiceFactory(Fixture, barrier);
		using var client = factory.CreateClient();
		var loginTask = rotate
			? client.PostAsJsonAsync("/api/refresh", new { refreshToken = oldRefresh })
			: client.PostAsJsonAsync("/api/login", new { email = Email, password = "Password1!" });
		await barrier.Issuing.Task.WaitAsync(TimeSpan.FromSeconds(20));
		Task<HttpResponseMessage>? resetTask = null;
		try
		{
			resetTask = ResetAsync(client, "Changedpass1!");
			await barrier.ResetClaiming.Task.WaitAsync(TimeSpan.FromSeconds(20));
			// Observe an actual SQL lock wait, not a scheduling delay.
			await WaitForBlockedOrCompletedResetAsync(resetTask, barrier.SessionId);
			resetTask.IsCompleted.Should().BeFalse("login holds the user lock until issuance commits");
		}
		finally
		{
			barrier.Release.TrySetResult();
		}
		using var loginResponse = await loginTask;
		using var resetResponse = await resetTask!;
		loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		var login = (await loginResponse.ReadApiResultAsync<LoginValue>()).Value!;
		using var refresh = await AccountClient.PostAsJsonAsync("/api/refresh", new { refreshToken = login.RefreshToken });
		refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		using var oldLogin = await AccountClient.PostAsJsonAsync("/api/login", new { email = Email, password = "Password1!" });
		oldLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	private async Task WaitForBlockedOrCompletedResetAsync(Task<HttpResponseMessage> reset, int blockingSessionId)
	{
		await using var connection = new SqlConnection(Fixture.AccountConnectionString);
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @session AND database_id = DB_ID();";
		command.Parameters.AddWithValue("@session", blockingSessionId);
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!reset.IsCompleted && DateTime.UtcNow < deadline)
		{
			if (Convert.ToInt32(await command.ExecuteScalarAsync()) > 0) return;
			await Task.Delay(25);
		}
		reset.IsCompleted.Should().BeTrue("the reset must either be blocked on issuance or finish with the old defect");
	}

	private async Task SetCodeAsync()
	{
		await using var db = Fixture.CreateAccountDbContext();
		var user = await db.Users.FindAsync(1);
		user!.VerificationCode = BCrypt.Net.BCrypt.EnhancedHashPassword(Code);
		user.CodeSentAtUtc = DateTime.UtcNow;
		await db.SaveChangesAsync();
	}

	private static Task<HttpResponseMessage> ResetAsync(HttpClient client, string password) =>
		client.PostAsJsonAsync("/api/reset-password", new { email = Email, code = Code, password, confirmPassword = password });

	private sealed class ResetReadBarrier : DbCommandInterceptor
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

	private sealed class LoginIssuanceBarrier : DbCommandInterceptor
	{
		public int SessionId { get; private set; }
		public TaskCompletionSource Issuing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource ResetClaiming { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
			CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
		{
			if (command.CommandText.Contains("INSERT INTO [RefreshTokens]"))
			{
				SessionId = ((SqlConnection)command.Connection!).ServerProcessId;
				Issuing.TrySetResult();
				await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
			}
			return result;
		}

		public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
			CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			if (eventData.CommandSource == CommandSource.ExecuteUpdate && command.CommandText.Contains("[PasswordHash]"))
				ResetClaiming.TrySetResult();
			return ValueTask.FromResult(result);
		}
	}
}
