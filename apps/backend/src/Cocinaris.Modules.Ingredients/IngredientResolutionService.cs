using System.Security.Cryptography;
using System.Text;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Embeddings;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Identity;

namespace Cocinaris.Modules.Ingredients;

/// <summary>
/// Background resolution of queued ingredient names (PLAN-0055, ADR-0010). Each name goes through
/// the cheapest step that can decide it:
/// <list type="number">
/// <item>a curated catalog name (the catalog may have grown since the name was queued);</item>
/// <item>trigram similarity to one curated name, high and unambiguous;</item>
/// <item>the embedding of the exact text, read from the cache by exact text or requested once
/// from the provider, compared with every catalog ingredient's embedding.</item>
/// </list>
/// A name no step decides stays unresolved and keeps matching by its text, as before the catalog.
/// </summary>
/// <remarks>
/// Deterministic code owns every decision: the provider only returns vectors, and a link needs a
/// similarity above the threshold and a clear margin over the next ingredient. Decided aliases
/// are never rewritten by the job. Provider calls go through the usage ledger, charged to the
/// owner whose name needed them, or to <see cref="SystemPrincipals.Maintenance"/> when none did.
/// </remarks>
public sealed class IngredientResolutionService(
    IIngredientStore store,
    IngredientCatalog catalog,
    IEmbeddingProvider? provider,
    EmbeddingOptions embeddingOptions,
    IngredientResolutionOptions options,
    AiUsageGovernor governor,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Identifies everything that decides a resolution: catalog content, vector space, query
    /// instruction, and thresholds. Unresolved names are re-checked when it changes.
    /// </summary>
    public string Evaluation => string.Join('|',
        catalog.Version,
        provider?.Space ?? "no-embeddings",
        options.InstructionVersion,
        FormattableString.Invariant($"t{options.TrigramThreshold:0.###}/{options.TrigramMargin:0.###}"),
        FormattableString.Invariant($"e{options.EmbeddingThreshold:0.###}/{options.EmbeddingMargin:0.###}"));

    /// <summary>
    /// Embedding cache key of one text: SHA-256 of the vector space, the role, the instruction
    /// version, and the exact text. Only the identical text in the identical setup reuses a vector
    /// (owner decision 2026-10-30), and the text itself is not stored.
    /// </summary>
    public static string CacheKey(string space, string role, string instructionVersion, string exactText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{space}\n{role}\n{instructionVersion}\n{exactText}")));

    /// <summary>Syncs the catalog, then resolves at most one batch. Returns true when a batch was claimed.</summary>
    public async Task<bool> ProcessNextBatchAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var evaluation = Evaluation;
        await store.SyncCatalogAsync(catalog, evaluation, now, cancellationToken);
        var claimed = await store.ClaimAsync(options.BatchSize, now, now.AddSeconds(options.LeaseSeconds), cancellationToken);
        if (claimed.Count == 0)
        {
            return false;
        }

        var remaining = new List<PendingIngredientName>();
        foreach (var name in claimed)
        {
            if (catalog.Aliases.TryGetValue(name.NormalizedText, out var item))
            {
                await store.ResolveAsync(name.NormalizedText, item.Id, IngredientResolutionMethod.Catalog, 1, evaluation, now, cancellationToken);
                continue;
            }

            var trigram = await store.TrigramCandidatesAsync(name.NormalizedText, cancellationToken);
            if (Decide(trigram, options.TrigramThreshold, options.TrigramMargin) is { } byTrigram)
            {
                await store.ResolveAsync(name.NormalizedText, byTrigram.IngredientId, IngredientResolutionMethod.Trigram, byTrigram.Score, evaluation, now, cancellationToken);
                continue;
            }

            remaining.Add(name);
        }

        if (remaining.Count == 0)
        {
            return true;
        }

        if (provider is null)
        {
            foreach (var name in remaining)
            {
                await store.MarkUnresolvedAsync(name.NormalizedText, null, evaluation, "embedding_unavailable", now, cancellationToken);
            }

            return true;
        }

        var chargeTo = remaining.Select(name => name.RequestedByUserId).FirstOrDefault(owner => owner is not null) ?? SystemPrincipals.Maintenance;
        if (!await EnsureCatalogVectorsAsync(chargeTo, cancellationToken))
        {
            await DeferAllAsync(remaining, "catalog_vectors", now, cancellationToken);
            return true;
        }

        var keys = remaining.ToDictionary(name => name.NormalizedText, name => CacheKey(provider.Space, "query", options.InstructionVersion, name.ExactText), StringComparer.Ordinal);
        var cached = await store.FindEmbeddingsAsync(keys.Values.Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var toEmbed = new List<PendingIngredientName>();
        foreach (var name in remaining)
        {
            if (cached.TryGetValue(keys[name.NormalizedText], out var vector))
            {
                vectors[name.NormalizedText] = vector;
            }
            else
            {
                toEmbed.Add(name);
            }
        }

        // One provider call per charged owner; a batch is at most BatchSize names, within one request.
        foreach (var group in toEmbed.GroupBy(name => name.RequestedByUserId ?? SystemPrincipals.Maintenance))
        {
            var names = group.ToList();
            var inputs = names.Select(name => $"Instruct: {options.QueryInstruction}\nQuery: {name.ExactText}").ToList();
            var embedded = await EmbedChargedAsync(group.Key, inputs, $"ingredient:{keys[names[0].NormalizedText][..16]}", cancellationToken);
            if (embedded.Vectors is not { } result)
            {
                await DeferAllAsync(names, embedded.Reason, now, cancellationToken);
                continue;
            }

            var toCache = new Dictionary<string, float[]>(StringComparer.Ordinal);
            for (var index = 0; index < names.Count; index++)
            {
                vectors[names[index].NormalizedText] = result[index];
                toCache[keys[names[index].NormalizedText]] = result[index];
            }

            await store.SaveEmbeddingsAsync(toCache, provider.Space, now, cancellationToken);
        }

        foreach (var name in remaining)
        {
            if (!vectors.TryGetValue(name.NormalizedText, out var vector))
            {
                continue;
            }

            var nearest = await store.NearestAsync(provider.Space, vector, cancellationToken);
            if (Decide(nearest, options.EmbeddingThreshold, options.EmbeddingMargin) is { } byEmbedding)
            {
                await store.ResolveAsync(name.NormalizedText, byEmbedding.IngredientId, IngredientResolutionMethod.Embedding, byEmbedding.Score, evaluation, now, cancellationToken);
            }
            else
            {
                await store.MarkUnresolvedAsync(name.NormalizedText, nearest.FirstOrDefault(), evaluation, nearest.Count == 0 ? "no_catalog_vectors" : "below_threshold", now, cancellationToken);
            }
        }

        return true;
    }

    /// <summary>
    /// Picks the best candidate when it reaches <paramref name="threshold"/> and beats a different
    /// second candidate by <paramref name="margin"/>; otherwise none. Candidates are best first.
    /// </summary>
    public static IngredientCandidate? Decide(IReadOnlyList<IngredientCandidate> candidates, double threshold, double margin)
    {
        if (candidates.Count == 0 || candidates[0].Score < threshold)
        {
            return null;
        }

        var second = candidates.Skip(1).FirstOrDefault(candidate => candidate.IngredientId != candidates[0].IngredientId);
        return second is null || candidates[0].Score - second.Score >= margin ? candidates[0] : null;
    }

    private async Task<bool> EnsureCatalogVectorsAsync(Guid chargeTo, CancellationToken cancellationToken)
    {
        var documentKeys = catalog.Items.ToDictionary(item => item.Id, item => CacheKey(provider!.Space, "document", "d1", item.DocumentText));
        var refresh = await store.CatalogVectorsToRefreshAsync(provider!.Space, documentKeys, cancellationToken);
        foreach (var chunk in refresh.Chunk(options.BatchSize))
        {
            var embedded = await EmbedChargedAsync(chargeTo, chunk.Select(id => catalog.ById[id].DocumentText).ToList(), "ingredient-catalog", cancellationToken);
            if (embedded.Vectors is not { } result)
            {
                return false;
            }

            var vectors = new Dictionary<int, (string, float[])>();
            for (var index = 0; index < chunk.Length; index++)
            {
                vectors[chunk[index]] = (documentKeys[chunk[index]], result[index]);
            }

            await store.SaveCatalogVectorsAsync(provider.Space, vectors, timeProvider.GetUtcNow(), cancellationToken);
        }

        return true;
    }

    private async Task<(IReadOnlyList<float[]>? Vectors, string Reason)> EmbedChargedAsync(Guid ownerUserId, IReadOnlyList<string> inputs, string correlationId, CancellationToken cancellationToken)
    {
        var estimatedTokens = inputs.Sum(text => text.Length / 3 + 2);
        var estimatedCost = Math.Max(1, (long)Math.Ceiling(estimatedTokens * embeddingOptions.PriceUsdPerMillionTokens));
        var reservation = await governor.ReserveAsync(ownerUserId, AiOperationRegistry.IngredientEmbed, 1, estimatedCost, estimatedTokens, correlationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved || reservation.ReservationId is not { } reservationId)
        {
            return (null, "budget");
        }

        var result = await provider!.EmbedAsync(new EmbeddingRequest(inputs), cancellationToken);
        // Price per million tokens times tokens, in micro-dollars, is tokens × price.
        var cost = result.CostUsd is { } reported
            ? (long)Math.Ceiling(reported * 1_000_000m)
            : (long)Math.Ceiling((result.PromptTokens ?? 0) * embeddingOptions.PriceUsdPerMillionTokens);
        var usage = new AiCallUsage(provider.Name, result.Model, result.PromptTokens, 0, 1, result.LatencyMs, result.FailureCode);
        if (!result.IsSuccess || result.Vectors!.Count != inputs.Count || result.Vectors.Any(vector => vector.Length != provider.Dimensions))
        {
            await governor.ReleaseAtCostAsync(reservationId, usage, cost, cancellationToken);
            return (null, result.FailureCode ?? "invalid_vectors");
        }

        await governor.SettleAtCostAsync(reservationId, 1, usage, cost, cancellationToken);
        return (result.Vectors, "ok");
    }

    private async Task DeferAllAsync(IReadOnlyList<PendingIngredientName> names, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var attempts = name.Attempts + 1;
            if (attempts >= options.MaxAttempts && reason != "budget")
            {
                await store.MarkUnresolvedAsync(name.NormalizedText, null, Evaluation, reason, now, cancellationToken);
            }
            else
            {
                // A budget deferral is not the name's fault, so it does not use up an attempt.
                await store.DeferAsync(name.NormalizedText, reason == "budget" ? name.Attempts : attempts, now.AddMinutes(reason == "budget" ? 60 : 10 * attempts), reason, now, cancellationToken);
            }
        }
    }
}
