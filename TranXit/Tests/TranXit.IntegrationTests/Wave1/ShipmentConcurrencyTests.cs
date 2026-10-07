extern alias CourierJobService;

using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TranXit.IntegrationTests.Infrastructure;

using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;
using CourierJobProgram = CourierJobService::Program;

namespace TranXit.IntegrationTests.Wave1;

public sealed class ShipmentConcurrencyTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-3.DelayedPickupCannotRegressDelivered")]
	public async Task DelayedPickupCannotRegressDelivered()
	{
		// UC-CUST-5, UC-COUR-5, UC-NFR-3. Pause the stale request immediately before its SQL write.
		await SeedBidAsync();
		await AwardAsync(CourierClient);
		var pause = new SqlPause(command => IsBidUpdate(command.CommandText));
		await using var factory = new InterceptedCourierFactory(Fixture, pause);
		using var delayedClient = factory.CreateClient();
		AssertInstalled(factory, pause);
		delayedClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		var delayedPickup = StatusAsync(delayedClient, 6);
		try
		{
			await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
			using var pickup = await StatusAsync(CourierClient, 6);
			pickup.StatusCode.Should().Be(HttpStatusCode.OK, await pickup.Content.ReadAsStringAsync());
			using var delivery = await StatusAsync(CourierClient, 7);
			delivery.StatusCode.Should().Be(HttpStatusCode.OK, await delivery.Content.ReadAsStringAsync());
		}
		finally
		{
			pause.Release.TrySetResult();
		}
		using var stale = await delayedPickup;
		stale.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		await using var db = Fixture.CreateCourierJobDbContext();
		var bid = await db.Biddings.SingleAsync();
		bid.JobStatusId.Should().Be(7);
		bid.TotalAmount.Should().Be(175);
		(await db.Jobs.SingleAsync()).AcceptedBidProposalId.Should().Be(100);
	}

	[Fact(DisplayName = "T-NFR-3.NewBidAndAwardSerializeAtJob")]
	public async Task NewBidAndAwardSerializeAtJob()
	{
		// UC-COUR-4, UC-CUST-5, UC-NFR-3. The job is already Bidding (the formerly unguarded path).
		await SeedBidAsync();
		var beforeInsert = new SqlPause(command => command.CommandText.Contains("INSERT INTO [Biddings]", StringComparison.Ordinal));
		await using var createFactory = new InterceptedCourierFactory(Fixture, beforeInsert);
		using var createClient = createFactory.CreateClient();
		AssertInstalled(createFactory, beforeInsert);
		createClient.AuthenticateAs(Tokens.ForUser(5, "Courier"));
		var claimAttempt = new SqlSignal(command => command.CommandText.Contains("UPDATE", StringComparison.Ordinal) &&
			command.CommandText.Contains("[Jobs]", StringComparison.Ordinal));
		await using var awardFactory = new InterceptedCourierFactory(Fixture, claimAttempt);
		using var awardClient = awardFactory.CreateClient();
		AssertInstalled(awardFactory, claimAttempt);
		awardClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var create = createClient.PostAsJsonAsync("/api/bids", CourierBidRegressionTests.BidPayload(1));
		Task<HttpResponseMessage>? award = null;
		try
		{
			await beforeInsert.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
			award = StatusAsync(awardClient, 3);
			await claimAttempt.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
			// Observe either the SQL lock wait or the old bug's completed award, not a timing-based guess.
			await WaitForBlockedOrCompletedAwardAsync(award, beforeInsert.SessionId);
		}
		finally
		{
			beforeInsert.Release.TrySetResult();
		}
		using var created = await create;
		created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
		using var accepted = await award!;
		accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
		await using var db = Fixture.CreateCourierJobDbContext();
		var bids = await db.Biddings.OrderBy(bid => bid.Id).ToListAsync();
		bids.Should().HaveCount(2);
		bids.Single(bid => bid.Id == 10).JobStatusId.Should().Be(3);
		bids.Single(bid => bid.UserId == 5).JobStatusId.Should().Be(4,
			"the award must include a concurrent bid committed before it, never leave a post-award pending bid");
	}

	private async Task WaitForBlockedOrCompletedAwardAsync(Task<HttpResponseMessage> award, int blockingSessionId)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		await using var connection = new SqlConnection(Fixture.CourierJobConnectionString);
		await connection.OpenAsync(timeout.Token);
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @session AND database_id = DB_ID();";
		command.Parameters.AddWithValue("@session", blockingSessionId);
		while (!award.IsCompleted)
		{
			if ((int)(await command.ExecuteScalarAsync(timeout.Token))! > 0) return;
			await Task.Delay(20, timeout.Token);
		}
	}

	[Theory(DisplayName = "T-NFR-3.LifecycleRequiresResolvedWinningProposal")]
	[InlineData(false, null)]
	[InlineData(true, null)]
	public async Task LifecycleRequiresResolvedWinningProposal(bool isAwarded, int? acceptedProposalId)
	{
		await SeedBidAsync();
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			await db.Jobs.ExecuteUpdateAsync(setters => setters
				.SetProperty(job => job.IsJobStatusFromBid, isAwarded)
				.SetProperty(job => job.AcceptedBidProposalId, acceptedProposalId));
			await db.Biddings.ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.JobStatusId, 3));
		}
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		using var response = await StatusAsync(CourierClient, 6);
		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		await using var check = Fixture.CreateCourierJobDbContext();
		(await check.Biddings.SingleAsync()).JobStatusId.Should().Be(3);
	}

	private async Task AwardAsync(HttpClient client)
	{
		client.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using var response = await StatusAsync(client, 3);
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
	}

	private static Task<HttpResponseMessage> StatusAsync(HttpClient client, int status)
		=> client.PutAsJsonAsync("/api/bids/status", new { bidId = 10, bidProposalId = 100, status });

	private async Task SeedBidAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			UPDATE [Jobs] SET [JobStatusId] = 5 WHERE [Id] = 1;
			SET IDENTITY_INSERT [Biddings] ON;
			INSERT INTO [Biddings] ([Id], [UserId], [JobId], [TotalAmount], [PickupCharges], [HandlingCharges], [CustomClearanceCharges])
			VALUES (10, 2, 1, 175, 100, 50, 25);
			SET IDENTITY_INSERT [Biddings] OFF;
			SET IDENTITY_INSERT [BiddingProposals] ON;
			INSERT INTO [BiddingProposals] ([Id], [BiddingId], [DeliveryTypeId], [IsBaseBid], [DeliveryDateUtc], [Total])
			VALUES (100, 10, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 175);
			SET IDENTITY_INSERT [BiddingProposals] OFF;
			""");
	}

	private static bool IsBidUpdate(string sql)
		=> sql.Contains("UPDATE", StringComparison.Ordinal) && sql.Contains("[Biddings]", StringComparison.Ordinal);

	private static void AssertInstalled(InterceptedCourierFactory factory, DbCommandInterceptor interceptor)
	{
		using var scope = factory.Services.CreateScope();
		var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CourierJobDbContext>>();
		options.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().Contain(interceptor);
	}

	private sealed class SqlPause(Func<DbCommand, bool> matches) : DbCommandInterceptor
	{
		private int _arrivals;
		public int SessionId { get; private set; }
		public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private async Task PauseAsync(DbCommand command, CancellationToken cancellationToken)
		{
			if (matches(command) && Interlocked.Increment(ref _arrivals) == 1)
			{
				SessionId = ((SqlConnection)command.Connection!).ServerProcessId;
				Arrived.TrySetResult();
				await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
			}
		}
		public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
		{
			await PauseAsync(command, cancellationToken);
			return result;
		}
		public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			await PauseAsync(command, cancellationToken);
			return result;
		}
	}

	private sealed class SqlSignal(Func<DbCommand, bool> matches) : DbCommandInterceptor
	{
		public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			if (matches(command)) Arrived.TrySetResult();
			return ValueTask.FromResult(result);
		}
	}

	private sealed class InterceptedCourierFactory(SqlContainerFixture fixture, DbCommandInterceptor interceptor)
		: WebApplicationFactory<CourierJobProgram>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			TestConfiguration.ApplyToProcessEnvironment(fixture.CourierJobConnectionString);
			builder.UseEnvironment("Testing");
			builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
				TestConfiguration.ForService(fixture.CourierJobConnectionString)));
			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<DbContextOptions<CourierJobDbContext>>();
				services.AddDbContext<CourierJobDbContext>(options => options
					.UseSqlServer(fixture.CourierJobConnectionString).AddInterceptors(interceptor));
			});
		}
		public override async ValueTask DisposeAsync()
		{
			await MassTransitTestTeardown.StopBusAsync(Services);
			try { await base.DisposeAsync(); }
			catch (Exception exception) when (MassTransitTestTeardown.IsBenignTeardownRace(exception)) { }
		}
	}
}
