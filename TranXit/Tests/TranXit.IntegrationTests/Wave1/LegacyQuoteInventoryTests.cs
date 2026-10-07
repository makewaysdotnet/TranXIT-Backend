using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TranXit.IntegrationTests.Infrastructure;

namespace TranXit.IntegrationTests.Wave1;

public sealed class LegacyQuoteInventoryTests(SqlContainerFixture fixture) : IntegrationTestBase(fixture)
{
	[Fact(DisplayName = "T-NFR-3.LegacyInventorySelectOnlyDoesNotReprice")]
	public async Task LegacyInventorySelectOnlyDoesNotReprice()
	{
		await SeedInventoryCasesAsync();
		var before = await ReadBusinessStateAsync();
		var rows = await ReadInventoryAsSelectOnlyUserAsync();

		rows.Should().Contain(row => row.JobId == 1 && row.Category == "unawarded_base_total_mismatch");
		rows.Should().Contain(row => row.JobId == 2 && row.Category == "unresolved_award_reference" && row.AcceptedEta == null);
		rows.Should().NotContain(row => row.JobId == 3,
			"a resolved legacy award keeps its stored price, even when it differs from the old proposal");
		rows.Should().NotContain(row => row.JobId == 4, "a consistent unawarded quote needs no review");
		(await ReadBusinessStateAsync()).Should().Be(before);
	}

	[Fact(DisplayName = "T-NFR-3.LegacyInventoryReportsMissingEtaAndComponents")]
	public async Task LegacyInventoryReportsMissingEtaAndComponents()
	{
		await SeedInventoryCasesAsync();
		await using (var db = Fixture.CreateCourierJobDbContext())
		{
			await db.Database.ExecuteSqlRawAsync("""
				UPDATE [BiddingProposals] SET [DeliveryDateUtc] = NULL, [Total] = 999 WHERE [Id] = 100;
				UPDATE [BiddingProposals] SET [DeliveryDateUtc] = NULL WHERE [Id] = 300;
				""");
		}
		var before = await ReadBusinessStateAsync();
		var rows = await ReadInventoryAsSelectOnlyUserAsync();
		rows.Should().Contain(row => row.JobId == 1 && row.Category == "unawarded_proposal_eta_missing");
		rows.Should().Contain(row => row.JobId == 1 && row.Category == "unawarded_components_mismatch");
		rows.Should().Contain(row => row.JobId == 3 && row.Category == "accepted_eta_missing" && row.AcceptedEta == null);
		(await ReadBusinessStateAsync()).Should().Be(before);
	}

	private async Task<List<InventoryRow>> ReadInventoryAsSelectOnlyUserAsync()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "legacy-quote-inventory.sql")))
		{
			directory = directory.Parent;
		}
		directory.Should().NotBeNull("the exact operator SQL script must be tested");
		var sql = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "scripts", "legacy-quote-inventory.sql"));
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			CREATE USER [LaunchInventoryReader] WITHOUT LOGIN;
			GRANT SELECT ON SCHEMA::dbo TO [LaunchInventoryReader];
			DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE ON SCHEMA::dbo TO [LaunchInventoryReader];
			""");
		try
		{
			await using var connection = new SqlConnection(Fixture.CourierJobConnectionString);
			await connection.OpenAsync();
			await using var command = connection.CreateCommand();
			command.CommandText = "EXECUTE AS USER = 'LaunchInventoryReader';\n" + sql + "\nREVERT;";
			await using var reader = await command.ExecuteReaderAsync();
			var rows = new List<InventoryRow>();
			while (await reader.ReadAsync())
			{
				var etaIndex = reader.GetOrdinal("ResolvedAcceptedEtaUtc");
				rows.Add(new InventoryRow(reader.GetInt32(reader.GetOrdinal("JobId")),
					reader.GetString(reader.GetOrdinal("Category")), reader.IsDBNull(etaIndex) ? null : reader.GetDateTime(etaIndex)));
			}
			return rows;
		}
		finally
		{
			await db.Database.ExecuteSqlRawAsync("DROP USER [LaunchInventoryReader];");
		}
	}

	private async Task<string> ReadBusinessStateAsync()
	{
		await using var connection = new SqlConnection(Fixture.CourierJobConnectionString);
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = """
			SELECT COALESCE((SELECT * FROM [Jobs] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') +
			    COALESCE((SELECT * FROM [JobItems] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') +
			    COALESCE((SELECT * FROM [Biddings] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') +
			    COALESCE((SELECT * FROM [BiddingProposals] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') +
			    COALESCE((SELECT * FROM [BiddingProposalItems] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]') +
			    COALESCE((SELECT * FROM [BiddingCharges] ORDER BY [Id] FOR JSON PATH, INCLUDE_NULL_VALUES), '[]');
			""";
		return (string)(await command.ExecuteScalarAsync())!;
	}

	private async Task SeedInventoryCasesAsync()
	{
		await using var db = Fixture.CreateCourierJobDbContext();
		await db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Jobs] ON;
			INSERT INTO [Jobs] ([Id], [UserId], [JobStatusId], [JobNumber], [ExpiryDateUtc], [IsJobStatusFromBid])
			VALUES (2, 1, NULL, 'INV0002', DATEADD(day, 6, SYSUTCDATETIME()), 1),
			       (3, 1, NULL, 'INV0003', DATEADD(day, 6, SYSUTCDATETIME()), 1),
			       (4, 1, 5, 'INV0004', DATEADD(day, 6, SYSUTCDATETIME()), 0);
			SET IDENTITY_INSERT [Jobs] OFF;
			SET IDENTITY_INSERT [Biddings] ON;
			INSERT INTO [Biddings] ([Id], [UserId], [JobId], [TotalAmount], [PickupCharges], [HandlingCharges], [CustomClearanceCharges], [JobStatusId])
			VALUES (10, 2, 1, 160, 100, 50, 25, NULL), (20, 2, 2, 175, 100, 50, 25, 3),
			       (30, 2, 3, 199.875, 100, 50, 25, 3), (40, 2, 4, 175, 100, 50, 25, NULL);
			SET IDENTITY_INSERT [Biddings] OFF;
			SET IDENTITY_INSERT [BiddingProposals] ON;
			INSERT INTO [BiddingProposals] ([Id], [BiddingId], [DeliveryTypeId], [IsBaseBid], [DeliveryDateUtc], [Total])
			VALUES (100, 10, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 175),
			       (200, 20, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 175),
			       (300, 30, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 200),
			       (400, 40, 1, 1, DATEADD(day, 5, SYSUTCDATETIME()), 175);
			SET IDENTITY_INSERT [BiddingProposals] OFF;
			UPDATE [Jobs] SET [AcceptedBidProposalId] = 300 WHERE [Id] = 3;
			""");
	}

	private sealed record InventoryRow(int JobId, string Category, DateTime? AcceptedEta);
}
