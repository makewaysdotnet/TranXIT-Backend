extern alias AccountService;
extern alias CourierJobService;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TranXit.IntegrationTests.Infrastructure;

using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;
using AccountProductionAdminBootstrapper = AccountService::AccountService.Database.AccountProductionAdminBootstrapper;
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

		(await accountDb.Roles
			.OrderBy(role => role.Id)
			.Select(role => role.Name)
			.ToListAsync())
			.Should()
			.Equal("Customer", "Courier", "Agent", "Admin");
		(await courierJobDb.JobStatuses.CountAsync()).Should().Be(7);
		(await courierJobDb.CourierModes.CountAsync()).Should().Be(3);
		(await courierJobDb.CargoModes.CountAsync()).Should().Be(3);
		(await courierJobDb.ItemTypes.CountAsync()).Should().Be(4);
		(await courierJobDb.DeliveryTypes.CountAsync()).Should().Be(3);
		(await courierJobDb.Countries.CountAsync()).Should().Be(3);
		(await courierJobDb.Cities.CountAsync()).Should().Be(5);
		(await courierJobDb.Jobs.CountAsync()).Should().Be(0);
	}

	[Fact(DisplayName = "T-NFR-7.ProductionAdminBootstrap")]
	public async Task ProductionAdminBootstrap()
	{
		// UC-NFR-7
		var connectionString = fixture.BuildTemporaryConnectionString("TranXit_Account_AdminBootstrap");
		await using var services = new ServiceCollection()
			.AddDbContext<AccountDbContext>(options => options.UseSqlServer(connectionString))
			.BuildServiceProvider();
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["AdminBootstrap:Email"] = "release-admin@tranxit.test"
			})
			.Build();

		using (var migrationScope = services.CreateScope())
		{
			var db = migrationScope.ServiceProvider.GetRequiredService<AccountDbContext>();
			await db.Database.MigrateAsync();
		}

		await AccountProductionAdminBootstrapper.BootstrapAsync(services, configuration);
		await AccountProductionAdminBootstrapper.BootstrapAsync(services, configuration);

		using var verificationScope = services.CreateScope();
		var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AccountDbContext>();
		var admins = await verificationDb.Users
			.Include(user => user.Role)
			.Where(user => user.Role!.Name == "Admin")
			.ToListAsync();

		admins.Should().ContainSingle();
		admins[0].Email.Should().Be("release-admin@tranxit.test");
		admins[0].IsEmailVerified.Should().BeTrue();
		admins[0].PasswordHash.Should().NotBeNullOrWhiteSpace();
		BCrypt.Net.BCrypt.EnhancedVerify("Password1!", admins[0].PasswordHash)
			.Should()
			.BeFalse();
	}

	[Fact(DisplayName = "T-NFR-7.CanonicalMigrationReconcilesExistingData")]
	public async Task CanonicalMigrationReconcilesExistingData()
	{
		// UC-NFR-7
		await using var accountDb = CreateAccountContext();
		await using var courierJobDb = CreateCourierJobContext();

		await accountDb.Database
			.GetService<IMigrator>()
			.MigrateAsync("20260526135948_AuthTokenAndCodeHygiene");
		await courierJobDb.Database
			.GetService<IMigrator>()
			.MigrateAsync("20260526133343_InitialCreate");

		await accountDb.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Roles] ON;
			INSERT INTO [Roles] ([Id], [Name])
			VALUES (1, 'Customer'), (2, 'Courier'), (3, 'Agent'), (4, 'Admin');
			SET IDENTITY_INSERT [Roles] OFF;
			""");
		await courierJobDb.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [JobStatuses] ON;
			INSERT INTO [JobStatuses] ([Id], [Status])
			VALUES (1, 'Open'), (2, 'Closed'), (3, 'Won'), (4, 'Lost'),
			       (5, 'Bidding'), (6, 'InTransit'), (7, 'Delivered');
			SET IDENTITY_INSERT [JobStatuses] OFF;

			SET IDENTITY_INSERT [CourierModes] ON;
			INSERT INTO [CourierModes] ([Id], [Name])
			VALUES (1, 'Door to door'), (2, 'Port to port'), (3, 'Warehouse pickup');
			SET IDENTITY_INSERT [CourierModes] OFF;

			SET IDENTITY_INSERT [CargoModes] ON;
			INSERT INTO [CargoModes] ([Id], [Name])
			VALUES (1, 'Sea freight'), (2, 'Air freight'), (3, 'Road freight');
			SET IDENTITY_INSERT [CargoModes] OFF;

			SET IDENTITY_INSERT [ItemTypes] ON;
			INSERT INTO [ItemTypes] ([Id], [Name])
			VALUES (1, 'Cartons'), (2, 'Pallets'), (3, 'Machinery'), (4, 'Documents');
			SET IDENTITY_INSERT [ItemTypes] OFF;

			SET IDENTITY_INSERT [DeliveryTypes] ON;
			INSERT INTO [DeliveryTypes] ([Id], [Name], [NoOfDays])
			VALUES (1, 'Economy', 22), (2, 'Standard', 16), (3, 'Express', 7);
			SET IDENTITY_INSERT [DeliveryTypes] OFF;

			SET IDENTITY_INSERT [Countries] ON;
			INSERT INTO [Countries] ([Id], [CountryName])
			VALUES (1, 'Pakistan'), (2, 'Germany'), (3, 'United Arab Emirates');
			SET IDENTITY_INSERT [Countries] OFF;

			SET IDENTITY_INSERT [Cities] ON;
			INSERT INTO [Cities] ([Id], [CityName], [CountryId])
			VALUES (1, 'Karachi', 1), (2, 'Lahore', 1), (3, 'Hamburg', 2),
			       (4, 'Berlin', 2), (5, 'Dubai', 3);
			SET IDENTITY_INSERT [Cities] OFF;
			""");

		await accountDb.Database.MigrateAsync();
		await courierJobDb.Database.MigrateAsync();

		(await accountDb.Roles.CountAsync()).Should().Be(4);
		(await courierJobDb.JobStatuses.CountAsync()).Should().Be(7);
		(await courierJobDb.CourierModes.CountAsync()).Should().Be(3);
		(await courierJobDb.CargoModes.CountAsync()).Should().Be(3);
		(await courierJobDb.ItemTypes.CountAsync()).Should().Be(4);
		(await courierJobDb.DeliveryTypes.CountAsync()).Should().Be(3);
		(await courierJobDb.Countries.CountAsync()).Should().Be(3);
		(await courierJobDb.Cities.CountAsync()).Should().Be(5);
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
