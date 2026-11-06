using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCookOnDateAndAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_sessions_status",
                schema: "planning",
                table: "shopping_plan_sessions");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CookOnDate",
                schema: "planning",
                table: "menu_entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_sessions_status",
                schema: "planning",
                table: "shopping_plan_sessions",
                sql: "\"Status\" IN ('Building', 'Reviewing', 'Finished', 'Amending', 'Abandoned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_shopping_plan_sessions_status",
                schema: "planning",
                table: "shopping_plan_sessions");

            migrationBuilder.DropColumn(
                name: "CookOnDate",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shopping_plan_sessions_status",
                schema: "planning",
                table: "shopping_plan_sessions",
                sql: "\"Status\" IN ('Building', 'Reviewing', 'Finished', 'Abandoned')");
        }
    }
}
