using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KitchenFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeAiGatewaySlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "recipes");

            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.CreateTable(
                name: "recipes",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipes", x => x.Id);
                    table.UniqueConstraint("AK_recipes_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("ck_recipes_current_revision_number", "\"CurrentRevisionNumber\" >= 1");
                    table.ForeignKey(
                        name: "FK_recipes_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usage_ledger",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReservedUnits = table.Column<int>(type: "integer", nullable: false),
                    SettledUnits = table.Column<int>(type: "integer", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_ledger", x => x.Id);
                    table.CheckConstraint("ck_usage_ledger_reserved_units", "\"ReservedUnits\" > 0");
                    table.CheckConstraint("ck_usage_ledger_settlement", "(\"Status\" = 'Reserved' AND \"SettledUnits\" IS NULL AND \"Provider\" IS NULL AND \"Model\" IS NULL AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'Settled' AND \"SettledUnits\" IS NOT NULL AND \"SettledUnits\" > 0 AND \"Provider\" IS NOT NULL AND \"Model\" IS NOT NULL AND \"ClosedAt\" IS NOT NULL) OR (\"Status\" = 'Released' AND \"SettledUnits\" IS NULL AND \"ClosedAt\" IS NOT NULL)");
                    table.CheckConstraint("ck_usage_ledger_status", "\"Status\" IN ('Reserved', 'Settled', 'Released')");
                    table.ForeignKey(
                        name: "FK_usage_ledger_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "generation_sessions",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CandidatesSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedCandidateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SelectedRecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectIdempotencyKey = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_sessions", x => x.Id);
                    table.UniqueConstraint("AK_generation_sessions_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("ck_generation_sessions_execution_mode", "\"ExecutionMode\" IN ('cook_now')");
                    table.CheckConstraint("ck_generation_sessions_status", "\"Status\" IN ('AwaitingCandidates', 'CandidatesReady', 'Expanding', 'Selected', 'Failed')");
                    table.ForeignKey(
                        name: "FK_generation_sessions_recipes_SelectedRecipeId_OwnerUserId",
                        columns: x => new { x.SelectedRecipeId, x.OwnerUserId },
                        principalSchema: "recipes",
                        principalTable: "recipes",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_generation_sessions_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recipe_revisions",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MealTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Servings = table.Column<int>(type: "integer", nullable: false),
                    NormalizedRecipeJson = table.Column<string>(type: "jsonb", nullable: false),
                    ThumbnailVisualJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceCandidateId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceGenerationSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_revisions", x => x.Id);
                    table.CheckConstraint("ck_recipe_revisions_name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("ck_recipe_revisions_revision_number", "\"RevisionNumber\" >= 1");
                    table.CheckConstraint("ck_recipe_revisions_servings", "\"Servings\" BETWEEN 1 AND 24");
                    table.CheckConstraint("ck_recipe_revisions_source_candidate_id", "length(btrim(\"SourceCandidateId\")) > 0");
                    table.ForeignKey(
                        name: "FK_recipe_revisions_recipes_RecipeId_OwnerUserId",
                        columns: x => new { x.RecipeId, x.OwnerUserId },
                        principalSchema: "recipes",
                        principalTable: "recipes",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipe_revisions_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_OwnerUserId_CreatedAt",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_OwnerUserId_IdempotencyKey",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "OwnerUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_OwnerUserId_SelectIdempotencyKey",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "OwnerUserId", "SelectIdempotencyKey" },
                unique: true,
                filter: "\"SelectIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_OwnerUserId_UpdatedAt_Id",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "OwnerUserId", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_SelectedRecipeId_OwnerUserId",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "SelectedRecipeId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_revisions_OwnerUserId_RecipeId_RevisionNumber",
                schema: "recipes",
                table: "recipe_revisions",
                columns: new[] { "OwnerUserId", "RecipeId", "RevisionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_revisions_RecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipe_revisions",
                columns: new[] { "RecipeId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_revisions_RecipeId_RevisionNumber",
                schema: "recipes",
                table: "recipe_revisions",
                columns: new[] { "RecipeId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_OwnerUserId_CreatedAt_Id",
                schema: "recipes",
                table: "recipes",
                columns: new[] { "OwnerUserId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_ledger_CreatedAt",
                schema: "ai",
                table: "usage_ledger",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_usage_ledger_OwnerUserId_CreatedAt",
                schema: "ai",
                table: "usage_ledger",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_ledger_OwnerUserId_Status",
                schema: "ai",
                table: "usage_ledger",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.Sql(
                """
                CREATE TRIGGER recipe_revisions_are_append_only
                BEFORE UPDATE OR DELETE ON recipes.recipe_revisions
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS recipe_revisions_are_append_only ON recipes.recipe_revisions;
                """);

            migrationBuilder.DropTable(
                name: "generation_sessions",
                schema: "recipes");

            migrationBuilder.DropTable(
                name: "recipe_revisions",
                schema: "recipes");

            migrationBuilder.DropTable(
                name: "usage_ledger",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "recipes",
                schema: "recipes");
        }
    }
}
