using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaiseShoppingPlanRepeatLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_cards_repeats",
                schema: "planning",
                table: "shopping_plan_cards");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_cards_repeats",
                schema: "planning",
                table: "shopping_plan_cards",
                sql: "\"Repeats\" BETWEEN 1 AND 14");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_cards_repeats",
                schema: "planning",
                table: "shopping_plan_cards");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_cards_repeats",
                schema: "planning",
                table: "shopping_plan_cards",
                sql: "\"Repeats\" BETWEEN 1 AND 4");
        }
    }
}
