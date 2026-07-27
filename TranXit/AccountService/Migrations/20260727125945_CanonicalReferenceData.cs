using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AccountService.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_RoleId",
                table: "Users");

            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM [Roles]
                    WHERE ([Id] = 1 AND [Name] <> 'Customer')
                       OR ([Id] = 2 AND [Name] <> 'Courier')
                       OR ([Id] = 3 AND [Name] <> 'Agent')
                       OR ([Id] = 4 AND [Name] <> 'Admin')
                       OR ([Name] = 'Customer' AND [Id] <> 1)
                       OR ([Name] = 'Courier' AND [Id] <> 2)
                       OR ([Name] = 'Agent' AND [Id] <> 3)
                       OR ([Name] = 'Admin' AND [Id] <> 4)
                )
                    THROW 51000, 'Canonical role IDs or names conflict with existing data.', 1;

                IF (SELECT COUNT(*) FROM [Users] WHERE [RoleId] = 4) > 1
                    THROW 51000, 'More than one Admin exists; canonical migration refused.', 1;

                SET IDENTITY_INSERT [Roles] ON;
                IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Id] = 1)
                    INSERT INTO [Roles] ([Id], [Name]) VALUES (1, 'Customer');
                IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Id] = 2)
                    INSERT INTO [Roles] ([Id], [Name]) VALUES (2, 'Courier');
                IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Id] = 3)
                    INSERT INTO [Roles] ([Id], [Name]) VALUES (3, 'Agent');
                IF NOT EXISTS (SELECT 1 FROM [Roles] WHERE [Id] = 4)
                    INSERT INTO [Roles] ([Id], [Name]) VALUES (4, 'Admin');
                SET IDENTITY_INSERT [Roles] OFF;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_Users_SingleAdmin",
                table: "Users",
                column: "RoleId",
                unique: true,
                filter: "[RoleId] = 4");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Users_SingleAdmin",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "UX_Roles_Name",
                table: "Roles");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");
        }
    }
}
