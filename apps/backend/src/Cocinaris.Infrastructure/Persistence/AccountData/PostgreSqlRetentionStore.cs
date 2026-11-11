using Cocinaris.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence.AccountData;

/// <summary>
/// Applies <see cref="RetentionCatalog.Rules"/> in PostgreSQL (PLAN-0049 Slice 3). Each rule is one
/// capped statement in its own implicit transaction, so a large backlog never holds a long lock and a
/// failed rule leaves the others done.
/// </summary>
/// <remarks>SQL is fixed per rule; only cutoffs and the cap are parameters. Nothing reads or logs row content.</remarks>
public sealed class PostgreSqlRetentionStore(ApplicationDbContext database) : IRetentionStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, int>> ApplyAsync(RetentionCutoffs cutoffs, int maxRowsPerRule, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in RetentionCatalog.Rules)
        {
            var (sql, cutoff) = Statement(rule.Name, cutoffs);
            object[] parameters = cutoff is null
                ? [new NpgsqlParameter("limit", maxRowsPerRule)]
                : [new NpgsqlParameter("limit", maxRowsPerRule), new NpgsqlParameter("cutoff", cutoff)];
            counts[rule.Name] = await database.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }

        return counts;
    }

    // ctid limits each statement without an index on the filter; the inner query takes the oldest rows.
    private static (string Sql, object? Cutoff) Statement(string rule, RetentionCutoffs cutoffs) => rule switch
    {
        "idempotency" => ("""
            DELETE FROM platform.idempotency_records WHERE ctid IN (
                SELECT ctid FROM platform.idempotency_records WHERE "CreatedAt" < @cutoff ORDER BY "CreatedAt" LIMIT @limit)
            """, cutoffs.IdempotencyBefore),
        "ai_usage" => ("""
            DELETE FROM ai.usage_ledger WHERE ctid IN (
                SELECT ctid FROM ai.usage_ledger WHERE "CreatedAt" < @cutoff ORDER BY "CreatedAt" LIMIT @limit)
            """, cutoffs.UsageBefore),
        "deletion_tombstones" => ("""
            DELETE FROM platform.account_deletions WHERE ctid IN (
                SELECT ctid FROM platform.account_deletions WHERE "State" = 'Completed' AND "CompletedAt" < @cutoff LIMIT @limit)
            """, cutoffs.TombstonesBefore),
        "push_delivery_days" => ("""
            DELETE FROM planning.push_delivery_days WHERE ctid IN (
                SELECT ctid FROM planning.push_delivery_days WHERE "LastSentOn" < @cutoff LIMIT @limit)
            """, cutoffs.PushDaysBefore),
        // Cards go with their plan through the cascade on shopping_plan_cards.
        "abandoned_plans" => ("""
            DELETE FROM planning.shopping_plan_sessions WHERE ctid IN (
                SELECT ctid FROM planning.shopping_plan_sessions WHERE "Status" = 'Abandoned' AND "UpdatedAt" < @cutoff LIMIT @limit)
            """, cutoffs.AbandonedPlansBefore),
        // The image stays for everyone; the column cannot be null, so the empty id names nobody.
        "orphan_thumbnail_owner" => ("""
            UPDATE recipes.recipe_thumbnails SET "RequestedByUserId" = '00000000-0000-0000-0000-000000000000'
            WHERE ctid IN (
                SELECT t.ctid FROM recipes.recipe_thumbnails AS t
                WHERE t."RequestedByUserId" <> '00000000-0000-0000-0000-000000000000'
                  AND NOT EXISTS (SELECT 1 FROM identity.users AS u WHERE u."Id" = t."RequestedByUserId")
                LIMIT @limit)
            """, null),
        "orphan_ingredient_owner" => ("""
            UPDATE ingredients.resolution_queue SET "RequestedByUserId" = NULL
            WHERE ctid IN (
                SELECT q.ctid FROM ingredients.resolution_queue AS q
                WHERE q."RequestedByUserId" IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM identity.users AS u WHERE u."Id" = q."RequestedByUserId")
                LIMIT @limit)
            """, null),
        _ => throw new InvalidOperationException($"No statement for retention rule {rule}."),
    };
}
