using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class RouteProtectionTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-AUTH-7.UnauthenticatedProtectedEndpoints401")]
	public async Task UnauthenticatedProtectedEndpoints401()
	{
		// UC-AUTH-7
		var createJobResponse = await CourierClient.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload());
		var createBidResponse = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload());
		var getUserResponse = await AccountClient.GetAsync("/api/users/1");

		createJobResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		createBidResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
		getUserResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact(DisplayName = "T-AUTH-7.WrongRoleCreateJob403")]
	public async Task WrongRoleCreateJob403()
	{
		// UC-AUTH-7
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/jobs", CustomerJobTests.CreateJobPayload());

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact(DisplayName = "T-AUTH-7.WrongRolePlaceBid403")]
	public async Task WrongRolePlaceBid403()
	{
		// UC-AUTH-7
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", BidPayload());

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	private static object BidPayload()
		=> new
		{
			jobId = 1,
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
					isBaseBid = true,
					deliveryDate = DateTime.UtcNow.AddDays(5),
					total = 1000.0,
					bidProposalItems = Array.Empty<object>()
				}
			}
		};
}
