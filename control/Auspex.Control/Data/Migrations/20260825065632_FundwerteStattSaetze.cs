using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class FundwerteStattSaetze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Werte",
                table: "Findings",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Werte",
                table: "Findings");
        }
    }
}
