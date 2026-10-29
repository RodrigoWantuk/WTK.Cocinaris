using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_preparation_batches_source",
                schema: "inventory",
                table: "preparation_batches");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReferenceId",
                schema: "inventory",
                table: "preparation_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recipe_executions",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Servings = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InventoryBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionKey = table.Column<Guid>(type: "uuid", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_executions", x => x.Id);
                    table.CheckConstraint("ck_recipe_executions_completion", "(\"Status\" = 'Completed') = (\"CompletionKey\" IS NOT NULL)");
                    table.CheckConstraint("ck_recipe_executions_finished", "(\"Status\" = 'InProgress') = (\"FinishedAt\" IS NULL)");
                    table.CheckConstraint("ck_recipe_executions_rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_recipe_executions_servings", "\"Servings\" BETWEEN 1 AND 24");
                    table.CheckConstraint("ck_recipe_executions_status", "\"Status\" IN ('InProgress', 'Completed', 'Abandoned')");
                    table.ForeignKey(
                        name: "FK_recipe_executions_recipes_RecipeId_OwnerUserId",
                        columns: x => new { x.RecipeId, x.OwnerUserId },
                        principalSchema: "recipes",
                        principalTable: "recipes",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipe_executions_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_preparation_batches_OwnerUserId_SourceReferenceId",
                schema: "inventory",
                table: "preparation_batches",
                columns: new[] { "OwnerUserId", "SourceReferenceId" },
                unique: true,
                filter: "\"SourceReferenceId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_preparation_batches_source",
                schema: "inventory",
                table: "preparation_batches",
                sql: "\"SourceType\" IN ('ManualPreparation', 'RecipeExecution')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_preparation_batches_source_reference",
                schema: "inventory",
                table: "preparation_batches",
                sql: "(\"SourceType\" = 'RecipeExecution') = (\"SourceReferenceId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_executions_OwnerUserId_RecipeId",
                schema: "recipes",
                table: "recipe_executions",
                columns: new[] { "OwnerUserId", "RecipeId" },
                unique: true,
                filter: "\"Status\" = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_executions_OwnerUserId_Status_StartedAt",
                schema: "recipes",
                table: "recipe_executions",
                columns: new[] { "OwnerUserId", "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_executions_RecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipe_executions",
                columns: new[] { "RecipeId", "OwnerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipe_executions",
                schema: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_preparation_batches_OwnerUserId_SourceReferenceId",
                schema: "inventory",
                table: "preparation_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_preparation_batches_source",
                schema: "inventory",
                table: "preparation_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_preparation_batches_source_reference",
                schema: "inventory",
                table: "preparation_batches");

            migrationBuilder.DropColumn(
                name: "SourceReferenceId",
                schema: "inventory",
                table: "preparation_batches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_preparation_batches_source",
                schema: "inventory",
                table: "preparation_batches",
                sql: "\"SourceType\" IN ('ManualPreparation')");
        }
    }
}
