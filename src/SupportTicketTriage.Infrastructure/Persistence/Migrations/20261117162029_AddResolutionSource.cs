using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportTicketTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResolutionSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionSource",
                table: "Tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Before this column existed, two code paths could resolve a
            // ticket: an approved review (TicketReviewService) and a direct
            // corpus import (EvalRunner). An approved review is written in the
            // same SaveChanges as the resolution, and Ticket.Resolve refuses an
            // already-resolved ticket, so an imported ticket can never also
            // carry an approved review. That makes the approved review alone
            // enough to tell the two apart. Unresolved tickets stay null.
            migrationBuilder.Sql("""
                UPDATE "Tickets" AS t
                SET "ResolutionSource" = CASE
                    WHEN EXISTS (
                        SELECT 1 FROM "TicketReviews" AS r
                        WHERE r."TicketId" = t."Id" AND r."Decision" = 'Approved')
                    THEN 'ApprovedDraft'
                    ELSE 'Imported'
                END
                WHERE t."Resolution" IS NOT NULL;
                """);

            // Added after the backfill, so a row the UPDATE missed fails the
            // migration rather than persisting a resolution with no source.
            migrationBuilder.AddCheckConstraint(
                name: "CK_Tickets_ResolutionSource_SetWithResolution",
                table: "Tickets",
                sql: "(\"Resolution\" IS NULL) = (\"ResolutionSource\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Tickets_ResolutionSource_SetWithResolution",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ResolutionSource",
                table: "Tickets");
        }
    }
}
