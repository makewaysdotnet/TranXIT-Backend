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
		var options = new DbContextOptionsBuilder<AccountDbContext>()
			.UseSqlServer(connectionString)
			.Options;
		await using var db = new AccountDbContext(options);

		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Roles] ON;
			INSERT INTO [Roles] ([Id], [Name]) VALUES
				(1, 'Customer'),
				(2, 'Courier'),
				(3, 'Agent'),
				(4, 'Admin');
			SET IDENTITY_INSERT [Roles] OFF;

			SET IDENTITY_INSERT [Users] ON;
			INSERT INTO [Users] ([Id], [Email], [PasswordHash], [Username], [RoleId], [IsEmailVerified], [Phone])
			VALUES
				(1, 'customer.seed@tranxit.test', 'integration-test-password-hash', 'Seed Customer', 1, 1, '+920000000001'),
				(2, 'courier.seed@tranxit.test', 'integration-test-password-hash', 'Seed Courier', 2, 1, '+920000000002'),
				(3, 'admin.seed@tranxit.test', 'integration-test-password-hash', 'Seed Admin', 4, 1, '+920000000003');
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
			SET IDENTITY_INSERT [JobStatuses] ON;
			INSERT INTO [JobStatuses] ([Id], [Status]) VALUES
				(1, 'Open'),
				(2, 'Closed'),
				(3, 'Won'),
				(4, 'Lost'),
				(5, 'Bidding'),
				(6, 'InTransit'),
				(7, 'Delivered');
			SET IDENTITY_INSERT [JobStatuses] OFF;

			SET IDENTITY_INSERT [CourierModes] ON;
			INSERT INTO [CourierModes] ([Id], [Name]) VALUES
				(1, 'Door to door'),
				(2, 'Port to port');
			SET IDENTITY_INSERT [CourierModes] OFF;

			SET IDENTITY_INSERT [CargoModes] ON;
			INSERT INTO [CargoModes] ([Id], [Name]) VALUES
				(1, 'Sea freight'),
				(2, 'Air freight');
			SET IDENTITY_INSERT [CargoModes] OFF;

			SET IDENTITY_INSERT [ItemTypes] ON;
			INSERT INTO [ItemTypes] ([Id], [Name]) VALUES
				(1, 'Cartons'),
				(2, 'Pallets');
			SET IDENTITY_INSERT [ItemTypes] OFF;

			SET IDENTITY_INSERT [DeliveryTypes] ON;
			INSERT INTO [DeliveryTypes] ([Id], [Name], [NoOfDays]) VALUES
				(1, 'Economy', 22),
				(2, 'Express', 7);
			SET IDENTITY_INSERT [DeliveryTypes] OFF;

			SET IDENTITY_INSERT [Countries] ON;
			INSERT INTO [Countries] ([Id], [CountryName]) VALUES
				(1, 'Pakistan'),
				(2, 'Germany');
			SET IDENTITY_INSERT [Countries] OFF;

			SET IDENTITY_INSERT [Cities] ON;
			INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES
				(1, 'Karachi', 1),
				(2, 'Lahore', 1),
				(3, 'Hamburg', 2);
			SET IDENTITY_INSERT [Cities] OFF;

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
