extern alias AccountService;
extern alias CourierJobService;

using System.Collections.Concurrent;
using System.Data.Common;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharedServicesManager.Contracts.User;
using TranXit.IntegrationTests.Infrastructure;

using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;
using CourierJobProgram = CourierJobService::Program;
using JobsHelper = CourierJobService::CourierJobService.Helpers.JobsHelper;
using UserConsumer = AccountService::AccountService.Consumers.UserConsumer;

namespace TranXit.IntegrationTests.Wave1;

public sealed class CustomerBidAwardIntegrityTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-CUST-5.AcceptBidIdenticalRetry")]
	public async Task AcceptBidIdenticalRetry()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		var beforeRetry = await ReadStateAsync();

		using var retry = await AwardAsync();

		await AssertAcceptedAsync(retry, 10);
		(await ReadStateAsync()).Should().Be(beforeRetry);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidRetryPreservesStoredAmount")]
	public async Task AcceptBidRetryPreservesStoredAmount()
	{
		// UC-CUST-5
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			// A persisted legacy price is authoritative even when it differs from the proposal.
			await db.Biddings.Where(bid => bid.Id == 10)
				.ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.TotalAmount, 1199.875));
		}
		var beforeRetry = await ReadStateAsync();

		using var retry = await AwardAsync();

		await AssertAcceptedAsync(retry, 10);
		(await ReadStateAsync()).Should().Be(beforeRetry);
	}

	[Theory(DisplayName = "T-CUST-5.AcceptBidCompetingSequential400")]
	[InlineData(11, 101)]
	[InlineData(10, 100)]
	public async Task AcceptBidCompetingSequential400(int bidId, int proposalId)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		var beforeCompetingAward = await ReadStateAsync();

		using var competing = await AwardAsync(bidId, proposalId);

		await AssertRejectedAsync(competing);
		(await ReadStateAsync()).Should().Be(beforeCompetingAward);
	}

	[Theory(DisplayName = "T-CUST-5.AcceptBidLostOrStale400")]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(6)]
	[InlineData(7)]
	public async Task AcceptBidLostOrStale400(int bidStatus)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			await db.Biddings.Where(bid => bid.Id == 10)
				.ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.JobStatusId, bidStatus));
		}
		var beforeAward = await ReadStateAsync();

		using var response = await AwardAsync();

		await AssertRejectedAsync(response);
		(await ReadStateAsync()).Should().Be(beforeAward);
	}

	[Theory(DisplayName = "T-CUST-5.AcceptBidIneligibleJob400")]
	[InlineData(2, false, false)]
	[InlineData(null, false, false)]
	[InlineData(null, true, false)]
	[InlineData(3, false, false)]
	[InlineData(5, false, true)]
	public async Task AcceptBidIneligibleJob400(int? jobStatus, bool awarded, bool expired)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			var job = await db.Jobs.SingleAsync();
			job.JobStatusId = jobStatus;
			job.IsJobStatusFromBid = awarded;
			if (expired)
			{
				job.ExpiryDateUtc = DateTime.UtcNow.AddMinutes(-1);
			}
			await db.SaveChangesAsync();
		}
		var beforeAward = await ReadStateAsync();

		using var response = await AwardAsync();

		await AssertRejectedAsync(response);
		(await ReadStateAsync()).Should().Be(beforeAward);
	}

	[Theory(DisplayName = "T-CUST-5.AcceptBidRetryPreservesLifecycle")]
	[InlineData(3)]
	[InlineData(6)]
	[InlineData(7)]
	public async Task AcceptBidRetryPreservesLifecycle(int lifecycle)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));
		foreach (var status in new[] { 6, 7 }.Where(status => status <= lifecycle))
		{
			using var progressed = await CourierClient.PutAsJsonAsync("/api/bids/status", new
			{
				bidId = 10, bidProposalId = 102, status
			});
			await AssertAcceptedAsync(progressed, 10);
		}
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			await db.Jobs.Where(job => job.Id == 1)
				.ExecuteUpdateAsync(setters => setters.SetProperty(job => job.ExpiryDateUtc, DateTime.UtcNow.AddMinutes(-1)));
		}
		var beforeRetry = await ReadStateAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		using var retry = await AwardAsync();
		using var competing = await AwardAsync(11, 101);

		await AssertAcceptedAsync(retry, 10);
		await AssertRejectedAsync(competing);
		(await ReadStateAsync()).Should().Be(beforeRetry);
		await using var verificationDb = Fixture.CreateCourierJobDbContext();
		(await verificationDb.Biddings.FindAsync(10))!.JobStatusId.Should().Be(lifecycle);
	}

	[Theory(DisplayName = "T-NFR-3.AcceptBidRoleAndLifecycleGuards")]
	[InlineData(4, "Customer", 3)]
	[InlineData(2, "Courier", 3)]
	[InlineData(1, "Customer", 6)]
	[InlineData(5, "Courier", 6)]
	[InlineData(2, "Courier", 7)]
	public async Task AcceptBidRoleAndLifecycleGuards(int userId, string role, int status)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		var beforeRequest = await ReadStateAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(userId, role));

		using var response = await CourierClient.PutAsJsonAsync("/api/bids/status", new
		{
			bidId = 10, bidProposalId = 102, status
		});

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await ReadStateAsync()).Should().Be(beforeRequest);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidForeignProposal400")]
	public async Task AcceptBidForeignProposal400()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		var beforeAward = await ReadStateAsync();

		using var response = await AwardAsync(10, 101);

		await AssertRejectedAsync(response);
		(await ReadStateAsync()).Should().Be(beforeAward);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidRetainsProposalHistory")]
	public async Task AcceptBidRetainsProposalHistory()
	{
		// UC-CUST-5, E2E-4
		await SeedProposalHistoryAsync();
		var proposalHistory = await ReadProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);

		(await ReadProposalHistoryAsync()).Should().Be(proposalHistory);
		await AssertWinnerAsync(10, 102);
		var result = await ReadOffersAsync();
		result.IsSuccess.Should().BeTrue();
		var offers = result.Value.GetProperty("items").EnumerateArray().ToArray();
		var winner = offers.Single(offer => offer.GetProperty("bidId").GetInt32() == 10);
		winner.GetProperty("courierName").GetString().Should().Be("Seed Courier");
		winner.GetProperty("acceptedBidProposalId").GetInt32().Should().Be(102);
		winner.GetProperty("bidProposalId").GetInt32().Should().Be(102);
		winner.GetProperty("bidStatusId").GetInt32().Should().Be(3);
		winner.GetProperty("bidProposalIds").EnumerateArray().Select(id => id.GetInt32()).Should().BeEquivalentTo([100, 102]);
		winner.GetProperty("bidProposals").EnumerateArray().Select(proposal => proposal.GetProperty("total").GetDouble())
			.Should().BeEquivalentTo([1000.0, 1200.0]);
		offers.Should().OnlyContain(offer => !offer.GetProperty("canAccept").GetBoolean() && offer.GetProperty("isJobAwarded").GetBoolean());
		var loser = offers.Single(offer => offer.GetProperty("bidId").GetInt32() == 11);
		loser.GetProperty("courierName").GetString().Should().Be("Second Courier");
		loser.GetProperty("acceptedBidProposalId").ValueKind.Should().Be(JsonValueKind.Null);
		loser.GetProperty("bidProposalIds").GetArrayLength().Should().Be(2);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidSelectedDeliveryDate")]
	public async Task AcceptBidSelectedDeliveryDate()
	{
		// UC-CUST-5, E2E-4
		await SeedProposalHistoryAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.Include(job => job.Biddings).ThenInclude(bid => bid.BiddingProposals).SingleAsync();
		var selected = job.Biddings.SelectMany(bid => bid.BiddingProposals).Single(proposal => proposal.Id == 102);
		var earlier = job.Biddings.SelectMany(bid => bid.BiddingProposals).Single(proposal => proposal.Id == 100);
		earlier.DeliveryDateUtc.Should().BeBefore(selected.DeliveryDateUtc!.Value);
		JobsHelper.GetJobDeliveryDate(job, job.Biddings, 2).Should().Be(selected.DeliveryDateUtc);
		JobsHelper.GetJobDeliveryDate(job, job.Biddings, null).Should().Be(selected.DeliveryDateUtc);

		using var response = await CourierClient.GetAsync("/api/jobs/1");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<JsonElement>();
		result.IsSuccess.Should().BeTrue();
		result.Value.EnumerateArray().Single().GetProperty("deliveryDateUtc").GetDateTime().Should().Be(selected.DeliveryDateUtc);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidUnknownLegacyHistoryNotGuessed")]
	public async Task AcceptBidUnknownLegacyHistoryNotGuessed()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			await db.Jobs.ExecuteUpdateAsync(setters => setters
				.SetProperty(job => job.IsJobStatusFromBid, true).SetProperty(job => job.JobStatusId, (int?)null));
			await db.Biddings.Where(bid => bid.Id == 10)
				.ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.JobStatusId, 3));
		}
		var beforeRetry = await ReadStateAsync();
		using var retry = await AwardAsync();
		await AssertRejectedAsync(retry);
		(await ReadStateAsync()).Should().Be(beforeRetry);

		var result = await ReadOffersAsync();
		result.IsSuccess.Should().BeTrue();
		var winner = result.Value.GetProperty("items").EnumerateArray().Single(offer => offer.GetProperty("bidId").GetInt32() == 10);
		winner.GetProperty("bidProposalId").ValueKind.Should().Be(JsonValueKind.Null);
		winner.GetProperty("acceptedBidProposalId").ValueKind.Should().Be(JsonValueKind.Null);
		winner.GetProperty("canAccept").GetBoolean().Should().BeFalse();
		await using var verificationDb = Fixture.CreateCourierJobDbContext();
		var job = await verificationDb.Jobs.Include(job => job.Biddings).ThenInclude(bid => bid.BiddingProposals).SingleAsync();
		JobsHelper.GetJobDeliveryDate(job, job.Biddings, 2).Should().BeNull();
	}

	[Theory(DisplayName = "T-NFR-3.ConcurrentDifferentBidAwardsSingleWinner")]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public async Task ConcurrentDifferentBidAwardsSingleWinner(int iteration)
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		var history = await ReadProposalHistoryAsync();
		var responses = await ConcurrentAwardsAsync(identical: false, reverse: iteration % 2 == 1);
		using var first = responses[0];
		using var second = responses[1];
		responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.BadRequest]);
		var winnerResponse = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
		var winner = await winnerResponse.ReadApiResultAsync<BidValue>();
		winner.IsSuccess.Should().BeTrue();
		await AssertRejectedAsync(responses.Single(response => response.StatusCode == HttpStatusCode.BadRequest));
		await AssertWinnerAsync(winner.Value!.BidId, winner.Value.BidId == 10 ? 102 : 101);
		(await ReadProposalHistoryAsync()).Should().Be(history);
	}

	[Fact(DisplayName = "T-CUST-5.ConcurrentIdenticalAwardRetry")]
	public async Task ConcurrentIdenticalAwardRetry()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		var history = await ReadProposalHistoryAsync();
		var responses = await ConcurrentAwardsAsync(identical: true);
		using var first = responses[0];
		using var second = responses[1];
		await AssertAcceptedAsync(first, 10);
		await AssertAcceptedAsync(second, 10);
		await AssertWinnerAsync(10, 102);
		(await ReadProposalHistoryAsync()).Should().Be(history);
	}

	[Fact(DisplayName = "T-NFR-3.AcceptBidPersistenceFailureRollsBack")]
	public async Task AcceptBidPersistenceFailureRollsBack()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		var beforeAward = await ReadStateAsync();
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Biddings] ADD CONSTRAINT [CK_TestAwardSaveFailure] CHECK ([JobStatusId] <> 3);");
		try
		{
			using var response = await AwardAsync();
			var result = await AssertRejectedAsync(response);
			result.Error.Should().Contain("Unable to accept bid. Reload the job and try again.");
			(await ReadStateAsync()).Should().Be(beforeAward);
		}
		finally
		{
			await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Biddings] DROP CONSTRAINT [CK_TestAwardSaveFailure];");
		}
		using var retry = await AwardAsync();
		await AssertAcceptedAsync(retry, 10);
		await AssertWinnerAsync(10, 102);
	}

	[Fact(DisplayName = "T-NFR-3.StaleJobWriteCannotClearAward")]
	public async Task StaleJobWriteCannotClearAward()
	{
		// UC-CUST-5, UC-NFR-3
		await SeedProposalHistoryAsync();
		await using var staleDb = Fixture.CreateCourierJobDbContext();
		var staleJob = await staleDb.Jobs.SingleAsync();
		using var accepted = await AwardAsync();
		await AssertAcceptedAsync(accepted, 10);
		var beforeStaleWrite = await ReadStateAsync();
		staleJob.Comments = "Stale pre-award job write";
		staleDb.Jobs.Update(staleJob);

		Func<Task> save = () => staleDb.SaveChangesAsync();

		await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
		(await ReadStateAsync()).Should().Be(beforeStaleWrite);
	}

	private async Task<HttpResponseMessage[]> ConcurrentAwardsAsync(bool identical, bool reverse = false)
	{
		var barrier = new AwardClaimBarrier();
		await using var factory = new AwardServiceFactory(Fixture, barrier);
		using var client = factory.CreateClient();
		client.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		using (var scope = factory.Services.CreateScope())
		{
			var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<CourierJobDbContext>>();
			options.FindExtension<CoreOptionsExtension>()!.Interceptors.Should().Contain(barrier,
				"both endpoint requests must use the SQL claim barrier");
		}
		var firstBid = reverse ? 11 : 10;
		var firstProposal = reverse ? 101 : 102;
		var secondBid = identical ? firstBid : reverse ? 10 : 11;
		var secondProposal = identical ? firstProposal : reverse ? 102 : 101;
		var responses = await Task.WhenAll(
			AwardAsync(firstBid, firstProposal, client),
			AwardAsync(secondBid, secondProposal, client));
		var responseBodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
		responses.Select(response => response.StatusCode).Should().BeEquivalentTo(
			identical ? [HttpStatusCode.OK, HttpStatusCode.OK] : new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest },
			string.Join(Environment.NewLine, responseBodies));
		barrier.Arrivals.Should().Be(2);
		barrier.AffectedRows.Should().BeEquivalentTo([1, 0], "the conditional SQL claim must select exactly one writer");
		return responses;
	}

	private async Task<ApiResult<JsonElement>> ReadOffersAsync()
	{
		await using var factory = new AwardServiceFactory(Fixture);
		using var client = factory.CreateClient();
		client.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		// Testing hosts have separate in-memory transports; keep the real lookup consumer on the requesting bus.
		var endpoint = factory.Services.GetRequiredService<IReceiveEndpointConnector>()
			.ConnectReceiveEndpoint($"batch4-users-{Guid.NewGuid():N}", (_, configuration) =>
				configuration.Handler<CheckUser>(async context =>
				{
					await using var db = Fixture.CreateAccountDbContext();
					await new UserConsumer(db).Consume(context);
				}));
		try
		{
			await endpoint.Ready.WaitAsync(TimeSpan.FromSeconds(10));
			using var response = await client.GetAsync("/api/bids/1?page=1&pageSize=10");
			response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
			return await response.ReadApiResultAsync<JsonElement>();
		}
		finally
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			await endpoint.StopAsync(timeout.Token);
		}
	}

	private Task<HttpResponseMessage> AwardAsync(int bidId = 10, int proposalId = 102, HttpClient? client = null)
		=> (client ?? CourierClient).PutAsJsonAsync("/api/bids/status", new { bidId, bidProposalId = proposalId, status = 3 });

	private static async Task AssertAcceptedAsync(HttpResponseMessage response, int bidId)
	{
		response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value!.BidId.Should().Be(bidId);
		result.Error.Should().BeEmpty();
	}

	private static async Task<ApiResult<BidValue>> AssertRejectedAsync(HttpResponseMessage response)
	{
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeFalse();
		result.Value.Should().BeNull();
		result.Error.Should().NotBeEmpty();
		return result;
	}

	private async Task AssertWinnerAsync(int bidId, int proposalId)
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.Include(job => job.AcceptedBidProposal).SingleAsync();
		job.AcceptedBidProposalId.Should().Be(proposalId);
		job.AcceptedBidProposal!.BiddingId.Should().Be(bidId);
		job.JobStatusId.Should().BeNull();
		job.IsJobStatusFromBid.Should().BeTrue();
		var bids = await db.Biddings.OrderBy(bid => bid.Id).ToListAsync();
		bids.Should().ContainSingle(bid => bid.JobStatusId == 3).Which.Id.Should().Be(bidId);
		bids.Single(bid => bid.Id != bidId).JobStatusId.Should().Be(4);
		bids.Single(bid => bid.Id != bidId).TotalAmount.Should().Be(bidId == 10 ? 1375 : 1175);
		bids.Single(bid => bid.Id == bidId).TotalAmount.Should().Be(proposalId == 102 ? 1200 : 1300);
		(await db.BiddingProposals.CountAsync()).Should().Be(4);
		(await db.BiddingProposalItems.CountAsync()).Should().Be(4);
	}

	private async Task<string> ReadStateAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		return JsonSerializer.Serialize(new
		{
			Jobs = await db.Jobs.OrderBy(job => job.Id).Select(job => new
			{
				job.Id, job.UserId, job.JobStatusId, job.IsJobStatusFromBid, job.AcceptedBidProposalId, job.ExpiryDateUtc, job.Comments
			}).ToListAsync(),
			Bids = await db.Biddings.OrderBy(bid => bid.Id).Select(bid => new
			{
				bid.Id, bid.JobId, bid.UserId, bid.JobStatusId, bid.TotalAmount, bid.IsInsurancePolicy,
				bid.PickupCharges, bid.HandlingCharges, bid.CustomClearanceCharges
			}).ToListAsync(),
			History = await ReadProposalHistoryAsync()
		});
	}

	private async Task<string> ReadProposalHistoryAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		return JsonSerializer.Serialize(new
		{
			Proposals = await db.BiddingProposals.OrderBy(proposal => proposal.Id).Select(proposal => new
			{
				proposal.Id, proposal.BiddingId, proposal.IsBaseBid, proposal.Total, proposal.DeliveryDateUtc, proposal.DeliveryTypeId
			}).ToListAsync(),
			Items = await db.BiddingProposalItems.OrderBy(item => item.Id).Select(item => new
			{
				item.Id, item.BiddingProposalId, item.JobItemId, item.UnitPrice, item.ItemTotal
			}).ToListAsync(),
			Charges = await db.BiddingCharges.OrderBy(charge => charge.Id).Select(charge => new
			{
				charge.Id, charge.BiddingId, charge.Name, charge.Description, charge.Amount
			}).ToListAsync()
		});
	}

	private async Task SeedProposalHistoryAsync()
	{
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));
		await using (var accountDb = Fixture.CreateAccountDbContext())
		{
			await accountDb.Database.ExecuteSqlRawAsync("""
				SET IDENTITY_INSERT [Users] ON;
				INSERT INTO [Users] ([Id], [Email], [NormalizedEmail], [PasswordHash], [Username], [RoleId], [IsEmailVerified], [Phone])
				SELECT 5, 'courier.second@tranxit.test', 'courier.second@tranxit.test', [PasswordHash], 'Second Courier', 2, 1, '+920000000005'
				FROM [Users] WHERE [Id] = 2;
				SET IDENTITY_INSERT [Users] OFF;
				""");
		}
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Biddings] ON;
			INSERT INTO [Biddings] ([Id], [UserId], [JobId], [TotalAmount], [IsInsurancePolicy], [PickupCharges], [HandlingCharges], [CustomClearanceCharges], [JobStatusId])
			VALUES (10, 2, 1, 1175, 1, 100, 50, 25, NULL), (11, 5, 1, 1375, 1, 150, 100, 25, 1);
			SET IDENTITY_INSERT [Biddings] OFF;
			SET IDENTITY_INSERT [BiddingProposals] ON;
			INSERT INTO [BiddingProposals] ([Id], [BiddingId], [DeliveryTypeId], [IsBaseBid], [DeliveryDateUtc], [Total])
			VALUES (100, 10, 1, 1, DATEADD(day, 3, SYSUTCDATETIME()), 1000),
			       (102, 10, 2, 0, DATEADD(day, 8, SYSUTCDATETIME()), 1200),
			       (101, 11, 1, 1, DATEADD(day, 6, SYSUTCDATETIME()), 1300),
			       (103, 11, 3, 0, DATEADD(day, 9, SYSUTCDATETIME()), 1500);
			SET IDENTITY_INSERT [BiddingProposals] OFF;
			INSERT INTO [BiddingProposalItems] ([BiddingProposalId], [JobItemId], [UnitPrice], [ItemTotal])
			VALUES (100, 1, 100, 1000), (102, 1, 120, 1200), (101, 1, 130, 1300), (103, 1, 150, 1500);
			INSERT INTO [BiddingCharges] ([BiddingId], [Name], [Description], [Amount])
			VALUES (10, 'Test charge', 'Retained quoted charge', 25), (11, 'Other charge', 'Retained losing charge', 35);
			""");
	}

	private sealed class AwardServiceFactory : WebApplicationFactory<CourierJobProgram>
	{
		private readonly SqlContainerFixture _fixture;
		private readonly AwardClaimBarrier? _barrier;
		private IServiceProvider? _hostServices;

		public AwardServiceFactory(SqlContainerFixture fixture, AwardClaimBarrier? barrier = null)
		{
			_fixture = fixture;
			_barrier = barrier;
			TestConfiguration.ApplyToProcessEnvironment(fixture.CourierJobConnectionString);
		}

		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			TestConfiguration.ApplyToProcessEnvironment(_fixture.CourierJobConnectionString);
			builder.UseEnvironment("Testing");
			builder.UseDefaultServiceProvider((_, options) =>
			{
				options.ValidateScopes = true;
				options.ValidateOnBuild = true;
			});
			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(TestConfiguration.ForService(_fixture.CourierJobConnectionString)));
			if (_barrier is not null)
			{
				builder.ConfigureTestServices(services =>
				{
					// EF Core 8 keeps the first options registration, so replace it before adding the interceptor.
					services.RemoveAll<DbContextOptions<CourierJobDbContext>>();
					services.AddDbContext<CourierJobDbContext>(options => options
						.UseSqlServer(_fixture.CourierJobConnectionString).AddInterceptors(_barrier));
				});
			}
		}

		protected override IHost CreateHost(IHostBuilder builder)
		{
			var host = base.CreateHost(builder);
			_hostServices = host.Services;
			return host;
		}

		public override async ValueTask DisposeAsync()
		{
			if (_hostServices is not null)
			{
				await MassTransitTestTeardown.StopBusAsync(_hostServices);
			}
			try
			{
				await base.DisposeAsync();
			}
			catch (Exception exception) when (MassTransitTestTeardown.IsBenignTeardownRace(exception))
			{
			}
		}
	}

	private sealed class AwardClaimBarrier : DbCommandInterceptor
	{
		private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _arrivals;
		public int Arrivals => _arrivals;
		public ConcurrentQueue<int> AffectedRows { get; } = new();

		public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
		{
			if (eventData.CommandSource == CommandSource.ExecuteUpdate)
			{
				if (Interlocked.Increment(ref _arrivals) == 2)
				{
					_bothReady.TrySetResult();
				}
				await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
			}
			return result;
		}

		public override ValueTask<int> NonQueryExecutedAsync(
			DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
		{
			if (eventData.CommandSource == CommandSource.ExecuteUpdate)
			{
				AffectedRows.Enqueue(result);
			}
			return ValueTask.FromResult(result);
		}
	}
}
