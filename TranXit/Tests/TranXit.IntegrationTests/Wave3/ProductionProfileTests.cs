extern alias AccountService;
extern alias CourierJobService;

using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;
using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TranXit.IntegrationTests.Infrastructure;
using TranXit.IntegrationTests.Wave2;

namespace TranXit.IntegrationTests.Wave3;

[Collection(IntegrationTestCollection.Name)]
public sealed class ProductionProfileTests(SqlContainerFixture fixture)
{
	[Fact(DisplayName = "T-NFR-7.FreshProdDbSupportsRegistration")]
	public async Task FreshProdDbSupportsRegistration()
	{
		// UC-NFR-7
		var accountConnectionString =
			fixture.BuildTemporaryConnectionString("TranXit_Account_ProductionProfile");
		var courierConnectionString =
			fixture.BuildTemporaryConnectionString("TranXit_Courier_ProductionProfile");

		await using var accountFactory =
			new ProductionProfileAccountServiceFactory(accountConnectionString);
		using var accountClient = accountFactory.CreateClient();

		await using var courierFactory =
			new ProductionProfileCourierJobServiceFactory(courierConnectionString);
		using var courierClient = courierFactory.CreateClient();

		accountFactory.Services
			.GetRequiredService<IHostEnvironment>()
			.EnvironmentName
			.Should()
			.Be(Environments.Production);
		courierFactory.Services
			.GetRequiredService<IHostEnvironment>()
			.EnvironmentName
			.Should()
			.Be(Environments.Production);

		const string email = "fresh-production-customer@tranxit.test";
		const string password = "Password1!";
		var registerResponse = await accountClient.PostAsJsonAsync("/api/register", new
		{
			email,
			password,
			confirmPassword = password,
			username = "Fresh Production Customer",
			phone = "+92 300 0000000",
			role = "Customer"
		});

		registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		var registerResult = await registerResponse.ReadApiResultAsync<LoginValue>();
		registerResult.IsSuccess.Should().BeTrue();
		registerResult.Value!.DevelopmentVerificationCode.Should().BeNull();

		var verifyResponse = await accountClient.PostAsJsonAsync("/api/verify-code", new
		{
			email,
			code = DeterministicUtils.VerificationCode
		});
		verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

		var loginResponse = await accountClient.PostAsJsonAsync("/api/login", new
		{
			email,
			password
		});
		loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
		var loginResult = await loginResponse.ReadApiResultAsync<LoginValue>();
		loginResult.IsSuccess.Should().BeTrue();
		loginResult.Value!.IsEmailVerified.Should().BeTrue();
		loginResult.Value.Role.Should().Be("Customer");
		loginResult.Value.Token.Should().NotBeNullOrWhiteSpace();

		courierClient.AuthenticateAs(loginResult.Value.Token!);
		var createJobResponse = await courierClient.PostAsJsonAsync(
			"/api/jobs",
			CustomerJobTests.CreateJobPayload());
		createJobResponse.StatusCode.Should().Be(HttpStatusCode.Created);
		var jobResult = await createJobResponse.ReadApiResultAsync<JobValue>();

		var accountOptions = new DbContextOptionsBuilder<AccountDbContext>()
			.UseSqlServer(accountConnectionString)
			.Options;
		await using var accountDb = new AccountDbContext(accountOptions);
		(await accountDb.Roles.CountAsync()).Should().Be(4);
		(await accountDb.Users.SingleAsync()).Email.Should().Be(email);

		var courierOptions = new DbContextOptionsBuilder<CourierJobDbContext>()
			.UseSqlServer(courierConnectionString)
			.Options;
		await using var courierDb = new CourierJobDbContext(courierOptions);
		(await courierDb.JobStatuses.CountAsync()).Should().Be(7);
		(await courierDb.Jobs.SingleAsync()).Id.Should().Be(jobResult.Value!.JobId);
	}
}
