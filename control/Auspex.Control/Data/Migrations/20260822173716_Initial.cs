using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DetectedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Detector = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Client = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    Dismissed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Boot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRunUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LostTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    Ingested = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Queries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Seq = table.Column<long>(type: "INTEGER", nullable: false),
                    Boot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Client = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Profile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Rule = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    List = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Schedule = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Upstream = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Rcode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Millis = table.Column<double>(type: "REAL", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LongestLabel = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Queries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_Dismissed_DetectedUtc",
                table: "Findings",
                columns: new[] { "Dismissed", "DetectedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_Fingerprint",
                table: "Findings",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Queries_Action_TimeUtc",
                table: "Queries",
                columns: new[] { "Action", "TimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Queries_Boot_Seq",
                table: "Queries",
                columns: new[] { "Boot", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Queries_Client_TimeUtc",
                table: "Queries",
                columns: new[] { "Client", "TimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Queries_Domain_TimeUtc",
                table: "Queries",
                columns: new[] { "Domain", "TimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Queries_TimeUtc",
                table: "Queries",
                column: "TimeUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "IngestStates");

            migrationBuilder.DropTable(
                name: "Queries");
        }
    }
}
