using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourierJobService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CargoModes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoModes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryName = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourierModes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierModes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    NoOfDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    CityName = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_Countries",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    OriginCountryId = table.Column<int>(type: "int", nullable: true),
                    OriginCityId = table.Column<int>(type: "int", nullable: true),
                    OriginAddress = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    DestinationCountryId = table.Column<int>(type: "int", nullable: true),
                    DestinationCityId = table.Column<int>(type: "int", nullable: true),
                    DestinationAddress = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Comments = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    JobStatusId = table.Column<int>(type: "int", nullable: true),
                    CreatedOnUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    PickupDateUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    CargoModeId = table.Column<int>(type: "int", nullable: true),
                    CourierModeId = table.Column<int>(type: "int", nullable: true),
                    JobNumber = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    RecipientName = table.Column<string>(type: "varchar(250)", unicode: false, maxLength: 250, nullable: true),
                    RecipientContact = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    RecipientEmail = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ExpiryDateUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    IsJobStatusFromBid = table.Column<bool>(type: "bit", nullable: true, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobs_CargoModes",
                        column: x => x.CargoModeId,
                        principalTable: "CargoModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_Cities",
                        column: x => x.OriginCityId,
                        principalTable: "Cities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_Cities1",
                        column: x => x.DestinationCityId,
                        principalTable: "Cities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_Countries",
                        column: x => x.OriginCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_Countries1",
                        column: x => x.DestinationCountryId,
                        principalTable: "Countries",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_CourierModes",
                        column: x => x.CourierModeId,
                        principalTable: "CourierModes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_JobStatuses",
                        column: x => x.JobStatusId,
                        principalTable: "JobStatuses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Biddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JobId = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<double>(type: "float", nullable: false),
                    IsInsurancePolicy = table.Column<bool>(type: "bit", nullable: true),
                    PickupCharges = table.Column<double>(type: "float", nullable: true),
                    HandlingCharges = table.Column<double>(type: "float", nullable: true),
                    CustomClearanceCharges = table.Column<double>(type: "float", nullable: true),
                    JobStatusId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Biddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Biddings_JobStatuses",
                        column: x => x.JobStatusId,
                        principalTable: "JobStatuses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Biddings_Jobs",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JobItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    ImageUrl = table.Column<string>(type: "varchar(250)", unicode: false, maxLength: 250, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    Weight = table.Column<double>(type: "float", nullable: true),
                    DeclaredValue = table.Column<double>(type: "float", nullable: true),
                    Dimensions = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    JobId = table.Column<int>(type: "int", nullable: true),
                    ItemTypeId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobItems_ItemTypes",
                        column: x => x.ItemTypeId,
                        principalTable: "ItemTypes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JobItems_Jobs",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BiddingCharges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BiddingId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiddingCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BiddingCharges_Biddings",
                        column: x => x.BiddingId,
                        principalTable: "Biddings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BiddingProposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BiddingId = table.Column<int>(type: "int", nullable: true),
                    DeliveryTypeId = table.Column<int>(type: "int", nullable: true),
                    IsBaseBid = table.Column<bool>(type: "bit", nullable: true),
                    DeliveryDateUtc = table.Column<DateTime>(type: "datetime", nullable: true),
                    Total = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiddingProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BiddingProposals_Biddings",
                        column: x => x.BiddingId,
                        principalTable: "Biddings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BiddingProposals_DeliveryTypes",
                        column: x => x.DeliveryTypeId,
                        principalTable: "DeliveryTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "JobItemImages",
                columns: table => new
                {
                    JobItemId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__JobItemI__A65D44637E33E7C7", x => x.JobItemId);
                    table.ForeignKey(
                        name: "FK_JobItemImage_JobItem",
                        column: x => x.JobItemId,
                        principalTable: "JobItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BiddingProposalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BiddingProposalId = table.Column<int>(type: "int", nullable: true),
                    JobItemId = table.Column<int>(type: "int", nullable: true),
                    UnitPrice = table.Column<double>(type: "float", nullable: true),
                    ItemTotal = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BiddingProposalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BiddingProposalItems_BiddingProposals",
                        column: x => x.BiddingProposalId,
                        principalTable: "BiddingProposals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BiddingProposalItems_JobItems",
                        column: x => x.JobItemId,
                        principalTable: "JobItems",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BiddingCharges_BiddingId",
                table: "BiddingCharges",
                column: "BiddingId");

            migrationBuilder.CreateIndex(
                name: "IX_BiddingProposalItems_BiddingProposalId",
                table: "BiddingProposalItems",
                column: "BiddingProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_BiddingProposalItems_JobItemId",
                table: "BiddingProposalItems",
                column: "JobItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BiddingProposals_BiddingId",
                table: "BiddingProposals",
                column: "BiddingId");

            migrationBuilder.CreateIndex(
                name: "IX_BiddingProposals_DeliveryTypeId",
                table: "BiddingProposals",
                column: "DeliveryTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Biddings_JobId",
                table: "Biddings",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Biddings_JobStatusId",
                table: "Biddings",
                column: "JobStatusId");

            migrationBuilder.CreateIndex(
                name: "UK_Biddings",
                table: "Biddings",
                columns: new[] { "UserId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_CountryId",
                table: "Cities",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_JobItems_ItemTypeId",
                table: "JobItems",
                column: "ItemTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_JobItems_JobId",
                table: "JobItems",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs",
                table: "Jobs",
                column: "JobNumber",
                unique: true,
                filter: "[JobNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CargoModeId",
                table: "Jobs",
                column: "CargoModeId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_CourierModeId",
                table: "Jobs",
                column: "CourierModeId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DestinationCityId",
                table: "Jobs",
                column: "DestinationCityId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_DestinationCountryId",
                table: "Jobs",
                column: "DestinationCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_JobStatusId",
                table: "Jobs",
                column: "JobStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_OriginCityId",
                table: "Jobs",
                column: "OriginCityId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_OriginCountryId",
                table: "Jobs",
                column: "OriginCountryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BiddingCharges");

            migrationBuilder.DropTable(
                name: "BiddingProposalItems");

            migrationBuilder.DropTable(
                name: "JobItemImages");

            migrationBuilder.DropTable(
                name: "BiddingProposals");

            migrationBuilder.DropTable(
                name: "JobItems");

            migrationBuilder.DropTable(
                name: "Biddings");

            migrationBuilder.DropTable(
                name: "DeliveryTypes");

            migrationBuilder.DropTable(
                name: "ItemTypes");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "CargoModes");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "CourierModes");

            migrationBuilder.DropTable(
                name: "JobStatuses");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
