namespace Cocinaris.Infrastructure.Persistence;

/// <summary>Persistence representation of one user-owned recipe identity.</summary>
public sealed class RecipeRecord
{
    /// <summary>Gets or sets the recipe identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the revision number of the current authoritative content.</summary>
    public int CurrentRevisionNumber { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last new revision.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets how the recipe was created: <c>Generated</c>, <c>Manual</c>, or <c>Derived</c>.</summary>
    public string Origin { get; set; } = "Generated";
    /// <summary>Gets or sets whether the owner marked the recipe as a favorite.</summary>
    public bool IsFavorite { get; set; }
    /// <summary>Gets or sets the UTC archive instant; null while the recipe is in the library.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
    /// <summary>Gets or sets the parent recipe of a derived recipe (same owner).</summary>
    public Guid? ParentRecipeId { get; set; }
    /// <summary>Gets or sets the parent revision a derived recipe started from.</summary>
    public int? ParentRevisionNumber { get; set; }
    /// <summary>Gets or sets the owner's reason for deriving the recipe.</summary>
    public string? DerivationReason { get; set; }
    /// <summary>Gets or sets the idempotency key of the request that created a manual or derived recipe.</summary>
    public Guid? CreationKey { get; set; }
}

/// <summary>
/// Immutable, append-only persistence representation of one recipe revision. Rows are never
/// updated or deleted after insertion; an append-only database trigger enforces this invariant.
/// </summary>
public sealed class RecipeRevisionRecord
{
    /// <summary>Gets or sets the revision identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owning recipe identifier.</summary>
    public Guid RecipeId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the strictly sequential revision number.</summary>
    public int RevisionNumber { get; set; }
    /// <summary>Gets or sets the recipe display name at this revision.</summary>
    public required string Name { get; set; }
    /// <summary>Gets or sets the applicable meal types at this revision, serialized as a JSON string array.</summary>
    public required string MealTypesJson { get; set; }
    /// <summary>Gets or sets the servings count at this revision.</summary>
    public int Servings { get; set; }
    /// <summary>Gets or sets the deterministically normalized full recipe content as validated protocol 0.3 JSON.</summary>
    public required string NormalizedRecipeJson { get; set; }
    /// <summary>Gets or sets the deterministically normalized indexable thumbnail visual descriptor JSON.</summary>
    public required string ThumbnailVisualJson { get; set; }
    /// <summary>Gets or sets the stable candidate identifier a generated revision was expanded from.</summary>
    public string? SourceCandidateId { get; set; }
    /// <summary>Gets or sets the generation session that produced a generated revision.</summary>
    public Guid? SourceGenerationSessionId { get; set; }
    /// <summary>Gets or sets how the content was produced: <c>Generated</c>, <c>Manual</c>, <c>Edited</c>, or <c>Derived</c>.</summary>
    public string Origin { get; set; } = "Generated";
    /// <summary>Gets or sets the owner's reason for an edited revision.</summary>
    public string? ChangeReason { get; set; }
    /// <summary>Gets or sets the completed execution whose adjustments the revision saves.</summary>
    public Guid? SourceExecutionId { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Persistence representation of one owner-scoped cook-now recipe generation session.</summary>
public sealed class RecipeGenerationSessionRecord
{
    /// <summary>Gets or sets the session identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the client-supplied idempotency key that created this session.</summary>
    public Guid IdempotencyKey { get; set; }
    /// <summary>Gets or sets the execution mode. PLAN-0028 supports only <c>cook_now</c>.</summary>
    public required string ExecutionMode { get; set; }
    /// <summary>Gets or sets the controlled lifecycle status.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the validated candidate snapshot JSON, present once candidates are ready.</summary>
    public string? CandidatesSnapshotJson { get; set; }
    /// <summary>Gets or sets the stable identifier of the selected candidate, present once selected.</summary>
    public string? SelectedCandidateId { get; set; }
    /// <summary>Gets or sets the identifier of the recipe produced from the selected candidate.</summary>
    public Guid? SelectedRecipeId { get; set; }
    /// <summary>Gets or sets the client-supplied select-and-expand idempotency key, when selection completed.</summary>
    public Guid? SelectIdempotencyKey { get; set; }
    /// <summary>Gets or sets a non-sensitive failure classification.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last state transition.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant after which the session may no longer be acted upon.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Persistence representation of one owner-scoped recipe execution (PLAN-0042).</summary>
public sealed class RecipeExecutionRecord
{
    /// <summary>Gets or sets the execution identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the recipe being cooked.</summary>
    public Guid RecipeId { get; set; }
    /// <summary>Gets or sets the pinned revision number.</summary>
    public int RevisionNumber { get; set; }
    /// <summary>Gets or sets the portions cooked.</summary>
    public int Servings { get; set; }
    /// <summary>Gets or sets the controlled lifecycle status.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the UTC start instant.</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last state change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets the UTC completion or abandonment instant.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
    /// <summary>Gets or sets the inventory batch that reconciled the completion.</summary>
    public Guid? InventoryBatchId { get; set; }
    /// <summary>Gets or sets the completion idempotency key.</summary>
    public Guid? CompletionKey { get; set; }
    /// <summary>Gets or sets the optional rating.</summary>
    public int? Rating { get; set; }
    /// <summary>Gets or sets the optional private notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Gets or sets the optimistic-concurrency token.</summary>
    public Guid Version { get; set; }
    /// <summary>Gets or sets the menu entry the session was started from; cleared if the entry is removed.</summary>
    public Guid? MenuEntryId { get; set; }
}
