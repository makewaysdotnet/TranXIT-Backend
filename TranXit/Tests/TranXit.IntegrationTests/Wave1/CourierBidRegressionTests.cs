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
	}

	internal static object BidPayload(int jobId, bool isBaseBid = true, DateTime? deliveryDate = null)
		=> new
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
					total = 1000.0,
					bidProposalItems = Array.Empty<object>()
				}
			}
		};
}
