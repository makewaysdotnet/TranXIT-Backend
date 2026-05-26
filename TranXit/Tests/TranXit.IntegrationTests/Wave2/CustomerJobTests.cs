using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave2;

public sealed class CustomerJobTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-CUST-2.CreateJobHappy201")]
	public async Task CreateJobHappy201()
	{
		// UC-CUST-2
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PostAsJsonAsync("/api/jobs", CreateJobPayload());

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var result = await response.ReadApiResultAsync<JobValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.JobId.Should().BeGreaterThan(1);

		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs
			.Include(x => x.JobItems)
			.SingleOrDefaultAsync(x => x.Id == result.Value.JobId);
		job.Should().NotBeNull();
		job!.UserId.Should().Be(1);
		job.RecipientEmail.Should().Be("ops@example.com");
		job.JobItems.Should().ContainSingle(x => x.Name == "Test cartons");
	}

	[Fact(DisplayName = "T-CUST-2.CreateJobNonCustomer403")]
	public async Task CreateJobNonCustomer403()
	{
		// UC-CUST-2
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PostAsJsonAsync("/api/jobs", CreateJobPayload());

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact(DisplayName = "T-CUST-3.JobDetailOwner200")]
	public async Task JobDetailOwner200()
	{
		// UC-CUST-3
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer", email: "customer.seed@tranxit.test"));

		var response = await CourierClient.GetAsync("/api/jobs/1/details");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var result = await response.ReadApiResultAsync<JobDetailValue>();
		result.IsSuccess.Should().BeTrue();
		result.Value.Should().NotBeNull();
		result.Value!.JobId.Should().Be(1);
		result.Value.UserId.Should().Be(1);
		result.Value.CustomerName.Should().Be("Customer 1");
	}

	[Fact(DisplayName = "T-CUST-3.JobDetailCrossCustomer403")]
	public async Task JobDetailCrossCustomer403()
	{
		// UC-CUST-3
		CourierClient.AuthenticateAs(Tokens.ForUser(4, "Customer"));

		var response = await CourierClient.GetAsync("/api/jobs/1/details");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	internal static object CreateJobPayload()
		=> new
		{
			courierModeId = 1,
			cargoModeId = 1,
			itemTypeId = 1,
			originCountryId = 1,
			destinationCountryId = 2,
			originCityId = 1,
			destinationCityId = 3,
			originAddress = "Warehouse 12, Port Qasim",
			destinationAddress = "Hamburg port terminal",
			recipientContact = "+49 40 222222",
			recipientName = "Operations Desk",
			recipientEmail = "ops@example.com",
			pickupDateUtc = DateTime.UtcNow.AddDays(3),
			expiryDateUtc = DateTime.UtcNow.AddDays(6),
			jobItems = new[]
			{
				new
				{
					itemName = "Test cartons",
					dimensions = "40ft container",
					description = "Integration create-job item",
					quantity = 12,
					itemTypeId = 1,
					weight = 850.0,
					declaredValue = 50000.0
				}
			}
		};
}
