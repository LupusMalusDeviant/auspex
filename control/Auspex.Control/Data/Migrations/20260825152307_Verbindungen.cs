using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class Verbindungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Verbindungen",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Client = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Geraet = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Prozess = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Ziel = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Protokoll = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    ErstUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ZuletztUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Anzahl = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesRaus = table.Column<long>(type: "INTEGER", nullable: true),
                    BytesRein = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verbindungen", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Verbindungen_Client_Prozess_Ziel_Port_Protokoll",
                table: "Verbindungen",
                columns: new[] { "Client", "Prozess", "Ziel", "Port", "Protokoll" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verbindungen_Geraet_ZuletztUtc",
                table: "Verbindungen",
                columns: new[] { "Geraet", "ZuletztUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Verbindungen_Ziel",
                table: "Verbindungen",
                column: "Ziel");

            migrationBuilder.CreateIndex(
                name: "IX_Verbindungen_ZuletztUtc",
                table: "Verbindungen",
                column: "ZuletztUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Verbindungen");
        }
    }
}
