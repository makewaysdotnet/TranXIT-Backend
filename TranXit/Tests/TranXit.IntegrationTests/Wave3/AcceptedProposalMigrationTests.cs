extern alias CourierJobService;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TranXit.IntegrationTests.Infrastructure;

using CourierJobDbContext = CourierJobService::CourierJobService.Database.CourierJobDbContext;
using Job = CourierJobService::CourierJobService.Database.Job;

namespace TranXit.IntegrationTests.Wave3;

[Collection(IntegrationTestCollection.Name)]
public sealed class AcceptedProposalMigrationTests(SqlContainerFixture fixture)
{
	private const string PreviousMigration = "20260727130005_CanonicalReferenceData";

	[Theory(DisplayName = "T-NFR-4.AcceptedProposalBackfillsUnambiguousHistory")]
	[InlineData(3)]
	[InlineData(6)]
	[InlineData(7)]
	public async Task AcceptedProposalBackfillsUnambiguousHistory(int lifecycle)
	{
		// UC-CUST-5, UC-NFR-4
		await using var db = CreateContext();
		await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
		await SeedLegacyAwardAsync(db);
		await db.Biddings.Where(bid => bid.Id == 10)
			.ExecuteUpdateAsync(setters => setters.SetProperty(bid => bid.JobStatusId, lifecycle));
		var originalHistory = await ReadLegacyStateAsync(db);

		await db.Database.MigrateAsync();
		await db.Database.MigrateAsync();

		var job = await db.Jobs.AsNoTracking().SingleAsync();
		job.AcceptedBidProposalId.Should().Be(100);
		(await ReadLegacyStateAsync(db)).Should().Be(originalHistory);
		(await db.Biddings.FindAsync(10))!.TotalAmount.Should().Be(1175.125);
		(await db.BiddingProposals.CountAsync()).Should().Be(3, "deleted alternatives must not be reconstructed");
	}

