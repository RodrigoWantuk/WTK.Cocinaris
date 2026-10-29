using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recipe_thumbnails",
                schema: "recipes",
                columns: table => new
                {
                    RenderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VisualIdentityKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DescriptorJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FileName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    MediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_thumbnails", x => x.RenderKey);
                    table.CheckConstraint("ck_recipe_thumbnails_ready_file", "(\"State\" = 'Ready') = (\"FileName\" IS NOT NULL)");
                    table.CheckConstraint("ck_recipe_thumbnails_state", "\"State\" IN ('Pending', 'Generating', 'Ready', 'Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_recipe_thumbnails_State_NextAttemptAt",
                schema: "recipes",
                table: "recipe_thumbnails",
                columns: new[] { "State", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recipe_thumbnails",
                schema: "recipes");
        }
    }
}
