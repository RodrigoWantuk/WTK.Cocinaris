using System.Globalization;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Ingredients;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL store of the ingredient identity module (PLAN-0055). Trigram search uses
/// <c>pg_trgm</c> and vector search uses <c>pgvector</c>; vectors travel as pgvector text
/// (<c>[0.1,0.2,…]</c>) cast in SQL, so no vector type mapping is needed in EF Core.
/// </summary>
public sealed class PostgreSqlIngredientStore(ApplicationDbContext database) : IIngredientStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> FindAliasSlugsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken)
    {
        if (normalizedNames.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        var rows = await (from alias in database.IngredientAliases.AsNoTracking()
                          join item in database.IngredientCatalogItems.AsNoTracking() on alias.IngredientId equals item.Id
                          where normalizedNames.Contains(alias.NormalizedText)
                          select new { alias.NormalizedText, item.Slug }).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.NormalizedText, row => row.Slug, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(IReadOnlyCollection<(string NormalizedText, string ExactText)> names, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return;
        }

        var normalized = names.Select(name => name.NormalizedText).ToArray();
        var exact = names.Select(name => name.ExactText).ToArray();
        // ON CONFLICT DO NOTHING: a name already queued, working, or unresolved keeps its row, so
        // repeated reads of the same pantry never multiply work.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingredients.resolution_queue ("NormalizedText", "ExactText", "RequestedByUserId", "State", "Attempts", "NextAttemptAt", "CreatedAt", "UpdatedAt")
            SELECT t.n, t.e, {ownerUserId}, 'Pending', 0, {now}, {now}, {now}
            FROM unnest({normalized}::text[], {exact}::text[]) AS t(n, e)
            ON CONFLICT ("NormalizedText") DO NOTHING
            """, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SyncCatalogAsync(IngredientCatalog catalog, string evaluation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await database.IngredientCatalogState.AsNoTracking().FirstOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (state is not null && state.CatalogVersion == catalog.Version && state.Evaluation == evaluation)
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // One sync at a time; a second worker waits and then finds the state already current.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('cocinaris.ingredients.catalog_sync'))", cancellationToken);
        state = await database.IngredientCatalogState.AsNoTracking().FirstOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (state is null || state.CatalogVersion != catalog.Version)
        {
            var ids = catalog.Items.Select(item => item.Id).ToArray();
            var slugs = catalog.Items.Select(item => item.Slug).ToArray();
            var categories = catalog.Items.Select(item => item.Category).ToArray();
            var names = catalog.Items.Select(item => JsonSerializer.Serialize(item.Names)).ToArray();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ingredients.catalog_items ("Id", "Slug", "Category", "NamesJson", "UpdatedAt")
                SELECT t.i, t.s, t.c, t.n::jsonb, {now} FROM unnest({ids}::int[], {slugs}::text[], {categories}::text[], {names}::text[]) AS t(i, s, c, n)
                ON CONFLICT ("Id") DO UPDATE SET "Slug" = EXCLUDED."Slug", "Category" = EXCLUDED."Category", "NamesJson" = EXCLUDED."NamesJson", "UpdatedAt" = EXCLUDED."UpdatedAt"
                """, cancellationToken);

            var aliasTexts = catalog.Aliases.Keys.ToArray();
            var aliasIds = catalog.Aliases.Values.Select(item => item.Id).ToArray();
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM ingredients.aliases WHERE "Method" = 'Catalog' AND NOT ("NormalizedText" = ANY({aliasTexts}::text[]))
                """, cancellationToken);
            // A curated name wins over a name the job learned earlier: the catalog is authoritative.
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ingredients.aliases AS a ("NormalizedText", "IngredientId", "Method", "Score", "Evaluation", "CreatedAt")
                SELECT t.n, t.i, 'Catalog', 1, {catalog.Version}, {now} FROM unnest({aliasTexts}::text[], {aliasIds}::int[]) AS t(n, i)
                ON CONFLICT ("NormalizedText") DO UPDATE SET "IngredientId" = EXCLUDED."IngredientId", "Method" = 'Catalog', "Score" = 1, "Evaluation" = EXCLUDED."Evaluation"
                WHERE a."Method" <> 'Catalog' OR a."IngredientId" <> EXCLUDED."IngredientId"
                """, cancellationToken);
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM ingredients.resolution_queue WHERE "NormalizedText" = ANY({aliasTexts}::text[])
                """, cancellationToken);
        }

        if (state is null || state.Evaluation != evaluation)
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ingredients.resolution_queue SET "State" = 'Pending', "Attempts" = 0, "NextAttemptAt" = {now}, "UpdatedAt" = {now}
                WHERE "State" = 'Unresolved' AND "Evaluation" IS DISTINCT FROM {evaluation}
                """, cancellationToken);
        }

        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingredients.catalog_state ("Id", "CatalogVersion", "Evaluation", "UpdatedAt") VALUES (1, {catalog.Version}, {evaluation}, {now})
            ON CONFLICT ("Id") DO UPDATE SET "CatalogVersion" = EXCLUDED."CatalogVersion", "Evaluation" = EXCLUDED."Evaluation", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingIngredientName>> ClaimAsync(int max, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        // SKIP LOCKED lets several workers drain the queue without claiming one row twice; an
        // expired lease (a crashed worker) makes a Working row claimable again.
        var claimed = await database.IngredientResolutions.FromSqlInterpolated($"""
            UPDATE ingredients.resolution_queue SET "State" = 'Working', "NextAttemptAt" = {leaseUntil}, "UpdatedAt" = {now}
            WHERE "NormalizedText" IN (
                SELECT "NormalizedText" FROM ingredients.resolution_queue
                WHERE "State" IN ('Pending', 'Working') AND "NextAttemptAt" <= {now}
                ORDER BY "NextAttemptAt", "CreatedAt"
                LIMIT {max}
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).AsNoTracking().ToListAsync(cancellationToken);
        return claimed.Select(item => new PendingIngredientName(item.NormalizedText, item.ExactText, item.RequestedByUserId, item.Attempts)).ToList();
    }

    /// <inheritdoc />
    public async Task ResolveAsync(string normalizedText, int ingredientId, IngredientResolutionMethod method, double score, string evaluation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // DO NOTHING: an alias decided earlier (or a curated one) is never rewritten by the job.
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingredients.aliases ("NormalizedText", "IngredientId", "Method", "Score", "Evaluation", "CreatedAt")
            VALUES ({normalizedText}, {ingredientId}, {method.ToString()}, {score}, {evaluation}, {now})
            ON CONFLICT ("NormalizedText") DO NOTHING
            """, cancellationToken);
        await database.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM ingredients.resolution_queue WHERE "NormalizedText" = {normalizedText}""", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkUnresolvedAsync(string normalizedText, IngredientCandidate? best, string evaluation, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.IngredientResolutions.Where(item => item.NormalizedText == normalizedText).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.State, "Unresolved")
            .SetProperty(item => item.RequestedByUserId, (Guid?)null)
            .SetProperty(item => item.Evaluation, evaluation)
            .SetProperty(item => item.BestIngredientId, best == null ? null : best.IngredientId)
            .SetProperty(item => item.BestScore, best == null ? null : best.Score)
            .SetProperty(item => item.Reason, reason)
            .SetProperty(item => item.NextAttemptAt, now)
            .SetProperty(item => item.UpdatedAt, now), cancellationToken);

    /// <inheritdoc />
    public Task DeferAsync(string normalizedText, int attempts, DateTimeOffset nextAttemptAt, string reason, DateTimeOffset now, CancellationToken cancellationToken) =>
        database.IngredientResolutions.Where(item => item.NormalizedText == normalizedText).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.State, "Pending")
            .SetProperty(item => item.Attempts, attempts)
            .SetProperty(item => item.NextAttemptAt, nextAttemptAt)
            .SetProperty(item => item.Reason, reason.Length > 40 ? reason[..40] : reason)
            .SetProperty(item => item.UpdatedAt, now), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngredientCandidate>> TrigramCandidatesAsync(string normalizedText, CancellationToken cancellationToken)
    {
        // % prefilters with the GIN trigram index (pg_trgm.similarity_threshold, 0.3 by default);
        // only curated names are compared, so a learned alias can never seed another one.
        var rows = await database.Database.SqlQuery<CandidateRow>($"""
            SELECT "IngredientId", similarity("NormalizedText", {normalizedText})::float8 AS "Score"
            FROM ingredients.aliases
            WHERE "Method" = 'Catalog' AND "NormalizedText" % {normalizedText}
            ORDER BY "Score" DESC, "IngredientId"
            LIMIT 8
            """).ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.IngredientId).Select(group => new IngredientCandidate(group.Key, group.Max(row => row.Score)))
            .OrderByDescending(candidate => candidate.Score).Take(2).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, float[]>> FindEmbeddingsAsync(IReadOnlyCollection<string> cacheKeys, CancellationToken cancellationToken)
    {
        if (cacheKeys.Count == 0)
        {
            return new Dictionary<string, float[]>();
        }

        var keys = cacheKeys.ToArray();
        var rows = await database.Database.SqlQuery<VectorRow>($"""
            SELECT "Key", "Embedding"::text AS "Vector" FROM ingredients.text_embeddings WHERE "Key" = ANY({keys}::text[])
            """).ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Key, row => ParseVector(row.Vector), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task SaveEmbeddingsAsync(IReadOnlyDictionary<string, float[]> vectors, string space, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (vectors.Count == 0)
        {
            return;
        }

        var keys = vectors.Keys.ToArray();
        var texts = keys.Select(key => FormatVector(vectors[key])).ToArray();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingredients.text_embeddings ("Key", "Space", "Embedding", "CreatedAt")
            SELECT t.k, {space}, t.v::vector, {now} FROM unnest({keys}::text[], {texts}::text[]) AS t(k, v)
            ON CONFLICT ("Key") DO NOTHING
            """, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> CatalogVectorsToRefreshAsync(string space, IReadOnlyDictionary<int, string> documentKeys, CancellationToken cancellationToken)
    {
        var stored = await database.Database.SqlQuery<DocumentKeyRow>($"""
            SELECT "IngredientId", "DocumentKey" FROM ingredients.catalog_vectors WHERE "Space" = {space}
            """).ToListAsync(cancellationToken);
        var current = stored.ToDictionary(row => row.IngredientId, row => row.DocumentKey);
        return documentKeys.Where(entry => !current.TryGetValue(entry.Key, out var key) || key != entry.Value).Select(entry => entry.Key).Order().ToList();
    }

    /// <inheritdoc />
    public async Task SaveCatalogVectorsAsync(string space, IReadOnlyDictionary<int, (string DocumentKey, float[] Vector)> vectors, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (vectors.Count == 0)
        {
            return;
        }

        var ids = vectors.Keys.ToArray();
        var keys = ids.Select(id => vectors[id].DocumentKey).ToArray();
        var texts = ids.Select(id => FormatVector(vectors[id].Vector)).ToArray();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ingredients.catalog_vectors ("IngredientId", "Space", "DocumentKey", "Embedding", "UpdatedAt")
            SELECT t.i, {space}, t.k, t.v::vector, {now} FROM unnest({ids}::int[], {keys}::text[], {texts}::text[]) AS t(i, k, v)
            ON CONFLICT ("IngredientId", "Space") DO UPDATE SET "DocumentKey" = EXCLUDED."DocumentKey", "Embedding" = EXCLUDED."Embedding", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IngredientCandidate>> NearestAsync(string space, float[] vector, CancellationToken cancellationToken)
    {
        var text = FormatVector(vector);
        // <=> is cosine distance; vectors are unit length, so 1 - distance is the cosine similarity.
        var rows = await database.Database.SqlQuery<CandidateRow>($"""
            SELECT "IngredientId", (1 - ("Embedding" <=> {text}::vector))::float8 AS "Score"
            FROM ingredients.catalog_vectors
            WHERE "Space" = {space}
            ORDER BY "Embedding" <=> {text}::vector, "IngredientId"
            LIMIT 2
            """).ToListAsync(cancellationToken);
        return rows.Select(row => new IngredientCandidate(row.IngredientId, row.Score)).ToList();
    }

    /// <summary>Formats a vector as pgvector text input.</summary>
    public static string FormatVector(float[] vector)
    {
        var builder = new StringBuilder(vector.Length * 11 + 2).Append('[');
        for (var index = 0; index < vector.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(vector[index].ToString("R", CultureInfo.InvariantCulture));
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Parses pgvector text output.</summary>
    public static float[] ParseVector(string text) =>
        text.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();

    private sealed class CandidateRow
    {
        public int IngredientId { get; set; }
        public double Score { get; set; }
    }

    private sealed class VectorRow
    {
        public required string Key { get; set; }
        public required string Vector { get; set; }
    }

    private sealed class DocumentKeyRow
    {
        public int IngredientId { get; set; }
        public required string DocumentKey { get; set; }
    }
}
