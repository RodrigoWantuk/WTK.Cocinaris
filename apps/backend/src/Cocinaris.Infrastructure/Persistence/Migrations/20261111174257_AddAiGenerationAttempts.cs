using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGenerationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generation_attempts",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Operation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Call = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rules = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generation_attempts", x => x.Id);
                    table.CheckConstraint("ck_generation_attempts_outcome", "\"Outcome\" IN ('accepted', 'accepted_with_issues', 'quality_issues', 'invalid_output', 'timeout', 'unavailable')");
                    table.CheckConstraint("ck_generation_attempts_round", "\"Round\" BETWEEN 1 AND 2");
                });

            migrationBuilder.CreateIndex(
                name: "IX_generation_attempts_OccurredAt",
                schema: "ai",
                table: "generation_attempts",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_generation_attempts_Operation_OccurredAt",
                schema: "ai",
                table: "generation_attempts",
                columns: new[] { "Operation", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generation_attempts",
                schema: "ai");
        }
    }
}
