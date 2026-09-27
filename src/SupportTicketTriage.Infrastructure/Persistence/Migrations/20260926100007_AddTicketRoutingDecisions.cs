using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportTicketTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketRoutingDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketRoutingDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDraftEligible = table.Column<bool>(type: "boolean", nullable: false),
                    FailedGates = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClassificationScore = table.Column<double>(type: "double precision", nullable: true),
                    TopSimilarity = table.Column<double>(type: "double precision", nullable: true),
                    MinimumClassificationScore = table.Column<double>(type: "double precision", nullable: false),
                    MinimumSimilarity = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketRoutingDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketRoutingDecisions_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketRoutingDecisions_TicketId_CreatedAt",
                table: "TicketRoutingDecisions",
                columns: new[] { "TicketId", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketRoutingDecisions");
        }
    }
}
