using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_source_candidate_id",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "recipes",
                table: "recipes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreationKey",
                schema: "recipes",
                table: "recipes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DerivationReason",
                schema: "recipes",
                table: "recipes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                schema: "recipes",
                table: "recipes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "recipes",
                table: "recipes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentRecipeId",
                schema: "recipes",
                table: "recipes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentRevisionNumber",
                schema: "recipes",
                table: "recipes",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceGenerationSessionId",
                schema: "recipes",
                table: "recipe_revisions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "SourceCandidateId",
                schema: "recipes",
                table: "recipe_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                schema: "recipes",
                table: "recipe_revisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                schema: "recipes",
                table: "recipe_revisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Generated");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipes_OwnerUserId_CreationKey",
                schema: "recipes",
                table: "recipes",
                columns: new[] { "OwnerUserId", "CreationKey" },
                unique: true,
                filter: "\"CreationKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_recipes_ParentRecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipes",
                columns: new[] { "ParentRecipeId", "OwnerUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipes_derivation",
                schema: "recipes",
                table: "recipes",
                sql: "(\"Origin\" = 'Derived') = (\"ParentRecipeId\" IS NOT NULL AND \"ParentRevisionNumber\" IS NOT NULL AND \"DerivationReason\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipes_origin",
                schema: "recipes",
                table: "recipes",
                sql: "\"Origin\" IN ('Generated', 'Manual', 'Derived')");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_revisions_SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions",
                column: "SourceExecutionId");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "(\"Origin\" = 'Edited') = (\"RevisionNumber\" > 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_generated_source",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "(\"Origin\" = 'Generated') = (\"SourceCandidateId\" IS NOT NULL AND \"SourceGenerationSessionId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "\"Origin\" IN ('Generated', 'Manual', 'Edited', 'Derived')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_source_candidate_id",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "\"SourceCandidateId\" IS NULL OR length(btrim(\"SourceCandidateId\")) > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_recipe_revisions_recipe_executions_SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions",
                column: "SourceExecutionId",
                principalSchema: "recipes",
                principalTable: "recipe_executions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_recipes_recipes_ParentRecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipes",
                columns: new[] { "ParentRecipeId", "OwnerUserId" },
                principalSchema: "recipes",
                principalTable: "recipes",
                principalColumns: new[] { "Id", "OwnerUserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recipe_revisions_recipe_executions_SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_recipes_recipes_ParentRecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_recipes_OwnerUserId_CreationKey",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_recipes_ParentRecipeId_OwnerUserId",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipes_derivation",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipes_origin",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropIndex(
                name: "IX_recipe_revisions_SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_edited_number",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_generated_source",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_origin",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_recipe_revisions_source_candidate_id",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "CreationKey",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "DerivationReason",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "ParentRecipeId",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "ParentRevisionNumber",
                schema: "recipes",
                table: "recipes");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropColumn(
                name: "Origin",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.DropColumn(
                name: "SourceExecutionId",
                schema: "recipes",
                table: "recipe_revisions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceGenerationSessionId",
                schema: "recipes",
                table: "recipe_revisions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SourceCandidateId",
                schema: "recipes",
                table: "recipe_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_recipe_revisions_source_candidate_id",
                schema: "recipes",
                table: "recipe_revisions",
                sql: "length(btrim(\"SourceCandidateId\")) > 0");
        }
    }
}
