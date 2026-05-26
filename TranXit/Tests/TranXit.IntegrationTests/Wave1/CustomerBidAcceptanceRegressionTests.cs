using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class CustomerBidAcceptanceRegressionTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-CUST-5.AcceptBidHappy")]
	public async Task AcceptBidHappy()
	{
		// UC-CUST-5, E2E-4
		await SeedTwoBidsAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PutAsJsonAsync("/api/bids/status", new
		{
			bidId = 10,
			bidProposalId = 100,
			status = 3
		});

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value!.BidId.Should().Be(10);

		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.FindAsync(1);
		var winningBid = await db.Biddings.FindAsync(10);
		var losingBid = await db.Biddings.FindAsync(11);

		job!.JobStatusId.Should().BeNull();
		job.IsJobStatusFromBid.Should().BeTrue();
		winningBid!.JobStatusId.Should().Be(3);
		winningBid.TotalAmount.Should().Be(1000);
		losingBid!.JobStatusId.Should().Be(4);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidNonOwner403")]
	public async Task AcceptBidNonOwner403()
	{
		// UC-CUST-5
		await SeedTwoBidsAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(4, "Customer"));

		var response = await CourierClient.PutAsJsonAsync("/api/bids/status", new
		{
			bidId = 10,
			bidProposalId = 100,
			status = 3
		});

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact(DisplayName = "T-CUST-5.AcceptBidUnknownProposal400")]
	public async Task AcceptBidUnknownProposal400()
	{
		// UC-CUST-5
		await SeedTwoBidsAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PutAsJsonAsync("/api/bids/status", new
		{
			bidId = 10,
			bidProposalId = 9999,
			status = 3
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var result = await response.ReadApiResultAsync<BidValue>();
		result.IsSuccess.Should().BeFalse();
		result.Error.Should().Contain(error => error.Contains("Bid proposal not found", StringComparison.OrdinalIgnoreCase));
	}

	private async Task SeedTwoBidsAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Biddings] ON;
			INSERT INTO [Biddings]
				([Id], [UserId], [JobId], [TotalAmount], [IsInsurancePolicy], [PickupCharges],
				 [HandlingCharges], [CustomClearanceCharges], [JobStatusId])
			VALUES
				(10, 2, 1, 1175, 1, 100, 50, 25, 1),
				(11, 5, 1, 1375, 1, 150, 100, 25, 1);
			SET IDENTITY_INSERT [Biddings] OFF;

			SET IDENTITY_INSERT [BiddingProposals] ON;
			INSERT INTO [BiddingProposals]
				([Id], [BiddingId], [DeliveryTypeId], [IsBaseBid], [DeliveryDateUtc], [Total])
			VALUES
				(100, 10, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 1000),
				(101, 11, 1, 1, DATEADD(day, 6, SYSUTCDATETIME()), 1200);
			SET IDENTITY_INSERT [BiddingProposals] OFF;
			""");
	}
}
