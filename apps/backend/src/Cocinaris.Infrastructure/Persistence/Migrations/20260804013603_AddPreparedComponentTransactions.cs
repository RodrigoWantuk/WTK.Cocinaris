using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparedComponentTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.CreateTable(
                name: "preparation_batches",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutputProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeclaredYieldMeasuredValue = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    DeclaredYieldMeasuredUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DeclaredYieldAvailabilityState = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_batches", x => x.Id);
                    table.UniqueConstraint("AK_preparation_batches_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("ck_preparation_batches_declared_yield", "((\"DeclaredYieldMeasuredValue\" IS NOT NULL AND \"DeclaredYieldMeasuredValue\" > 0 AND \"DeclaredYieldMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"DeclaredYieldAvailabilityState\" IS NULL) OR (\"DeclaredYieldMeasuredValue\" IS NULL AND \"DeclaredYieldMeasuredUnit\" IS NULL AND \"DeclaredYieldAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))) IS TRUE");
                    table.CheckConstraint("ck_preparation_batches_prepared_at", "\"PreparedAt\" <= \"CreatedAt\"");
                    table.CheckConstraint("ck_preparation_batches_source", "\"SourceType\" IN ('ManualPreparation')");
                    table.ForeignKey(
                        name: "FK_preparation_batches_products_OutputProductId_OwnerUserId",
                        columns: x => new { x.OutputProductId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "products",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_batches_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "preparation_inputs",
                schema: "inventory",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedValue = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    ConsumedUnit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_inputs", x => new { x.BatchId, x.InputLotId });
                    table.CheckConstraint("ck_preparation_inputs_quantity", "\"ConsumedValue\" > 0 AND \"ConsumedUnit\" IN ('Gram', 'Milliliter', 'Unit')");
                    table.ForeignKey(
                        name: "FK_preparation_inputs_lots_InputLotId_OwnerUserId",
                        columns: x => new { x.InputLotId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_inputs_preparation_batches_BatchId_OwnerUserId",
                        columns: x => new { x.BatchId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "preparation_batches",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "preparation_outputs",
                schema: "inventory",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutputLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preparation_outputs", x => new { x.BatchId, x.OutputLotId });
                    table.ForeignKey(
                        name: "FK_preparation_outputs_lots_OutputLotId_OwnerUserId",
                        columns: x => new { x.OutputLotId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_preparation_outputs_preparation_batches_BatchId_OwnerUserId",
                        columns: x => new { x.BatchId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "preparation_batches",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prepared_lots",
                schema: "inventory",
                columns: table => new
                {
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShelfLifeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ShelfLifeSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShelfLifeConfidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShelfLifeConditions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prepared_lots", x => new { x.LotId, x.OwnerUserId });
                    table.CheckConstraint("ck_prepared_lots_evidence", "(\"ShelfLifeSource\" = 'Unknown' AND \"ShelfLifeDate\" IS NULL AND \"ShelfLifeConfidence\" = 'Unknown') OR (\"ShelfLifeSource\" IN ('UserEntered', 'Curated', 'Regional') AND \"ShelfLifeDate\" IS NOT NULL AND \"ShelfLifeConfidence\" IN ('Low', 'Medium', 'High'))");
                    table.CheckConstraint("ck_prepared_lots_lifecycle", "\"LifecycleState\" IN ('Prepared')");
                    table.ForeignKey(
                        name: "FK_prepared_lots_lots_LotId_OwnerUserId",
                        columns: x => new { x.LotId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_prepared_lots_preparation_batches_BatchId_OwnerUserId",
                        columns: x => new { x.BatchId, x.OwnerUserId },
                        principalSchema: "inventory",
                        principalTable: "preparation_batches",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted', 'PreparationInputConsumed', 'PreparationOutputCreated')");

            migrationBuilder.CreateIndex(
                name: "IX_preparation_batches_OutputProductId_OwnerUserId",
                schema: "inventory",
                table: "preparation_batches",
                columns: new[] { "OutputProductId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_batches_OwnerUserId_PreparedAt_Id",
                schema: "inventory",
                table: "preparation_batches",
                columns: new[] { "OwnerUserId", "PreparedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_inputs_BatchId_OwnerUserId",
                schema: "inventory",
                table: "preparation_inputs",
                columns: new[] { "BatchId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_inputs_InputLotId_OwnerUserId",
                schema: "inventory",
                table: "preparation_inputs",
                columns: new[] { "InputLotId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_inputs_OwnerUserId_InputLotId",
                schema: "inventory",
                table: "preparation_inputs",
                columns: new[] { "OwnerUserId", "InputLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_outputs_BatchId_OwnerUserId",
                schema: "inventory",
                table: "preparation_outputs",
                columns: new[] { "BatchId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_outputs_OutputLotId_OwnerUserId",
                schema: "inventory",
                table: "preparation_outputs",
                columns: new[] { "OutputLotId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_outputs_OwnerUserId_OutputLotId",
                schema: "inventory",
                table: "preparation_outputs",
                columns: new[] { "OwnerUserId", "OutputLotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prepared_lots_BatchId_OwnerUserId",
                schema: "inventory",
                table: "prepared_lots",
                columns: new[] { "BatchId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_prepared_lots_OwnerUserId_BatchId",
                schema: "inventory",
                table: "prepared_lots",
                columns: new[] { "OwnerUserId", "BatchId" });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER preparation_batches_are_append_only
                BEFORE UPDATE OR DELETE ON inventory.preparation_batches
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

                CREATE TRIGGER preparation_inputs_are_append_only
                BEFORE UPDATE OR DELETE ON inventory.preparation_inputs
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

                CREATE TRIGGER preparation_outputs_are_append_only
                BEFORE UPDATE OR DELETE ON inventory.preparation_outputs
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

                CREATE TRIGGER prepared_lots_are_append_only
                BEFORE UPDATE OR DELETE ON inventory.prepared_lots
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS prepared_lots_are_append_only ON inventory.prepared_lots;
                DROP TRIGGER IF EXISTS preparation_outputs_are_append_only ON inventory.preparation_outputs;
                DROP TRIGGER IF EXISTS preparation_inputs_are_append_only ON inventory.preparation_inputs;
                DROP TRIGGER IF EXISTS preparation_batches_are_append_only ON inventory.preparation_batches;
                """);
            migrationBuilder.DropTable(
                name: "preparation_inputs",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "preparation_outputs",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "prepared_lots",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "preparation_batches",
                schema: "inventory");

            migrationBuilder.DropCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_transactions_type",
                schema: "inventory",
                table: "transactions",
                sql: "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted')");
        }
    }
}
