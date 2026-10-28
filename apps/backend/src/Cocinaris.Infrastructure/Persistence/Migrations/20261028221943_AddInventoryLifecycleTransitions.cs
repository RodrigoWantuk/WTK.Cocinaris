using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLifecycleTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.AddColumn<string>(
                name: "PreviousLifecycleState",
                schema: "inventory",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStorageLocation",
                schema: "inventory",
                table: "transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedLotId",
                schema: "inventory",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultingLifecycleState",
                schema: "inventory",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultingStorageLocation",
                schema: "inventory",
                table: "transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitionKind",
                schema: "inventory",
                table: "transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FrozenAt",
                schema: "inventory",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleState",
                schema: "inventory",
                table: "lots",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "AsAcquired");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OpenedAt",
                schema: "inventory",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentLotId",
                schema: "inventory",
                table: "lots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThawStartedAt",
                schema: "inventory",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThawedAt",
                schema: "inventory",
                table: "lots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_transition",
                schema: "inventory",
                table: "transactions",
                sql: "(\"Type\" IN ('LifecycleTransition', 'TransitionSplit', 'TransitionDerived') AND \"TransitionKind\" IN ('Open', 'Move', 'Freeze', 'StartThaw', 'CompleteThaw') AND \"PreviousLifecycleState\" IN ('AsAcquired', 'Prepared', 'Frozen', 'Thawing', 'Thawed') AND \"ResultingLifecycleState\" IN ('AsAcquired', 'Prepared', 'Frozen', 'Thawing', 'Thawed') AND \"PreviousStorageLocation\" IN ('Pantry', 'Refrigerator', 'Freezer', 'Other') AND \"ResultingStorageLocation\" IN ('Pantry', 'Refrigerator', 'Freezer', 'Other') AND (\"Type\" = 'LifecycleTransition') = (\"RelatedLotId\" IS NULL)) OR (\"Type\" NOT IN ('LifecycleTransition', 'TransitionSplit', 'TransitionDerived') AND \"TransitionKind\" IS NULL AND \"PreviousLifecycleState\" IS NULL AND \"ResultingLifecycleState\" IS NULL AND \"PreviousStorageLocation\" IS NULL AND \"ResultingStorageLocation\" IS NULL AND \"RelatedLotId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted', 'PreparationInputConsumed', 'PreparationOutputCreated', 'LifecycleTransition', 'TransitionSplit', 'TransitionDerived')");

            migrationBuilder.CreateIndex(
                name: "IX_lots_ParentLotId_OwnerUserId",
                schema: "inventory",
                table: "lots",
                columns: new[] { "ParentLotId", "OwnerUserId" });

            // Existing preparation outputs were created before lots carried a lifecycle state.
            migrationBuilder.Sql("""UPDATE inventory.lots SET "LifecycleState" = 'Prepared' WHERE "Id" IN (SELECT "LotId" FROM inventory.prepared_lots);""");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_lifecycle_state",
                schema: "inventory",
                table: "lots",
                sql: "\"LifecycleState\" IN ('AsAcquired', 'Prepared', 'Frozen', 'Thawing', 'Thawed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lots_parent_not_self",
                schema: "inventory",
                table: "lots",
                sql: "\"ParentLotId\" IS NULL OR \"ParentLotId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "FK_lots_lots_ParentLotId_OwnerUserId",
                schema: "inventory",
                table: "lots",
                columns: new[] { "ParentLotId", "OwnerUserId" },
                principalSchema: "inventory",
                principalTable: "lots",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lots_lots_ParentLotId_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_transition",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_lots_ParentLotId_OwnerUserId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_lifecycle_state",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lots_parent_not_self",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "PreviousLifecycleState",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PreviousStorageLocation",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RelatedLotId",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ResultingLifecycleState",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ResultingStorageLocation",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TransitionKind",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "LifecycleState",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "ParentLotId",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "ThawStartedAt",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "ThawedAt",
                schema: "inventory",
                table: "lots");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted', 'PreparationInputConsumed', 'PreparationOutputCreated')");
        }
    }
}
