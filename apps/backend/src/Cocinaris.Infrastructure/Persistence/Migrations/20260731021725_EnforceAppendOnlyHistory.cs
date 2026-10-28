using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cocinaris.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceAppendOnlyHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE FUNCTION platform.reject_immutable_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = '55000',
                        MESSAGE = format('%I.%I is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME);
                END;
                $$;

                CREATE TRIGGER transactions_are_append_only
                BEFORE UPDATE OR DELETE ON inventory.transactions
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

                CREATE TRIGGER audit_events_are_append_only
                BEFORE UPDATE OR DELETE ON platform.audit_events
                FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS transactions_are_append_only ON inventory.transactions;
                DROP TRIGGER IF EXISTS audit_events_are_append_only ON platform.audit_events;
                DROP FUNCTION IF EXISTS platform.reject_immutable_history_mutation();
                """);
        }
    }
}
