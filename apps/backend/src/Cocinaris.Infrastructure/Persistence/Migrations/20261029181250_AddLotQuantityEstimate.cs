using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotQuantityEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "QuantityIsEstimate",
                schema: "inventory",
                table: "lots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_estimate_measured",
                schema: "inventory",
                table: "lots",
                sql: "NOT \"QuantityIsEstimate\" OR \"MeasuredValue\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_estimate_measured",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "QuantityIsEstimate",
                schema: "inventory",
                table: "lots");
        }
    }
}
