using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MealsCovered",
                schema: "planning",
                table: "menu_entries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SuggestedOrder",
                schema: "planning",
                table: "menu_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shopping_plan_sessions",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreationKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SetupJson = table.Column<string>(type: "jsonb", nullable: false),
                    BatchCount = table.Column<int>(type: "integer", nullable: false),
                    LastBatchKey = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsecutiveRejections = table.Column<int>(type: "integer", nullable: false),
                    LastDecidedCardId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrivateDirection = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_plan_sessions", x => x.Id);
                    table.UniqueConstraint("AK_shopping_plan_sessions_Id_OwnerUserId", x => new { x.Id, x.OwnerUserId });
                    table.CheckConstraint("ck_shopping_plan_sessions_batches", "\"BatchCount\" BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_shopping_plan_sessions_status", "\"Status\" IN ('Building', 'Reviewing', 'Finished', 'Abandoned')");
                    table.ForeignKey(
                        name: "FK_shopping_plan_sessions_menu_plans_MenuPlanId_OwnerUserId",
                        columns: x => new { x.MenuPlanId, x.OwnerUserId },
                        principalSchema: "planning",
                        principalTable: "menu_plans",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shopping_plan_sessions_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shopping_plan_cards",
                schema: "planning",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateJson = table.Column<string>(type: "jsonb", nullable: true),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RejectReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RejectIngredientKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AcceptMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Repeats = table.Column<int>(type: "integer", nullable: false),
                    ExpansionState = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExpansionKey = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenOrder = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_plan_cards", x => x.Id);
                    table.CheckConstraint("ck_shopping_plan_cards_expansion", "\"ExpansionState\" IN ('None', 'Pending', 'Ready', 'Failed')");
                    table.CheckConstraint("ck_shopping_plan_cards_repeats", "\"Repeats\" BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_shopping_plan_cards_source", "\"Source\" IN ('Saved', 'Idea', 'Ai')");
                    table.CheckConstraint("ck_shopping_plan_cards_state", "\"State\" IN ('Unseen', 'Accepted', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_shopping_plan_cards_shopping_plan_sessions_SessionId_OwnerU~",
                        columns: x => new { x.SessionId, x.OwnerUserId },
                        principalSchema: "planning",
                        principalTable: "shopping_plan_sessions",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_menu_entries_meals_covered",
                schema: "planning",
                table: "menu_entries",
                sql: "\"MealsCovered\" BETWEEN 1 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_cards_OwnerUserId_SessionId_FirstSeenOrder",
                schema: "planning",
                table: "shopping_plan_cards",
                columns: new[] { "OwnerUserId", "SessionId", "FirstSeenOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_cards_SessionId_OwnerUserId",
                schema: "planning",
                table: "shopping_plan_cards",
                columns: new[] { "SessionId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_sessions_MenuPlanId",
                schema: "planning",
                table: "shopping_plan_sessions",
                column: "MenuPlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_sessions_MenuPlanId_OwnerUserId",
                schema: "planning",
                table: "shopping_plan_sessions",
                columns: new[] { "MenuPlanId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_sessions_OwnerUserId_CreatedAt",
                schema: "planning",
                table: "shopping_plan_sessions",
                columns: new[] { "OwnerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_plan_sessions_OwnerUserId_CreationKey",
                schema: "planning",
                table: "shopping_plan_sessions",
                columns: new[] { "OwnerUserId", "CreationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_shopping_plan_sessions_in_progress",
                schema: "planning",
                table: "shopping_plan_sessions",
                column: "OwnerUserId",
                unique: true,
                filter: "\"Status\" IN ('Building', 'Reviewing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopping_plan_cards",
                schema: "planning");

            migrationBuilder.DropTable(
                name: "shopping_plan_sessions",
                schema: "planning");

            migrationBuilder.DropCheckConstraint(
                name: "ck_menu_entries_meals_covered",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "MealsCovered",
                schema: "planning",
                table: "menu_entries");

            migrationBuilder.DropColumn(
                name: "SuggestedOrder",
                schema: "planning",
                table: "menu_entries");
        }
    }
}
