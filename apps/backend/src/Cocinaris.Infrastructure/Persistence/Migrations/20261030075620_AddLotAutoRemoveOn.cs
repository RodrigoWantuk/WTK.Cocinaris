using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLotAutoRemoveOn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AutoRemoveOn",
                schema: "inventory",
                table: "lots",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lots_AutoRemoveOn",
                schema: "inventory",
                table: "lots",
                column: "AutoRemoveOn",
                filter: "\"AutoRemoveOn\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_lots_AutoRemoveOn",
                schema: "inventory",
                table: "lots");

            migrationBuilder.DropColumn(
                name: "AutoRemoveOn",
                schema: "inventory",
                table: "lots");
        }
    }
}
