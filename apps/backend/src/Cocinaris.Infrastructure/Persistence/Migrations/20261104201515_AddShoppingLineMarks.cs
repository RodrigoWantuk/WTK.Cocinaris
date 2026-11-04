using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingLineMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shopping_line_marks",
                schema: "planning",
                columns: table => new
                {
                    MenuPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SubstituteName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Place = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shopping_line_marks", x => new { x.MenuPlanId, x.ItemKey });
                    table.CheckConstraint("ck_shopping_line_marks_place", "\"Place\" IS NULL OR \"Place\" IN ('market', 'street_market', 'butcher', 'bakery')");
                    table.CheckConstraint("ck_shopping_line_marks_state", "\"State\" IS NULL OR \"State\" = 'later'");
                    table.ForeignKey(
                        name: "FK_shopping_line_marks_menu_plans_MenuPlanId_OwnerUserId",
                        columns: x => new { x.MenuPlanId, x.OwnerUserId },
                        principalSchema: "planning",
                        principalTable: "menu_plans",
                        principalColumns: new[] { "Id", "OwnerUserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shopping_line_marks_MenuPlanId_OwnerUserId",
                schema: "planning",
                table: "shopping_line_marks",
                columns: new[] { "MenuPlanId", "OwnerUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopping_line_marks",
                schema: "planning");
        }
    }
}
