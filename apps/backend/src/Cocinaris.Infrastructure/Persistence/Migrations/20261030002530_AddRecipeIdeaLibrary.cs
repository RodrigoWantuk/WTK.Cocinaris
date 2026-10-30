using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeIdeaLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recipe_ideas",
                schema: "recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Signature = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InstructionDetail = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CookingSkill = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Restrictions = table.Column<string[]>(type: "text[]", nullable: false),
                    MealType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IngredientSlugs = table.Column<string[]>(type: "text[]", nullable: false),
                    EquipmentCodes = table.Column<string[]>(type: "text[]", nullable: false),
                    ContentJson = table.Column<string>(type: "jsonb", nullable: false),
                    TimesOffered = table.Column<int>(type: "integer", nullable: false),
                    TimesSelected = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_ideas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_ideas_IngredientSlugs",
                schema: "recipes",
                table: "recipe_ideas",
                column: "IngredientSlugs")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_ideas_Language_MealType",
                schema: "recipes",
                table: "recipe_ideas",
                columns: new[] { "Language", "MealType" });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_ideas_Signature",
                schema: "recipes",
                table: "recipe_ideas",
                column: "Signature",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipe_ideas",
                schema: "recipes");
        }
    }
}
