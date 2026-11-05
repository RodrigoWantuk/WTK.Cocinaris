using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuEntryComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "planning",
                table: "menu_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "meal");

            migrationBuilder.AddColumn<string>(
                name: "ProducesName",
                schema: "planning",
                table: "menu_entries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProducesQuantity",
                schema: "planning",
                table: "menu_entries",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProducesUnit",
                schema: "planning",
                table: "menu_entries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_menu_entries_kind",
                schema: "planning",
                table: "menu_entries",
                sql: "(\"Kind\" = 'meal' AND \"ProducesName\" IS NULL AND \"ProducesQuantity\" IS NULL AND \"ProducesUnit\" IS NULL) OR (\"Kind\" = 'component' AND \"ProducesName\" IS NOT NULL AND \"ProducesQuantity\" > 0 AND \"ProducesUnit\" IN ('Gram', 'Milliliter', 'Unit'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_menu_entries_kind",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "ProducesName",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "ProducesQuantity",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "ProducesUnit",
                schema: "planning",
                table: "menu_entries");
        }
    }
}
