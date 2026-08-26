using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class TemporaereAusnahmen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TemporaryAllows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Device = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Rule = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UntilUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemporaryAllows", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TemporaryAllows");
        }
    }
}
