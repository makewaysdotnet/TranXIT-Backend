using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class CourierBidRegressionTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-COUR-4.PlaceBidMissingJob")]
	public async Task PlaceBidMissingJob()
	{
		// UC-COUR-4, E2E-3
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(jobId: 99999));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Job not found", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidMissingBaseProposal")]
	public async Task PlaceBidMissingBaseProposal()
	{
		// UC-COUR-4
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(
			jobId: 1,
			isBaseBid: false));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("base bid proposal", StringComparison.OrdinalIgnoreCase));
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidHappy")]
	public async Task PlaceBidHappy()
	{
		// UC-COUR-4
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(jobId: 1));

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.BidId.Should().BeGreaterThan(0);

		await using var db = Fixture.CreateCourierJobDbContext();
		var bid = await db.Biddings
			.Include(x => x.BiddingProposals)
			.SingleOrDefaultAsync(x => x.Id == result.Value.BidId);
		bid.Should().NotBeNull();
		bid!.JobId.Should().Be(1);
		bid.UserId.Should().Be(2);
		bid.BiddingProposals.Should().ContainSingle(x => x.IsBaseBid == true);
		bid.TotalAmount.Should().Be(175);
		bid.BiddingProposals.Single().Total.Should().Be(bid.TotalAmount);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidSameJobItemHappy")]
	public async Task PlaceBidSameJobItemHappy()
	{
		// UC-COUR-4, UC-NFR-3
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync(
			"/api/bids",
			BidPayload(jobId: 1, jobItemId: 1));

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var result = await response.ReadApiResultAsync<BidValue>();
		await using var db = Fixture.CreateCourierJobDbContext();
		var proposalItem = await db.BiddingProposalItems
			.SingleOrDefaultAsync(item => item.BiddingProposal!.BiddingId == result.Value!.BidId);
		proposalItem.Should().NotBeNull();
		proposalItem!.JobItemId.Should().Be(1);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidCrossJobItemRejected")]
	public async Task PlaceBidCrossJobItemRejected()
	{
		// UC-COUR-4, UC-NFR-3
		await SeedOtherJobItemAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync(
			"/api/bids",
			BidPayload(jobId: 1, jobItemId: 2));

		await AssertRejectedWithoutBidAsync(response);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidMissingJobItemRejected")]
	public async Task PlaceBidMissingJobItemRejected()
	{
		// UC-COUR-4, UC-NFR-3
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync(
			"/api/bids",
			BidPayload(jobId: 1, jobItemId: 99999));

		await AssertRejectedWithoutBidAsync(response);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidClosedJobRejected")]
	public async Task PlaceBidClosedJobRejected()
	{
		// UC-COUR-4, UC-NFR-3
		await SetJobLifecycleAsync(jobStatusId: 2, isJobStatusFromBid: false);
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(jobId: 1));

		await AssertRejectedWithoutBidAsync(response);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidAwardedJobRejected")]
	public async Task PlaceBidAwardedJobRejected()
	{
		// UC-COUR-4, UC-NFR-3
		await SetJobLifecycleAsync(jobStatusId: null, isJobStatusFromBid: true);
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(jobId: 1));

		await AssertRejectedWithoutBidAsync(response);
	}

	[Fact(DisplayName = "T-COUR-4.PlaceBidExpiredJobRejected")]
	public async Task PlaceBidExpiredJobRejected()
	{
		// UC-COUR-4
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			var job = await db.Jobs.FindAsync(1);
			job!.ExpiryDateUtc = DateTime.UtcNow.AddMinutes(-1);
			await db.SaveChangesAsync();
		}
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload(jobId: 1));

		await AssertRejectedWithoutBidAsync(response);
	}

	internal static object BidPayload(
		int jobId,
		bool isBaseBid = true,
		DateTime? deliveryDate = null,
		int? jobItemId = null)
	{
		var proposalItems = jobItemId.HasValue
			? new object[]
			{
				new
				{
					jobItemId,
					unitPrice = 100.0,
					itemTotal = 2400.0
				}
			}
			: [];

		return new
		{
			jobId,
			isInsurancePolicy = true,
			pickupCharges = 100.0,
			handlingCharges = 50.0,
			customClearanceCharges = 25.0,
			bidCustomCharges = Array.Empty<object>(),
			bidProposals = new[]
			{
				new
				{
					deliveryTypeId = 1,
					isBaseBid,
					deliveryDate = deliveryDate ?? DateTime.UtcNow.AddDays(5),
					total = jobItemId.HasValue ? 2575.0 : 175.0,
					bidProposalItems = proposalItems
				}
			}
		};
	}

	private async Task AssertRejectedWithoutBidAsync(HttpResponseMessage response)
	{
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		await using var db = Fixture.CreateCourierJobDbContext();
		(await db.Biddings.CountAsync()).Should().Be(0);
	}

	private async Task SetJobLifecycleAsync(int? jobStatusId, bool isJobStatusFromBid)
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.FindAsync(1);
		job!.JobStatusId = jobStatusId;
		job.IsJobStatusFromBid = isJobStatusFromBid;
		job.ExpiryDateUtc = DateTime.UtcNow.AddDays(1);
		await db.SaveChangesAsync();
	}

	private async Task SeedOtherJobItemAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Jobs] ON;
			INSERT INTO [Jobs]
				([Id], [UserId], [JobStatusId], [CreatedOnUtc], [JobNumber], [ExpiryDateUtc], [IsJobStatusFromBid])
			VALUES
				(2, 4, 1, SYSUTCDATETIME(), 'TX4002', DATEADD(day, 2, SYSUTCDATETIME()), 0);
			SET IDENTITY_INSERT [Jobs] OFF;

			SET IDENTITY_INSERT [JobItems] ON;
			INSERT INTO [JobItems] ([Id], [Name], [JobId], [ItemTypeId])
			VALUES (2, 'Other job item', 2, 1);
			SET IDENTITY_INSERT [JobItems] OFF;
			""");
	}
}
