using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveAccountDeletionAttemptsToJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                schema: "platform",
                table: "account_deletions");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                schema: "platform",
                table: "account_deletions");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "platform",
                table: "account_deletions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                schema: "platform",
                table: "account_deletions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                schema: "platform",
                table: "account_deletions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "platform",
                table: "account_deletions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }
    }
}
