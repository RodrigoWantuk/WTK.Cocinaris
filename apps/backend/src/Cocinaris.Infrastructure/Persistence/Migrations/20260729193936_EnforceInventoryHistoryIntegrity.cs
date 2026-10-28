using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceInventoryHistoryIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_previous_quantity",
                schema: "inventory",
                table: "transactions",
                sql: "(\"PreviousMeasuredValue\" IS NULL AND \"PreviousMeasuredUnit\" IS NULL AND \"PreviousAvailabilityState\" IS NULL) OR (\"PreviousMeasuredValue\" IS NOT NULL AND \"PreviousMeasuredValue\" >= 0 AND \"PreviousMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"PreviousAvailabilityState\" IS NULL) OR (\"PreviousMeasuredValue\" IS NULL AND \"PreviousMeasuredUnit\" IS NULL AND \"PreviousAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_reason_code",
                schema: "inventory",
                table: "transactions",
                sql: "\"ReasonCode\" IS NULL OR length(btrim(\"ReasonCode\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_resulting_quantity",
                schema: "inventory",
                table: "transactions",
                sql: "(\"ResultingMeasuredValue\" IS NOT NULL AND \"ResultingMeasuredValue\" >= 0 AND \"ResultingMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"ResultingAvailabilityState\" IS NULL) OR (\"ResultingMeasuredValue\" IS NULL AND \"ResultingMeasuredUnit\" IS NULL AND \"ResultingAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_display_name",
                schema: "inventory",
                table: "products",
                sql: "length(btrim(\"DisplayName\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_normalized_search_name",
                schema: "inventory",
                table: "products",
                sql: "length(btrim(\"NormalizedSearchName\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_idempotency_completion",
                schema: "platform",
                table: "idempotency_records",
                sql: "(\"CompletedAt\" IS NULL AND \"ResponseBody\" IS NULL AND \"ETag\" IS NULL) OR (\"CompletedAt\" IS NOT NULL AND \"ResponseBody\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_idempotency_status_code",
                schema: "platform",
                table: "idempotency_records",
                sql: "\"StatusCode\" BETWEEN 200 AND 299");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_previous_quantity",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_reason_code",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_resulting_quantity",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_display_name",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_normalized_search_name",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_idempotency_completion",
                schema: "platform",
                table: "idempotency_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_idempotency_status_code",
                schema: "platform",
                table: "idempotency_records");
        }
    }
}
