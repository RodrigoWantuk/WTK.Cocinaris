using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceInventoryReferentialIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_products_Id_OwnerUserId",
                schema: "inventory",
                table: "products",
                columns: new[] { "Id", "OwnerUserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_lots_Id_OwnerUserId",
                schema: "inventory",
                table: "lots",
                columns: new[] { "Id", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_LotId_OwnerUserId",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "LotId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_lots_ProductId_OwnerUserId",
                schema: "inventory",
                table: "lots",
                columns: new[] { "ProductId", "OwnerUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_availability_state",
                schema: "inventory",
                table: "lots",
                sql: "\"AvailabilityState\" IS NULL OR \"AvailabilityState\" IN ('Available', 'Low', 'Unavailable')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots",
                sql: "(\"PrintedExpirationDate\" IS NULL AND \"ExpirationProvenance\" IS NULL) OR (\"PrintedExpirationDate\" IS NOT NULL AND \"ExpirationProvenance\" = 'UserEntered')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_measured_unit",
                schema: "inventory",
                table: "lots",
                sql: "\"MeasuredUnit\" IS NULL OR \"MeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_measured_value",
                schema: "inventory",
                table: "lots",
                sql: "\"MeasuredValue\" IS NULL OR \"MeasuredValue\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_package_state",
                schema: "inventory",
                table: "lots",
                sql: "\"PackageState\" IS NULL OR \"PackageState\" IN ('Sealed', 'Opened', 'Unknown')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_storage",
                schema: "inventory",
                table: "lots",
                sql: "(\"StorageLocation\" IN ('Pantry', 'Refrigerator', 'Freezer') AND \"CustomLocation\" IS NULL) OR (\"StorageLocation\" = 'Other' AND \"CustomLocation\" IS NOT NULL AND length(btrim(\"CustomLocation\")) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ActorUserId",
                schema: "platform",
                table: "audit_events",
                column: "ActorUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_events_users_ActorUserId",
                schema: "platform",
                table: "audit_events",
                column: "ActorUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_idempotency_records_users_OwnerUserId",
                schema: "platform",
                table: "idempotency_records",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lots_products_ProductId_OwnerUserId",
                schema: "inventory",
                table: "lots",
                columns: new[] { "ProductId", "OwnerUserId" },
                principalSchema: "inventory",
                principalTable: "products",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_lots_users_OwnerUserId",
                schema: "inventory",
                table: "lots",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_products_users_OwnerUserId",
                schema: "inventory",
                table: "products",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_lots_LotId_OwnerUserId",
                schema: "inventory",
                table: "transactions",
                columns: new[] { "LotId", "OwnerUserId" },
                principalSchema: "inventory",
                principalTable: "lots",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_users_OwnerUserId",
                schema: "inventory",
                table: "transactions",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_events_users_ActorUserId",
                schema: "platform",
                table: "audit_events");

            migrationBuilder.DropForeignKey(
                name: "FK_idempotency_records_users_OwnerUserId",
                schema: "platform",
                table: "idempotency_records");

            migrationBuilder.DropForeignKey(
                name: "FK_lots_products_ProductId_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropForeignKey(
                name: "FK_lots_users_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropForeignKey(
                name: "FK_products_users_OwnerUserId",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_lots_LotId_OwnerUserId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_users_OwnerUserId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_LotId_OwnerUserId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_products_Id_OwnerUserId",
                schema: "inventory",
                table: "products");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_lots_Id_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropIndex(
                name: "IX_lots_ProductId_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_availability_state",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_expiration_provenance",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_measured_unit",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_measured_value",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_package_state",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_storage",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_ActorUserId",
                schema: "platform",
                table: "audit_events");
        }
    }
}
