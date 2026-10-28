using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Enforces unique (OwnerUserId, StableCode) for equipment. This migration does not delete or merge
    /// duplicate rows. Environments that already contain duplicates must resolve them before applying;
    /// PostgreSQL will reject unique index creation until the data is clean.
    /// </remarks>
    public partial class EnforceUniqueProfileEquipmentStableCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_equipment_entries_OwnerUserId_StableCode",
                schema: "profiles",
                table: "equipment_entries");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_entries_OwnerUserId_StableCode",
                schema: "profiles",
                table: "equipment_entries",
                columns: new[] { "OwnerUserId", "StableCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_equipment_entries_OwnerUserId_StableCode",
                schema: "profiles",
                table: "equipment_entries");

            migrationBuilder.CreateIndex(
                name: "IX_equipment_entries_OwnerUserId_StableCode",
                schema: "profiles",
                table: "equipment_entries",
                columns: new[] { "OwnerUserId", "StableCode" });
        }
    }
}
