using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auspex.Control.Data.Migrations
{
    /// <inheritdoc />
    public partial class Dnssec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Validated",
                table: "Queries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Validated",
                table: "Queries");
        }
    }
}
