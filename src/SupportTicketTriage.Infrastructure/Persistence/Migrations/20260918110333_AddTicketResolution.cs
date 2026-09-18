using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportTicketTriage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "Tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Tickets");
        }
    }
}
