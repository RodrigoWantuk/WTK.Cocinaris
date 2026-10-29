namespace Cocinaris.Modules.Ingredients;

/// <summary>How a name was linked to a catalog ingredient.</summary>
public enum IngredientResolutionMethod
{
    /// <summary>The normalized name is a curated catalog name.</summary>
    Catalog,

    /// <summary>Trigram similarity to one curated name was high and unambiguous.</summary>
    Trigram,

    /// <summary>The embedding of the exact text was close to one catalog ingredient and clearly closer than the next.</summary>
    Embedding
}

/// <summary>A name waiting for resolution, claimed by the job.</summary>
/// <param name="NormalizedText">Normalized name; the queue key.</param>
/// <param name="ExactText">The exact text first seen for this normalized name (at most <see cref="IngredientText.MaxExactLength"/> characters).</param>
/// <param name="RequestedByUserId">Owner charged for provider calls; null after a catalog change re-queued the name.</param>
/// <param name="Attempts">Provider attempts already made.</param>
public sealed record PendingIngredientName(string NormalizedText, string ExactText, Guid? RequestedByUserId, int Attempts);

/// <summary>One catalog ingredient and how close a name is to it.</summary>
/// <param name="IngredientId">Catalog ingredient.</param>
/// <param name="Score">Similarity: trigram similarity or cosine similarity, from 0 to 1.</param>
public sealed record IngredientCandidate(int IngredientId, double Score);

/// <summary>
/// Persistence port of the ingredient identity module (PLAN-0055). Implementations own the
/// <c>ingredients</c> schema. Nothing in it is private to one owner except the transient
/// <see cref="PendingIngredientName.RequestedByUserId"/>, which is cleared when a name is decided.
/// </summary>
public interface IIngredientStore
{
    /// <summary>Returns the learned or catalog alias of each normalized name that has one, as catalog slugs.</summary>
    Task<IReadOnlyDictionary<string, string>> FindAliasSlugsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);

    /// <summary>Queues names for resolution; a name already queued or decided is left as it is.</summary>
    /// <param name="names">Normalized name and the exact text seen.</param>
    /// <param name="ownerUserId">Owner charged for the provider calls these names need.</param>
    /// <param name="now">UTC instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueAsync(IReadOnlyCollection<(string NormalizedText, string ExactText)> names, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the database copy of the catalog to <paramref name="catalog"/> and records
    /// <paramref name="evaluation"/>. When the evaluation changed, names left unresolved under an
    /// older one are queued again. Idempotent and cheap when nothing changed.
    /// </summary>
    Task SyncCatalogAsync(IngredientCatalog catalog, string evaluation, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Claims up to <paramref name="max"/> due names with a lease, skipping rows another worker holds.</summary>
    Task<IReadOnlyList<PendingIngredientName>> ClaimAsync(int max, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);

    /// <summary>Records the alias of a name, removes it from the queue, and forgets its exact text and owner.</summary>
    Task ResolveAsync(string normalizedText, int ingredientId, IngredientResolutionMethod method, double score, string evaluation, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Marks a name unresolved under <paramref name="evaluation"/> and clears its owner; the best candidate is kept for tuning.</summary>
    Task MarkUnresolvedAsync(string normalizedText, IngredientCandidate? best, string evaluation, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Returns a claimed name to the queue for a later attempt.</summary>
    Task DeferAsync(string normalizedText, int attempts, DateTimeOffset nextAttemptAt, string reason, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The two curated catalog names most similar to <paramref name="normalizedText"/> by trigrams, best first.</summary>
    Task<IReadOnlyList<IngredientCandidate>> TrigramCandidatesAsync(string normalizedText, CancellationToken cancellationToken);

    /// <summary>Returns cached vectors by cache key.</summary>
    Task<IReadOnlyDictionary<string, float[]>> FindEmbeddingsAsync(IReadOnlyCollection<string> cacheKeys, CancellationToken cancellationToken);

    /// <summary>Stores vectors by cache key; an existing key keeps its first vector.</summary>
    Task SaveEmbeddingsAsync(IReadOnlyDictionary<string, float[]> vectors, string space, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Returns the catalog ingredients whose stored vector in <paramref name="space"/> is missing or was built from another document key.</summary>
    Task<IReadOnlyList<int>> CatalogVectorsToRefreshAsync(string space, IReadOnlyDictionary<int, string> documentKeys, CancellationToken cancellationToken);

    /// <summary>Stores catalog ingredient vectors for <paramref name="space"/>.</summary>
    Task SaveCatalogVectorsAsync(string space, IReadOnlyDictionary<int, (string DocumentKey, float[] Vector)> vectors, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The two catalog ingredients nearest to <paramref name="vector"/> by cosine similarity, best first.</summary>
    Task<IReadOnlyList<IngredientCandidate>> NearestAsync(string space, float[] vector, CancellationToken cancellationToken);
}

/// <summary>
/// Resolution policy under <c>Ingredients:Resolution</c> (PLAN-0055). Thresholds are part of the
/// evaluation string, so tuning them re-checks unresolved names but never rewrites decided ones.
/// </summary>
public sealed class IngredientResolutionOptions
{
    /// <summary>Gets or sets the most names one job step claims; also the embedding batch size.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>Gets or sets how often the job looks for queued names, in seconds.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>Gets or sets the claim lease in seconds.</summary>
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Gets or sets the most provider attempts per name before it is left unresolved.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Gets or sets the lowest trigram similarity that links a name to a curated name.</summary>
    public double TrigramThreshold { get; set; } = 0.75;

    /// <summary>Gets or sets how much better the best trigram candidate must be than a different second one.</summary>
    public double TrigramMargin { get; set; } = 0.1;

    /// <summary>Gets or sets the lowest cosine similarity that links a name to a catalog ingredient.</summary>
    public double EmbeddingThreshold { get; set; } = 0.80;

    /// <summary>Gets or sets how much closer the best catalog ingredient must be than the second.</summary>
    public double EmbeddingMargin { get; set; } = 0.03;

    /// <summary>Gets or sets the Qwen3 Embedding query instruction; documents (catalog entries) carry none.</summary>
    public string QueryInstruction { get; set; } = "Given a food or ingredient name typed by a home cook, retrieve the canonical cooking ingredient it names";

    /// <summary>Gets or sets the instruction version; part of every query cache key.</summary>
    public string InstructionVersion { get; set; } = "q1";

    /// <summary>Validates that the policy is coherent.</summary>
    public bool IsValid() => BatchSize is > 0 and <= 64 && PollSeconds > 0 && LeaseSeconds > 0 && MaxAttempts > 0
        && TrigramThreshold is > 0 and <= 1 && TrigramMargin is >= 0 and < 1 && EmbeddingThreshold is > 0 and <= 1 && EmbeddingMargin is >= 0 and < 1
        && !string.IsNullOrWhiteSpace(QueryInstruction) && !string.IsNullOrWhiteSpace(InstructionVersion);
}
