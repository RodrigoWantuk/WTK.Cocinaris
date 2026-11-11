using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Append-only history may be deleted only by an account erasure, which marks its own
            // transaction (PLAN-0049). Updates stay refused, and so does any other deletion.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION platform.reject_immutable_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' AND current_setting('cocinaris.account_erasure', true) = 'on' THEN
                        RETURN OLD;
                    END IF;
                    RAISE EXCEPTION USING
                        ERRCODE = '55000',
                        MESSAGE = format('%I.%I is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME);
                END;
                $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_usage_ledger_users_OwnerUserId",
                schema: "ai",
                table: "usage_ledger");

            migrationBuilder.CreateTable(
                name: "account_deletions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CountsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_deletions", x => x.Id);
                    table.CheckConstraint("ck_account_deletions_owner", "(\"State\" = 'Pending' AND \"OwnerUserId\" IS NOT NULL) OR (\"State\" = 'Completed' AND \"OwnerUserId\" IS NULL AND \"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("ck_account_deletions_state", "\"State\" IN ('Pending', 'Completed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_deletions_OwnerUserId",
                schema: "platform",
                table: "account_deletions",
                column: "OwnerUserId",
                unique: true,
                filter: "\"State\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_account_deletions_RequestedAt",
                schema: "platform",
                table: "account_deletions",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_account_deletions_SubjectHash",
                schema: "platform",
                table: "account_deletions",
                column: "SubjectHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION platform.reject_immutable_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = '55000',
                        MESSAGE = format('%I.%I is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME);
                END;
                $$;
                """);

            migrationBuilder.DropTable(
                name: "account_deletions",
                schema: "platform");

            migrationBuilder.AddForeignKey(
                name: "FK_usage_ledger_users_OwnerUserId",
                schema: "ai",
                table: "usage_ledger",
                column: "OwnerUserId",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
