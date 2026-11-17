using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportTicketTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowManualResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TicketDraftId",
                table: "TicketReviews",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TicketReviews_DraftRequiredUnlessResolvedManually",
                table: "TicketReviews",
                sql: "\"Decision\" = 'ResolvedManually' OR \"TicketDraftId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TicketReviews_DraftRequiredUnlessResolvedManually",
                table: "TicketReviews");

            // No default value, unlike what EF generated: the original column
            // had none. Rolling back fails while any manual resolution without
            // a draft exists, because the old schema cannot represent one.
            // That is deliberate; the alternatives are deleting human
            // decisions or pointing them at a draft that does not exist.
            migrationBuilder.AlterColumn<Guid>(
                name: "TicketDraftId",
                table: "TicketReviews",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
