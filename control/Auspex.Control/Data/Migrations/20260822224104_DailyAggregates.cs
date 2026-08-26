using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailyAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyClients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Client = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Total = table.Column<long>(type: "INTEGER", nullable: false),
                    Blocked = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyDomains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Total = table.Column<long>(type: "INTEGER", nullable: false),
                    Blocked = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyDomains", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyTotals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Day = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Total = table.Column<long>(type: "INTEGER", nullable: false),
                    Blocked = table.Column<long>(type: "INTEGER", nullable: false),
                    Validated = table.Column<long>(type: "INTEGER", nullable: false),
                    Upstream = table.Column<long>(type: "INTEGER", nullable: false),
                    Clients = table.Column<int>(type: "INTEGER", nullable: false),
                    Domains = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyTotals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyClients_Day_Client",
                table: "DailyClients",
                columns: new[] { "Day", "Client" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyDomains_Day_Domain",
                table: "DailyDomains",
                columns: new[] { "Day", "Domain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyTotals_Day",
                table: "DailyTotals",
                column: "Day",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyClients");

            migrationBuilder.DropTable(
                name: "DailyDomains");

            migrationBuilder.DropTable(
                name: "DailyTotals");
        }
    }
}
