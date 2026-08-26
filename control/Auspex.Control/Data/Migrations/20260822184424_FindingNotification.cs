using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class FindingNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NotifiedUtc",
                table: "Findings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Findings_NotifiedUtc_DetectedUtc",
                table: "Findings",
                columns: new[] { "NotifiedUtc", "DetectedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Findings_NotifiedUtc_DetectedUtc",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "NotifiedUtc",
                table: "Findings");
        }
    }
}
