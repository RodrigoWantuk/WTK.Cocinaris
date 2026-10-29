using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTokenWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstimatedTokens",
                schema: "ai",
                table: "usage_ledger",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedTokens",
                schema: "ai",
                table: "usage_ledger");
        }
    }
}
