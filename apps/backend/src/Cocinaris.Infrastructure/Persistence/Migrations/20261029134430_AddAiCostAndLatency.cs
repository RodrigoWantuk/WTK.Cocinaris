using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCostAndLatency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                schema: "ai",
                table: "usage_ledger",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CostMicroUsd",
                schema: "ai",
                table: "usage_ledger",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EstimatedCostMicroUsd",
                schema: "ai",
                table: "usage_ledger",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                schema: "ai",
                table: "usage_ledger",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityFlags",
                schema: "ai",
                table: "usage_ledger",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                schema: "ai",
                table: "usage_ledger");

            migrationBuilder.DropColumn(
                name: "CostMicroUsd",
                schema: "ai",
                table: "usage_ledger");

            migrationBuilder.DropColumn(
                name: "EstimatedCostMicroUsd",
                schema: "ai",
                table: "usage_ledger");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                schema: "ai",
                table: "usage_ledger");

            migrationBuilder.DropColumn(
                name: "QualityFlags",
                schema: "ai",
                table: "usage_ledger");
        }
    }
}
