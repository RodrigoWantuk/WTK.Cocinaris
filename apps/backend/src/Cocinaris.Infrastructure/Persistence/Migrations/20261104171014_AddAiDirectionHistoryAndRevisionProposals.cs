using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDirectionHistoryAndRevisionProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.AddColumn<string>(
                name: "Adaptation",
                schema: "recipes",
                table: "generation_sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PrivateDirection",
                schema: "recipes",
                table: "generation_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedFromHistoryAt",
                schema: "recipes",
                table: "generation_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSessionId",
                schema: "recipes",
                table: "generation_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserHint",
                schema: "recipes",
                table: "generation_sessions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "revision_proposals",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Instruction = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MealTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Servings = table.Column<int>(type: "integer", nullable: true),
                    NormalizedRecipeJson = table.Column<string>(type: "jsonb", nullable: true),
                    ThumbnailVisualJson = table.Column<string>(type: "jsonb", nullable: true),
                    AcceptedRevisionNumber = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_revision_proposals", x => x.Id);
                    table.CheckConstraint("ck_revision_proposals_status", "\"Status\" IN ('Generating', 'Ready', 'Accepted', 'Discarded', 'Failed')");
                    table.ForeignKey(
                        name: "FK_revision_proposals_recipes_RecipeId_OwnerUserId",
                        columns: x => new { x.RecipeId, x.OwnerUserId },
                        principalSchema: "recipes",
                        principalTable: "recipes",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_revision_proposals_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "(\"Origin\" IN ('Edited', 'AiRevised')) = (\"RevisionNumber\" > 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "\"Origin\" IN ('Generated', 'Manual', 'Edited', 'Derived', 'AiRevised')");

            migrationBuilder.CreateIndex(
                name: "IX_generation_sessions_OwnerUserId_SourceSessionId",
                schema: "recipes",
                table: "generation_sessions",
                columns: new[] { "OwnerUserId", "SourceSessionId" },
                filter: "\"SourceSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_revision_proposals_OwnerUserId_IdempotencyKey",
                schema: "recipes",
                table: "revision_proposals",
                columns: new[] { "OwnerUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_revision_proposals_OwnerUserId_RecipeId_CreatedAt",
                schema: "recipes",
                table: "revision_proposals",
                columns: new[] { "OwnerUserId", "RecipeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_revision_proposals_RecipeId_OwnerUserId",
                schema: "recipes",
                table: "revision_proposals",
                columns: new[] { "RecipeId", "OwnerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revision_proposals",
                schema: "recipes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropIndex(
                name: "IX_generation_sessions_OwnerUserId_SourceSessionId",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.DropColumn(
                name: "Adaptation",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.DropColumn(
                name: "PrivateDirection",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.DropColumn(
                name: "RemovedFromHistoryAt",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.DropColumn(
                name: "SourceSessionId",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.DropColumn(
                name: "UserHint",
                schema: "recipes",
                table: "generation_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "(\"Origin\" = 'Edited') = (\"RevisionNumber\" > 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "\"Origin\" IN ('Generated', 'Manual', 'Edited', 'Derived')");
        }
    }
}
