using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourierJobService.Migrations
{
    public partial class StableAcceptedProposal : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AcceptedBidProposalId",
                table: "Jobs",
                type: "int",
                nullable: true);

            // The legacy accept path kept exactly one proposal and copied its total to the bid.
            // Any conflicting lifecycle, multiple winners/proposals, or price mismatch stays unresolved.
            migrationBuilder.Sql("""
                UPDATE job
                SET [AcceptedBidProposalId] = proposal.[Id]
                FROM [Jobs] job
                INNER JOIN [Biddings] bid ON bid.[JobId] = job.[Id]
                INNER JOIN [BiddingProposals] proposal ON proposal.[BiddingId] = bid.[Id]
                WHERE job.[IsJobStatusFromBid] = 1
                  AND job.[JobStatusId] IS NULL
                  AND bid.[JobStatusId] IN (3, 6, 7)
                  AND proposal.[Total] = bid.[TotalAmount]
                  AND NOT EXISTS (
                      SELECT 1 FROM [Biddings] other
                      WHERE other.[JobId] = job.[Id] AND other.[Id] <> bid.[Id]
                        AND other.[JobStatusId] IN (3, 6, 7))
                  AND NOT EXISTS (
                      SELECT 1 FROM [BiddingProposals] other
                      WHERE other.[BiddingId] = bid.[Id] AND other.[Id] <> proposal.[Id]);

                DECLARE @unresolved int = (
                    SELECT COUNT(*) FROM [Jobs] job
                    WHERE job.[AcceptedBidProposalId] IS NULL
                      AND (job.[IsJobStatusFromBid] = 1 OR job.[JobStatusId] IN (3, 6, 7)
                           OR EXISTS (SELECT 1 FROM [Biddings] bid
                                      WHERE bid.[JobId] = job.[Id] AND bid.[JobStatusId] IN (3, 6, 7))));
                IF @unresolved > 0
                    RAISERROR ('Accepted proposal history unresolved for %d job(s). References remain NULL; review legacy awarded jobs. No prices or proposal rows were changed.', 10, 1, @unresolved) WITH NOWAIT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_AcceptedBidProposalId",
                table: "Jobs",
                column: "AcceptedBidProposalId",
                unique: true,
                filter: "[AcceptedBidProposalId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_AcceptedBidProposal",
                table: "Jobs",
                column: "AcceptedBidProposalId",
                principalTable: "BiddingProposals",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_AcceptedBidProposal",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_AcceptedBidProposalId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AcceptedBidProposalId",
                table: "Jobs");
        }
    }
}
