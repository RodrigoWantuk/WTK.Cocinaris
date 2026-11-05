using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaiseShoppingPlanBatchLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_sessions_batches",
                schema: "planning",
                table: "shopping_plan_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_sessions_batches",
                schema: "planning",
                table: "shopping_plan_sessions",
                sql: "\"BatchCount\" BETWEEN 0 AND 12");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_sessions_batches",
                schema: "planning",
                table: "shopping_plan_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_sessions_batches",
                schema: "planning",
                table: "shopping_plan_sessions",
                sql: "\"BatchCount\" BETWEEN 0 AND 5");
        }
    }
}
