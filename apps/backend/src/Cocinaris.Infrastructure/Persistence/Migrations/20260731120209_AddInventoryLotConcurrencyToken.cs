using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLotConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                schema: "inventory",
                table: "lots",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_lots_ConcurrencyToken",
                schema: "inventory",
                table: "lots",
                column: "ConcurrencyToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lots_ConcurrencyToken",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                schema: "inventory",
                table: "lots");
        }
    }
}
