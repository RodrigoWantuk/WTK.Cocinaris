using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ReuseScore",
                schema: "recipes",
                table: "recipe_thumbnails",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceRenderKey",
                schema: "recipes",
                table: "recipe_thumbnails",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReuseScore",
                schema: "recipes",
                table: "recipe_thumbnails");

            migrationBuilder.DropColumn(
                name: "SourceRenderKey",
                schema: "recipes",
                table: "recipe_thumbnails");
        }
    }
}
