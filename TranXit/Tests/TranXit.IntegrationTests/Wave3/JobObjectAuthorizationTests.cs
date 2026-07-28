extern alias CourierJobService;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

using Bidding = CourierJobService::CourierJobService.Database.Bidding;
using Job = CourierJobService::CourierJobService.Database.Job;

namespace TranXit.IntegrationTests.Wave3;

public sealed class JobObjectAuthorizationTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-3.UpdateJobStatusCrossUser403")]
	public async Task UpdateJobStatusCrossUser403()
	{
		// UC-NFR-3
		await ExpireJobAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(4, "Customer"));

		var response = await CourierClient.PutAsJsonAsync(
			"/api/jobs/status",
			new { jobId = 1, status = 2 });

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await ReadJobAsync()).JobStatusId.Should().Be(5);
	}

	[Fact(DisplayName = "T-NFR-3.UpdateJobStatusCourier403")]
	public async Task UpdateJobStatusCourier403()
	{
		// UC-NFR-3
		await ExpireJobAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var response = await CourierClient.PutAsJsonAsync(
			"/api/jobs/status",
			new { jobId = 1, status = 2 });

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await ReadJobAsync()).JobStatusId.Should().Be(5);
	}

	[Fact(DisplayName = "T-CUST-3.UpdateExpiredOwnJobClosed")]
	public async Task UpdateExpiredOwnJobClosed()
	{
		// UC-CUST-3, UC-NFR-3
		await ExpireJobAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PutAsJsonAsync(
			"/api/jobs/status",
			new { jobId = 1, status = 2 });

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadJobAsync()).JobStatusId.Should().Be(2);
	}

	[Fact(DisplayName = "T-CUST-3.UpdateJobStatusInvalidTransition400")]
	public async Task UpdateJobStatusInvalidTransition400()
	{
		// UC-CUST-3, UC-NFR-3
		CourierClient.AuthenticateAs(Tokens.ForUser(1, "Customer"));

		var response = await CourierClient.PutAsJsonAsync(
			"/api/jobs/status",
			new { jobId = 1, status = 7 });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await ReadJobAsync()).JobStatusId.Should().Be(5);
	}

	[Fact(DisplayName = "T-NFR-3.CourierOpenJobVisible")]
	public async Task CourierOpenJobVisible()
	{
		// UC-COUR-2, UC-NFR-3
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var statsResponse = await CourierClient.GetAsync("/api/jobs/1/bid-stats");
		var listResponse = await CourierClient.GetAsync("/api/jobs?page=1&pageSize=20");

		statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadListedJobIdsAsync(listResponse)).Should().Contain(1);
	}

	[Fact(DisplayName = "T-NFR-3.CourierCompletedJobHidden")]
	public async Task CourierCompletedJobHidden()
	{
		// UC-COUR-2, UC-COUR-3, UC-NFR-3
		await SetClosedJobAsync();
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var detailResponse = await CourierClient.GetAsync("/api/jobs/1/details");
		var statsResponse = await CourierClient.GetAsync("/api/jobs/1/bid-stats");
		var listResponse = await CourierClient.GetAsync("/api/jobs?page=1&pageSize=20");

		detailResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		statsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await ReadListedJobIdsAsync(listResponse)).Should().NotContain(1);
	}

	[Fact(DisplayName = "T-NFR-3.CourierBidHistoryVisible")]
	public async Task CourierBidHistoryVisible()
	{
		// UC-COUR-2, UC-NFR-3
		await SetAwardedJobWithBidAsync(courierId: 2);
		CourierClient.AuthenticateAs(Tokens.ForUser(2, "Courier"));

		var statsResponse = await CourierClient.GetAsync("/api/jobs/1/bid-stats");
		var listResponse = await CourierClient.GetAsync("/api/jobs?page=1&pageSize=20");

		statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadListedJobIdsAsync(listResponse)).Should().Contain(1);
	}

	private async Task ExpireJobAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.FindAsync(1);
		job!.ExpiryDateUtc = DateTime.UtcNow.AddMinutes(-1);
		await db.SaveChangesAsync();
	}

	private async Task SetClosedJobAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.FindAsync(1);
		job!.JobStatusId = 2;
		job.IsJobStatusFromBid = false;
		job.ExpiryDateUtc = DateTime.UtcNow.AddMinutes(-1);
		await db.SaveChangesAsync();
	}

	private async Task SetAwardedJobWithBidAsync(int courierId)
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		var job = await db.Jobs.FindAsync(1);
		job!.JobStatusId = null;
		job.IsJobStatusFromBid = true;
		db.Biddings.Add(new Bidding
		{
			JobId = 1,
			UserId = courierId,
			TotalAmount = 1000,
			JobStatusId = 3
		});
		await db.SaveChangesAsync();
	}

	private async Task<Job> ReadJobAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		return await db.Jobs.AsNoTracking().SingleAsync(job => job.Id == 1);
	}

	private static async Task<int[]> ReadListedJobIdsAsync(HttpResponseMessage response)
	{
		using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return document.RootElement
			.GetProperty("value")
			.GetProperty("items")
			.EnumerateArray()
			.Select(item => item.GetProperty("id").GetInt32())
			.ToArray();
	}
}
