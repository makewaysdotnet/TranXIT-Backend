extern alias AccountService;
extern alias CourierJobService;

using Microsoft.EntityFrameworkCore;

using AccountDbContext = AccountService::AccountService.Database.AccountDbContext;
using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;

namespace TranXit.IntegrationTests.Infrastructure;

internal static class TestSeed
{
	public static async Task SeedAccountAsync(string connectionString)
	{
		var passwordHash = BCrypt.Net.BCrypt.EnhancedHashPassword("Password1!");
		var options = new DbContextOptionsBuilder<AccountDbContext>()
			.UseSqlServer(connectionString)
			.Options;
		await using var db = new AccountDbContext(options);

		await db.Database.ExecuteSqlInterpolatedAsync($"""
			SET IDENTITY_INSERT [Users] ON;
			INSERT INTO [Users] ([Id], [Email], [NormalizedEmail], [PasswordHash], [Username], [RoleId], [IsEmailVerified], [Phone])
			VALUES
				(1, 'customer.seed@tranxit.test', 'customer.seed@tranxit.test', {passwordHash}, 'Seed Customer', 1, 1, '+920000000001'),
				(2, 'courier.seed@tranxit.test', 'courier.seed@tranxit.test', {passwordHash}, 'Seed Courier', 2, 1, '+920000000002'),
				(3, 'admin.seed@tranxit.test', 'admin.seed@tranxit.test', {passwordHash}, 'Seed Admin', 4, 1, '+920000000003');
			SET IDENTITY_INSERT [Users] OFF;
			""");
	}

	public static async Task SeedCourierJobAsync(string connectionString)
	{
		var options = new DbContextOptionsBuilder<CourierJobDbContext>()
			.UseSqlServer(connectionString)
			.Options;
		await using var db = new CourierJobDbContext(options);

		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Jobs] ON;
			INSERT INTO [Jobs]
				([Id], [UserId], [OriginCountryId], [OriginCityId], [OriginAddress],
				 [DestinationCountryId], [DestinationCityId], [DestinationAddress], [Comments],
				 [JobStatusId], [CreatedOnUtc], [PickupDateUtc], [CargoModeId], [CourierModeId],
				 [JobNumber], [RecipientName], [RecipientContact], [RecipientEmail], [ExpiryDateUtc],
				 [IsJobStatusFromBid])
			VALUES
				(1, 1, 1, 1, 'Warehouse 12, Port Qasim',
				 2, 3, 'Hamburg port terminal', 'Integration sample job',
				 5, SYSUTCDATETIME(), DATEADD(day, 3, SYSUTCDATETIME()), 1, 1,
				 'TX1001', 'M. Weber', '+49 40 000000', 'recipient@example.com',
				 DATEADD(day, 6, SYSUTCDATETIME()), 0);
			SET IDENTITY_INSERT [Jobs] OFF;

			SET IDENTITY_INSERT [JobItems] ON;
			INSERT INTO [JobItems]
				([Id], [Name], [ImageUrl], [Quantity], [Weight], [DeclaredValue], [Dimensions], [Description], [JobId], [ItemTypeId])
			VALUES
				(1, 'Textile cartons', NULL, 24, 1180, 1750000, '40ft container', 'Export cartons for retail shipment', 1, 1);
			SET IDENTITY_INSERT [JobItems] OFF;
			""");
	}
}
