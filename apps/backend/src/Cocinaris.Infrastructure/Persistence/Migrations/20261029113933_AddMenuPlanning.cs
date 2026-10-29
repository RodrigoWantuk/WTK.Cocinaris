using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "planning");

            migrationBuilder.AddColumn<Guid>(
                name: "MenuEntryId",
                schema: "recipes",
                table: "recipe_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "menu_plans",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_plans", x => x.Id);
                    table.UniqueConstraint("AK_menu_plans_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("ck_menu_plans_accepted", "\"Status\" <> 'Accepted' OR \"AcceptedAt\" IS NOT NULL");
                    table.CheckConstraint("ck_menu_plans_period", "\"EndDate\" >= \"StartDate\" AND \"EndDate\" - \"StartDate\" < 31");
                    table.CheckConstraint("ck_menu_plans_status", "\"Status\" IN ('Draft', 'Accepted', 'Archived')");
                    table.ForeignKey(
                        name: "FK_menu_plans_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "menu_entries",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PinnedRevisionNumber = table.Column<int>(type: "integer", nullable: true),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MealType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Servings = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_entries", x => x.Id);
                    table.CheckConstraint("ck_menu_entries_meal_type", "\"MealType\" IS NULL OR \"MealType\" IN ('breakfast', 'lunch', 'dinner', 'snack')");
                    table.CheckConstraint("ck_menu_entries_pinned_revision", "\"PinnedRevisionNumber\" IS NULL OR \"PinnedRevisionNumber\" >= 1");
                    table.CheckConstraint("ck_menu_entries_servings", "\"Servings\" BETWEEN 1 AND 24");
                    table.CheckConstraint("ck_menu_entries_status", "\"Status\" IN ('Planned', 'Skipped')");
                    table.ForeignKey(
                        name: "FK_menu_entries_menu_plans_MenuPlanId_OwnerUserId",
                        columns: x => new { x.MenuPlanId, x.OwnerUserId },
                        principalSchema: "planning",
                        principalTable: "menu_plans",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_menu_entries_recipes_RecipeId_OwnerUserId",
                        columns: x => new { x.RecipeId, x.OwnerUserId },
                        principalSchema: "recipes",
                        principalTable: "recipes",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_menu_entries_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shopping_checks",
                schema: "planning",
                columns: table => new
                {
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_checks", x => new { x.MenuPlanId, x.ItemKey });
                    table.ForeignKey(
                        name: "FK_shopping_checks_menu_plans_MenuPlanId_OwnerUserId",
                        columns: x => new { x.MenuPlanId, x.OwnerUserId },
                        principalSchema: "planning",
                        principalTable: "menu_plans",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shopping_checks_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_executions_MenuEntryId",
                schema: "recipes",
                table: "recipe_executions",
                column: "MenuEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_entries_MenuPlanId_OwnerUserId",
                schema: "planning",
                table: "menu_entries",
                columns: new[] { "MenuPlanId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_entries_OwnerUserId_CreationKey",
                schema: "planning",
                table: "menu_entries",
                columns: new[] { "OwnerUserId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_entries_OwnerUserId_MenuPlanId_PlannedDate",
                schema: "planning",
                table: "menu_entries",
                columns: new[] { "OwnerUserId", "MenuPlanId", "PlannedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_entries_RecipeId_OwnerUserId",
                schema: "planning",
                table: "menu_entries",
                columns: new[] { "RecipeId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_plans_OwnerUserId_CreationKey",
                schema: "planning",
                table: "menu_plans",
                columns: new[] { "OwnerUserId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_plans_OwnerUserId_Status_StartDate",
                schema: "planning",
                table: "menu_plans",
                columns: new[] { "OwnerUserId", "Status", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_checks_MenuPlanId_OwnerUserId",
                schema: "planning",
                table: "shopping_checks",
                columns: new[] { "MenuPlanId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_checks_OwnerUserId",
                schema: "planning",
                table: "shopping_checks",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_recipe_executions_menu_entries_MenuEntryId",
                schema: "recipes",
                table: "recipe_executions",
                column: "MenuEntryId",
                principalSchema: "planning",
                principalTable: "menu_entries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recipe_executions_menu_entries_MenuEntryId",
                schema: "recipes",
                table: "recipe_executions");

            migrationBuilder.DropTable(
                name: "menu_entries",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "shopping_checks",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "menu_plans",
                schema: "planning");

            migrationBuilder.DropIndex(
                name: "IX_recipe_executions_MenuEntryId",
                schema: "recipes",
                table: "recipe_executions");

            migrationBuilder.DropColumn(
                name: "MenuEntryId",
                schema: "recipes",
                table: "recipe_executions");
        }
    }
}