	[Theory(DisplayName = "T-NFR-4.AcceptedProposalLeavesAmbiguousHistoryUnresolved")]
	[InlineData("multiple-proposals")]
	[InlineData("multiple-winners")]
	[InlineData("missing-proposal")]
	[InlineData("price-mismatch")]
	[InlineData("missing-award-flag")]
	[InlineData("conflicting-job-status")]
	[InlineData("null-proposal-total")]
	[InlineData("missing-winner")]
	public async Task AcceptedProposalLeavesAmbiguousHistoryUnresolved(string ambiguity)
	{
		// UC-CUST-5, UC-NFR-4
		await using var db = CreateContext();
		await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
		await SeedLegacyAwardAsync(db);
		var mutation = ambiguity switch
		{
			"multiple-proposals" => "INSERT INTO [BiddingProposals] ([BiddingId], [IsBaseBid], [Total]) VALUES (10, 0, 1175.125);",
			"multiple-winners" => "UPDATE [Biddings] SET [JobStatusId] = 6 WHERE [Id] = 11;",
			"missing-proposal" => "DELETE FROM [BiddingProposalItems] WHERE [BiddingProposalId] = 100; DELETE FROM [BiddingProposals] WHERE [Id] = 100;",
			"price-mismatch" => "UPDATE [BiddingProposals] SET [Total] = 1000 WHERE [Id] = 100;",
			"missing-award-flag" => "UPDATE [Jobs] SET [IsJobStatusFromBid] = 0;",
			"conflicting-job-status" => "UPDATE [Jobs] SET [JobStatusId] = 5;",
			"null-proposal-total" => "UPDATE [BiddingProposals] SET [Total] = NULL WHERE [Id] = 100;",
			"missing-winner" => "UPDATE [Biddings] SET [JobStatusId] = 4;",
			_ => throw new ArgumentOutOfRangeException(nameof(ambiguity))
		};
		await db.Database.ExecuteSqlRawAsync(mutation);
		var originalHistory = await ReadLegacyStateAsync(db);
		var messages = new List<string>();
		var connection = (SqlConnection)db.Database.GetDbConnection();
		connection.InfoMessage += (_, args) => messages.Add(args.Message);

		await db.Database.MigrateAsync();

		(await db.Jobs.AsNoTracking().SingleAsync()).AcceptedBidProposalId.Should().BeNull();
		(await ReadLegacyStateAsync(db)).Should().Be(originalHistory);
		messages.Should().Contain(message => message.Contains("Accepted proposal history unresolved for 1 job(s)", StringComparison.Ordinal));
		(await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
	}

	[Fact(DisplayName = "T-NFR-4.AcceptedProposalLeavesUnawardedJobsUnchanged")]
	public async Task AcceptedProposalLeavesUnawardedJobsUnchanged()
	{
		// UC-CUST-5, UC-NFR-4
		await using var db = CreateContext();
		await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
		await SeedLegacyAwardAsync(db);
		await db.Database.ExecuteSqlRawAsync("""
			UPDATE [Jobs] SET [IsJobStatusFromBid] = 0, [JobStatusId] = 5;
			UPDATE [Biddings] SET [JobStatusId] = NULL;
			""");
		var originalHistory = await ReadLegacyStateAsync(db);
		var messages = new List<string>();
		((SqlConnection)db.Database.GetDbConnection()).InfoMessage += (_, args) => messages.Add(args.Message);

		await db.Database.MigrateAsync();

		(await db.Jobs.AsNoTracking().SingleAsync()).AcceptedBidProposalId.Should().BeNull();
		(await ReadLegacyStateAsync(db)).Should().Be(originalHistory);
		messages.Should().NotContain(message => message.Contains("Accepted proposal history unresolved", StringComparison.Ordinal));
	}

	[Fact(DisplayName = "T-NFR-4.AcceptedProposalMigrationMatchesModel")]
	public async Task AcceptedProposalMigrationMatchesModel()
	{
		// UC-CUST-5, UC-NFR-4
		await using var db = CreateContext();

		await db.Database.MigrateAsync();

		db.Database.HasPendingModelChanges().Should().BeFalse();
		var acceptedProperty = db.Model.FindEntityType(typeof(Job))!.FindProperty(nameof(Job.AcceptedBidProposalId))!;
		acceptedProperty.IsNullable.Should().BeTrue();
		acceptedProperty.IsConcurrencyToken.Should().BeTrue();
		acceptedProperty.GetContainingForeignKeys().Should().ContainSingle()
			.Which.DeleteBehavior.Should().Be(DeleteBehavior.NoAction);
		(await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
	}

	[Fact(DisplayName = "T-NFR-4.AcceptedProposalReferenceRejectsDeletion")]
	public async Task AcceptedProposalReferenceRejectsDeletion()
	{
		// UC-CUST-5, UC-NFR-4
		await using var db = CreateContext();
		await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
		await SeedLegacyAwardAsync(db);
		await db.Database.MigrateAsync();
		await db.BiddingProposalItems.Where(item => item.BiddingProposalId == 100).ExecuteDeleteAsync();
		var beforeDeletion = await ReadLegacyStateAsync(db);

		Func<Task> delete = () => db.BiddingProposals.Where(proposal => proposal.Id == 100).ExecuteDeleteAsync();

		var failure = await delete.Should().ThrowAsync<SqlException>();
		failure.Which.Number.Should().Be(547);
		failure.Which.Message.Should().Contain("FK_Jobs_AcceptedBidProposal");
		(await db.Jobs.AsNoTracking().SingleAsync()).AcceptedBidProposalId.Should().Be(100);
		(await ReadLegacyStateAsync(db)).Should().Be(beforeDeletion);
	}

	private CourierJobDbContext CreateContext()
		=> new(new DbContextOptionsBuilder<CourierJobDbContext>()
			.UseSqlServer(fixture.BuildTemporaryConnectionString("TranXit_AcceptedProposal_Migration"))
			.Options);

	private static async Task<string> ReadLegacyStateAsync(CourierJobDbContext db)
		=> JsonSerializer.Serialize(new
		{
			Jobs = await db.Jobs.OrderBy(job => job.Id).Select(job => new
			{
				job.Id, job.UserId, job.JobStatusId, job.IsJobStatusFromBid, job.ExpiryDateUtc, job.JobNumber
			}).ToListAsync(),
			Bids = await db.Biddings.OrderBy(bid => bid.Id).Select(bid => new
			{
				bid.Id, bid.JobId, bid.UserId, bid.JobStatusId, bid.TotalAmount, bid.IsInsurancePolicy,
				bid.PickupCharges, bid.HandlingCharges, bid.CustomClearanceCharges
			}).ToListAsync(),
			Proposals = await db.BiddingProposals.OrderBy(proposal => proposal.Id).Select(proposal => new
			{
				proposal.Id, proposal.BiddingId, proposal.IsBaseBid, proposal.Total, proposal.DeliveryDateUtc, proposal.DeliveryTypeId
			}).ToListAsync(),
			Items = await db.BiddingProposalItems.OrderBy(item => item.Id).Select(item => new
			{
				item.Id, item.BiddingProposalId, item.JobItemId, item.UnitPrice, item.ItemTotal
			}).ToListAsync(),
			Charges = await db.BiddingCharges.OrderBy(charge => charge.Id).Select(charge => new
			{
				charge.Id, charge.BiddingId, charge.Name, charge.Description, charge.Amount
			}).ToListAsync()
		});

	private static Task SeedLegacyAwardAsync(CourierJobDbContext db)
		=> db.Database.ExecuteSqlRawAsync("""
			SET IDENTITY_INSERT [Jobs] ON;
			INSERT INTO [Jobs] ([Id], [UserId], [JobNumber], [JobStatusId], [IsJobStatusFromBid], [ExpiryDateUtc])
			VALUES (1, 1, 'TXLEGACY', NULL, 1, DATEADD(day, -1, SYSUTCDATETIME()));
			SET IDENTITY_INSERT [Jobs] OFF;
			SET IDENTITY_INSERT [JobItems] ON;
			INSERT INTO [JobItems] ([Id], [JobId], [Name], [ItemTypeId]) VALUES (1, 1, 'Legacy item', 1);
			SET IDENTITY_INSERT [JobItems] OFF;
			SET IDENTITY_INSERT [Biddings] ON;
			INSERT INTO [Biddings] ([Id], [UserId], [JobId], [TotalAmount], [JobStatusId], [PickupCharges], [HandlingCharges], [CustomClearanceCharges])
			VALUES (10, 2, 1, 1175.125, 3, 100, 50, 25), (11, 5, 1, 1375.25, 4, 150, 100, 25);
			SET IDENTITY_INSERT [Biddings] OFF;
			SET IDENTITY_INSERT [BiddingProposals] ON;
			INSERT INTO [BiddingProposals] ([Id], [BiddingId], [DeliveryTypeId], [IsBaseBid], [DeliveryDateUtc], [Total])
			VALUES (100, 10, 2, 0, DATEADD(day, 8, SYSUTCDATETIME()), 1175.125),
			       (101, 11, 1, 1, DATEADD(day, 3, SYSUTCDATETIME()), 1375.25),
			       (102, 11, 2, 0, DATEADD(day, 9, SYSUTCDATETIME()), 1475.25);
			SET IDENTITY_INSERT [BiddingProposals] OFF;
			INSERT INTO [BiddingProposalItems] ([BiddingProposalId], [JobItemId], [UnitPrice], [ItemTotal])
			VALUES (100, 1, 1175.125, 1175.125), (101, 1, 1375.25, 1375.25), (102, 1, 1475.25, 1475.25);
			INSERT INTO [BiddingCharges] ([BiddingId], [Name], [Amount]) VALUES (10, 'Legacy charge', 25), (11, 'Other charge', 30);
			""");
}
