using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reminder_dismissals",
                schema: "planning",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReminderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_dismissals", x => new { x.OwnerUserId, x.MenuEntryId, x.Kind });
                    table.CheckConstraint("ck_reminder_dismissals_kind", "\"Kind\" IN ('cook_by')");
                    table.ForeignKey(
                        name: "FK_reminder_dismissals_menu_entries_MenuEntryId",
                        column: x => x.MenuEntryId,
                        principalSchema: "planning",
                        principalTable: "menu_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_reminder_dismissals_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reminder_dismissals_MenuEntryId",
                schema: "planning",
                table: "reminder_dismissals",
                column: "MenuEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_dismissals",
                schema: "planning");
        }
    }
}
