namespace Cocinaris.Infrastructure.Persistence;

/// <summary>Database copy of one curated catalog ingredient (PLAN-0055); the embedded catalog is the source.</summary>
public sealed class IngredientCatalogItemRecord
{
    /// <summary>Gets or sets the permanent catalog id.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the stable slug.</summary>
    public required string Slug { get; set; }
    /// <summary>Gets or sets the kitchen category.</summary>
    public required string Category { get; set; }
    /// <summary>Gets or sets the names by language as JSON.</summary>
    public required string NamesJson { get; set; }
    /// <summary>Gets or sets the UTC instant of the last sync that changed the row.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A normalized name linked to a catalog ingredient. Global and non-private: it records that a
/// spelling means an ingredient, not who typed it.
/// </summary>
public sealed class IngredientAliasRecord
{
    /// <summary>Gets or sets the normalized name; the key.</summary>
    public required string NormalizedText { get; set; }
    /// <summary>Gets or sets the catalog ingredient.</summary>
    public int IngredientId { get; set; }
    /// <summary>Gets or sets <c>Catalog</c>, <c>Trigram</c>, or <c>Embedding</c>.</summary>
    public required string Method { get; set; }
    /// <summary>Gets or sets the similarity that decided it (1 for curated names).</summary>
    public double Score { get; set; }
    /// <summary>Gets or sets the evaluation (catalog version, vector space, thresholds) that decided it.</summary>
    public required string Evaluation { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A name waiting for resolution or left unresolved. The owner is kept only while the name is
/// queued; the exact text is kept while unresolved so a later catalog can re-check it.
/// </summary>
public sealed class IngredientResolutionRecord
{
    /// <summary>Gets or sets the normalized name; the key.</summary>
    public required string NormalizedText { get; set; }
    /// <summary>Gets or sets the exact text first seen.</summary>
    public required string ExactText { get; set; }
    /// <summary>Gets or sets the owner charged for provider calls while queued.</summary>
    public Guid? RequestedByUserId { get; set; }
    /// <summary>Gets or sets <c>Pending</c>, <c>Working</c>, or <c>Unresolved</c>.</summary>
    public required string State { get; set; }
    /// <summary>Gets or sets the provider attempts made.</summary>
    public int Attempts { get; set; }
    /// <summary>Gets or sets the earliest next try while pending, or the lease expiry while working.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }
    /// <summary>Gets or sets the evaluation an unresolved decision was made under.</summary>
    public string? Evaluation { get; set; }
    /// <summary>Gets or sets the closest catalog ingredient when unresolved, for tuning.</summary>
    public int? BestIngredientId { get; set; }
    /// <summary>Gets or sets the closest similarity when unresolved.</summary>
    public double? BestScore { get; set; }
    /// <summary>Gets or sets the bounded reason of the last deferral or unresolved decision.</summary>
    public string? Reason { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Single-row sync state of the catalog copy.</summary>
public sealed class IngredientCatalogStateRecord
{
    /// <summary>Gets or sets the row id; always 1.</summary>
    public int Id { get; set; }
    /// <summary>Gets or sets the catalog version last synced.</summary>
    public required string CatalogVersion { get; set; }
    /// <summary>Gets or sets the evaluation last recorded.</summary>
    public required string Evaluation { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
