using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CourierJobService.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [JobStatuses]
                    WHERE ([Id] = 1 AND [Status] <> 'Open')
                       OR ([Id] = 2 AND [Status] <> 'Closed')
                       OR ([Id] = 3 AND [Status] <> 'Won')
                       OR ([Id] = 4 AND [Status] <> 'Lost')
                       OR ([Id] = 5 AND [Status] <> 'Bidding')
                       OR ([Id] = 6 AND [Status] <> 'InTransit')
                       OR ([Id] = 7 AND [Status] <> 'Delivered')
                       OR ([Status] = 'Open' AND [Id] <> 1)
                       OR ([Status] = 'Closed' AND [Id] <> 2)
                       OR ([Status] = 'Won' AND [Id] <> 3)
                       OR ([Status] = 'Lost' AND [Id] <> 4)
                       OR ([Status] = 'Bidding' AND [Id] <> 5)
                       OR ([Status] = 'InTransit' AND [Id] <> 6)
                       OR ([Status] = 'Delivered' AND [Id] <> 7)
                )
                    THROW 51000, 'Canonical job-status IDs or names conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [CourierModes]
                    WHERE ([Id] = 1 AND [Name] <> 'Door to door')
                       OR ([Id] = 2 AND [Name] <> 'Port to port')
                       OR ([Id] = 3 AND [Name] <> 'Warehouse pickup')
                       OR ([Name] = 'Door to door' AND [Id] <> 1)
                       OR ([Name] = 'Port to port' AND [Id] <> 2)
                       OR ([Name] = 'Warehouse pickup' AND [Id] <> 3)
                )
                    THROW 51000, 'Canonical courier-mode IDs or names conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [CargoModes]
                    WHERE ([Id] = 1 AND [Name] <> 'Sea freight')
                       OR ([Id] = 2 AND [Name] <> 'Air freight')
                       OR ([Id] = 3 AND [Name] <> 'Road freight')
                       OR ([Name] = 'Sea freight' AND [Id] <> 1)
                       OR ([Name] = 'Air freight' AND [Id] <> 2)
                       OR ([Name] = 'Road freight' AND [Id] <> 3)
                )
                    THROW 51000, 'Canonical cargo-mode IDs or names conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [ItemTypes]
                    WHERE ([Id] = 1 AND [Name] <> 'Cartons')
                       OR ([Id] = 2 AND [Name] <> 'Pallets')
                       OR ([Id] = 3 AND [Name] <> 'Machinery')
                       OR ([Id] = 4 AND [Name] <> 'Documents')
                       OR ([Name] = 'Cartons' AND [Id] <> 1)
                       OR ([Name] = 'Pallets' AND [Id] <> 2)
                       OR ([Name] = 'Machinery' AND [Id] <> 3)
                       OR ([Name] = 'Documents' AND [Id] <> 4)
                )
                    THROW 51000, 'Canonical item-type IDs or names conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [DeliveryTypes]
                    WHERE ([Id] = 1 AND ([Name] <> 'Economy' OR [NoOfDays] <> 22))
                       OR ([Id] = 2 AND ([Name] <> 'Standard' OR [NoOfDays] <> 16))
                       OR ([Id] = 3 AND ([Name] <> 'Express' OR [NoOfDays] <> 7))
                       OR ([Name] = 'Economy' AND [Id] <> 1)
                       OR ([Name] = 'Standard' AND [Id] <> 2)
                       OR ([Name] = 'Express' AND [Id] <> 3)
                )
                    THROW 51000, 'Canonical delivery-type IDs or values conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [Countries]
                    WHERE ([Id] = 1 AND [CountryName] <> 'Pakistan')
                       OR ([Id] = 2 AND [CountryName] <> 'Germany')
                       OR ([Id] = 3 AND [CountryName] <> 'United Arab Emirates')
                       OR ([CountryName] = 'Pakistan' AND [Id] <> 1)
                       OR ([CountryName] = 'Germany' AND [Id] <> 2)
                       OR ([CountryName] = 'United Arab Emirates' AND [Id] <> 3)
                )
                    THROW 51000, 'Canonical country IDs or names conflict with existing data.', 1;

                IF EXISTS (
                    SELECT 1 FROM [Cities]
                    WHERE ([Id] = 1 AND ([CityName] <> 'Karachi' OR [CountryId] <> 1))
                       OR ([Id] = 2 AND ([CityName] <> 'Lahore' OR [CountryId] <> 1))
                       OR ([Id] = 3 AND ([CityName] <> 'Hamburg' OR [CountryId] <> 2))
                       OR ([Id] = 4 AND ([CityName] <> 'Berlin' OR [CountryId] <> 2))
                       OR ([Id] = 5 AND ([CityName] <> 'Dubai' OR [CountryId] <> 3))
                       OR ([CityName] = 'Karachi' AND [Id] <> 1)
                       OR ([CityName] = 'Lahore' AND [Id] <> 2)
                       OR ([CityName] = 'Hamburg' AND [Id] <> 3)
                       OR ([CityName] = 'Berlin' AND [Id] <> 4)
                       OR ([CityName] = 'Dubai' AND [Id] <> 5)
                )
                    THROW 51000, 'Canonical city IDs or values conflict with existing data.', 1;

                SET IDENTITY_INSERT [JobStatuses] ON;
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 1) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (1, 'Open');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 2) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (2, 'Closed');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 3) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (3, 'Won');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 4) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (4, 'Lost');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 5) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (5, 'Bidding');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 6) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (6, 'InTransit');
                IF NOT EXISTS (SELECT 1 FROM [JobStatuses] WHERE [Id] = 7) INSERT INTO [JobStatuses] ([Id], [Status]) VALUES (7, 'Delivered');
                SET IDENTITY_INSERT [JobStatuses] OFF;

                SET IDENTITY_INSERT [CourierModes] ON;
                IF NOT EXISTS (SELECT 1 FROM [CourierModes] WHERE [Id] = 1) INSERT INTO [CourierModes] ([Id], [Name]) VALUES (1, 'Door to door');
                IF NOT EXISTS (SELECT 1 FROM [CourierModes] WHERE [Id] = 2) INSERT INTO [CourierModes] ([Id], [Name]) VALUES (2, 'Port to port');
                IF NOT EXISTS (SELECT 1 FROM [CourierModes] WHERE [Id] = 3) INSERT INTO [CourierModes] ([Id], [Name]) VALUES (3, 'Warehouse pickup');
                SET IDENTITY_INSERT [CourierModes] OFF;

                SET IDENTITY_INSERT [CargoModes] ON;
                IF NOT EXISTS (SELECT 1 FROM [CargoModes] WHERE [Id] = 1) INSERT INTO [CargoModes] ([Id], [Name]) VALUES (1, 'Sea freight');
                IF NOT EXISTS (SELECT 1 FROM [CargoModes] WHERE [Id] = 2) INSERT INTO [CargoModes] ([Id], [Name]) VALUES (2, 'Air freight');
                IF NOT EXISTS (SELECT 1 FROM [CargoModes] WHERE [Id] = 3) INSERT INTO [CargoModes] ([Id], [Name]) VALUES (3, 'Road freight');
                SET IDENTITY_INSERT [CargoModes] OFF;

                SET IDENTITY_INSERT [ItemTypes] ON;
                IF NOT EXISTS (SELECT 1 FROM [ItemTypes] WHERE [Id] = 1) INSERT INTO [ItemTypes] ([Id], [Name]) VALUES (1, 'Cartons');
                IF NOT EXISTS (SELECT 1 FROM [ItemTypes] WHERE [Id] = 2) INSERT INTO [ItemTypes] ([Id], [Name]) VALUES (2, 'Pallets');
                IF NOT EXISTS (SELECT 1 FROM [ItemTypes] WHERE [Id] = 3) INSERT INTO [ItemTypes] ([Id], [Name]) VALUES (3, 'Machinery');
                IF NOT EXISTS (SELECT 1 FROM [ItemTypes] WHERE [Id] = 4) INSERT INTO [ItemTypes] ([Id], [Name]) VALUES (4, 'Documents');
                SET IDENTITY_INSERT [ItemTypes] OFF;

                SET IDENTITY_INSERT [DeliveryTypes] ON;
                IF NOT EXISTS (SELECT 1 FROM [DeliveryTypes] WHERE [Id] = 1) INSERT INTO [DeliveryTypes] ([Id], [Name], [NoOfDays]) VALUES (1, 'Economy', 22);
                IF NOT EXISTS (SELECT 1 FROM [DeliveryTypes] WHERE [Id] = 2) INSERT INTO [DeliveryTypes] ([Id], [Name], [NoOfDays]) VALUES (2, 'Standard', 16);
                IF NOT EXISTS (SELECT 1 FROM [DeliveryTypes] WHERE [Id] = 3) INSERT INTO [DeliveryTypes] ([Id], [Name], [NoOfDays]) VALUES (3, 'Express', 7);
                SET IDENTITY_INSERT [DeliveryTypes] OFF;

                SET IDENTITY_INSERT [Countries] ON;
                IF NOT EXISTS (SELECT 1 FROM [Countries] WHERE [Id] = 1) INSERT INTO [Countries] ([Id], [CountryName]) VALUES (1, 'Pakistan');
                IF NOT EXISTS (SELECT 1 FROM [Countries] WHERE [Id] = 2) INSERT INTO [Countries] ([Id], [CountryName]) VALUES (2, 'Germany');
                IF NOT EXISTS (SELECT 1 FROM [Countries] WHERE [Id] = 3) INSERT INTO [Countries] ([Id], [CountryName]) VALUES (3, 'United Arab Emirates');
                SET IDENTITY_INSERT [Countries] OFF;

                SET IDENTITY_INSERT [Cities] ON;
                IF NOT EXISTS (SELECT 1 FROM [Cities] WHERE [Id] = 1) INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES (1, 'Karachi', 1);
                IF NOT EXISTS (SELECT 1 FROM [Cities] WHERE [Id] = 2) INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES (2, 'Lahore', 1);
                IF NOT EXISTS (SELECT 1 FROM [Cities] WHERE [Id] = 3) INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES (3, 'Hamburg', 2);
                IF NOT EXISTS (SELECT 1 FROM [Cities] WHERE [Id] = 4) INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES (4, 'Berlin', 2);
                IF NOT EXISTS (SELECT 1 FROM [Cities] WHERE [Id] = 5) INSERT INTO [Cities] ([Id], [CityName], [CountryId]) VALUES (5, 'Dubai', 3);
                SET IDENTITY_INSERT [Cities] OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Canonical reference rows are intentionally retained during rollback.
        }
    }
}
