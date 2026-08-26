using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class ZieleUndAufloesungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Aufloesungen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    ErstUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ZuletztUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Anzahl = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aufloesungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ziele",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Privat = table.Column<bool>(type: "INTEGER", nullable: false),
                    GeprueftUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Land = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    Stadt = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Asn = table.Column<int>(type: "INTEGER", nullable: true),
                    Betreiber = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    StadtUnsicher = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErstUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ZuletztUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ziele", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aufloesungen_Domain_ZuletztUtc",
                table: "Aufloesungen",
                columns: new[] { "Domain", "ZuletztUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Aufloesungen_Ip",
                table: "Aufloesungen",
                column: "Ip");

            migrationBuilder.CreateIndex(
                name: "IX_Aufloesungen_Name_Ip",
                table: "Aufloesungen",
                columns: new[] { "Name", "Ip" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Aufloesungen_ZuletztUtc",
                table: "Aufloesungen",
                column: "ZuletztUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Ziele_Asn",
                table: "Ziele",
                column: "Asn");

            migrationBuilder.CreateIndex(
                name: "IX_Ziele_Ip",
                table: "Ziele",
                column: "Ip",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ziele_Privat_GeprueftUtc",
                table: "Ziele",
                columns: new[] { "Privat", "GeprueftUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Aufloesungen");

            migrationBuilder.DropTable(
                name: "Ziele");
        }
    }
}
