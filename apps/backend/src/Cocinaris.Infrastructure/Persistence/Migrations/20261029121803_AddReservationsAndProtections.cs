using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationsAndProtections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReserveIngredients",
                schema: "planning",
                table: "menu_entries",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "lot_protections",
                schema: "planning",
                columns: table => new
                {
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Whole = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumQuantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lot_protections", x => new { x.OwnerUserId, x.LotId });
                    table.CheckConstraint("ck_lot_protections_kind", "\"Whole\" = (\"MinimumQuantity\" IS NULL) AND (\"MinimumQuantity\" IS NULL OR \"MinimumQuantity\" > 0)");
                    table.ForeignKey(
                        name: "FK_lot_protections_lots_LotId",
                        column: x => x.LotId,
                        principalSchema: "inventory",
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lot_protections_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lot_protections_LotId",
                schema: "planning",
                table: "lot_protections",
                column: "LotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lot_protections",
                schema: "planning");

            migrationBuilder.DropColumn(
                name: "ReserveIngredients",
                schema: "planning",
                table: "menu_entries");
        }
    }
}
