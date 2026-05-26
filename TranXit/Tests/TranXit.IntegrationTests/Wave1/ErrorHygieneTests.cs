using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class ErrorHygieneTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-2.ErrorHygiene")]
	public async Task ErrorHygiene()
	{
		// UC-NFR-2
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/bids", new
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
					deliveryDate = (DateTime?)null,
					total = 1000.0,
					bidProposalItems = Array.Empty<object>()
				}
			}
		});

		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		var body = await response.ReadBodyAsync();
		body.Should().Contain("correlationId");
		body.Should().NotContain("InvalidOperationException");
		body.Should().NotContain("Nullable object must have a value");
		body.Should().NotContain(" at ");
	}
}
