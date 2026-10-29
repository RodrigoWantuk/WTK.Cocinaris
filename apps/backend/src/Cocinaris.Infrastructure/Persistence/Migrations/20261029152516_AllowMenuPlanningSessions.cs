using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowMenuPlanningSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_generation_sessions_execution_mode",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_generation_sessions_execution_mode",
                schema: "recipes",
                table: "generation_sessions",
                sql: "\"ExecutionMode\" IN ('cook_now', 'menu_planning')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_generation_sessions_execution_mode",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_generation_sessions_execution_mode",
                schema: "recipes",
                table: "generation_sessions",
                sql: "\"ExecutionMode\" IN ('cook_now')");
        }
    }
}
