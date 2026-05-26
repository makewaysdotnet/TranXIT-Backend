extern alias AccountService;
extern alias CourierJobService;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;
using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;

namespace TranXit.IntegrationTests.Wave3;

[Collection(IntegrationTestCollection.Name)]
public sealed class MigrationTests(SqlContainerFixture fixture)
{
	[Fact(DisplayName = "T-NFR-4.MigrationsApplyClean")]
	public async Task MigrationsApplyClean()
	{
		// UC-NFR-4
		await using var accountDb = CreateAccountContext();
		await using var courierJobDb = CreateCourierJobContext();

		await accountDb.Database.MigrateAsync();
		await courierJobDb.Database.MigrateAsync();

		await accountDb.Database.MigrateAsync();
		await courierJobDb.Database.MigrateAsync();

		(await accountDb.Roles.CountAsync()).Should().Be(0);
		(await courierJobDb.Jobs.CountAsync()).Should().Be(0);
	}

	[Fact(DisplayName = "T-NFR-4.MigrationsMatchModel")]
	public async Task MigrationsMatchModel()
	{
		// UC-NFR-4
		await using var accountDb = CreateAccountContext();
		await using var courierJobDb = CreateCourierJobContext();

		await accountDb.Database.MigrateAsync();
		await courierJobDb.Database.MigrateAsync();

		(await accountDb.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
		(await courierJobDb.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
	}

	private AccountDbContext CreateAccountContext()
	{
		var options = new DbContextOptionsBuilder<AccountDbContext>()
			.UseSqlServer(fixture.BuildTemporaryConnectionString("TranXit_Account_Migration"))
			.Options;

		return new AccountDbContext(options);
	}

	private CourierJobDbContext CreateCourierJobContext()
	{
		var options = new DbContextOptionsBuilder<CourierJobDbContext>()
			.UseSqlServer(fixture.BuildTemporaryConnectionString("TranXit_CourierJob_Migration"))
			.Options;

		return new CourierJobDbContext(options);
	}
}
