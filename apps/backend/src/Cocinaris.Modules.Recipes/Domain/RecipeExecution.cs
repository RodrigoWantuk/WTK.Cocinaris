namespace Cocinaris.Modules.Recipes.Domain;

/// <summary>Lifecycle of one recipe execution.</summary>
public enum RecipeExecutionStatus
{
    /// <summary>The user started cooking and has not finished or abandoned it.</summary>
    InProgress,
    /// <summary>The user finished; inventory reconciliation committed in the same transaction.</summary>
    Completed,
    /// <summary>The user stopped without recording consumption.</summary>
    Abandoned
}

/// <summary>
/// One explicit, owner-scoped cooking of a recipe revision (PLAN-0042). Viewing a recipe never
/// starts an execution. The execution pins the revision it started from, so a later revision does
/// not change what the user is cooking. Completion records the inventory batch that reconciled it;
/// the application layer commits both in one database transaction.
/// </summary>
public sealed class RecipeExecution
{
    /// <summary>Maximum notes length, in characters.</summary>
    public const int MaxNotesLength = 1000;

    private RecipeExecution()
    {
    }

    /// <summary>Gets the execution identifier.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the owning user identifier.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the recipe being cooked.</summary>
    public Guid RecipeId { get; private init; }

    /// <summary>Gets the pinned revision number the execution started from.</summary>
    public int RevisionNumber { get; private init; }

    /// <summary>Gets the portions the user is cooking, 1 to 24.</summary>
    public int Servings { get; private init; }

    /// <summary>Gets the lifecycle status.</summary>
    public RecipeExecutionStatus Status { get; private set; }

    /// <summary>Gets the UTC start instant.</summary>
    public DateTimeOffset StartedAt { get; private init; }

    /// <summary>Gets the UTC instant of the last state change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the UTC completion or abandonment instant.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Gets the inventory batch that reconciled the completion, when anything was consumed or stored.</summary>
    public Guid? InventoryBatchId { get; private set; }

    /// <summary>Gets the client idempotency key of the completion, used to replay a retried request.</summary>
    public Guid? CompletionKey { get; private set; }

    /// <summary>Gets the optional 1 to 5 rating.</summary>
    public int? Rating { get; private set; }

    /// <summary>Gets the optional private notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Gets the optimistic-concurrency token, replaced on every state change.</summary>
    public Guid Version { get; private set; }

    /// <summary>
    /// Gets the menu entry this cooking session was started from (PLAN-0029), if any. The planning
    /// module reads it to show the entry as cooking or cooked; it never affects reconciliation.
    /// </summary>
    public Guid? MenuEntryId { get; private init; }

    /// <summary>Gets the cook's ingredient changes in this session (PLAN-0042 Phase 2), at most one per ingredient.</summary>
    public IReadOnlyList<RecipeExecutionAdjustment> Adjustments { get; private set; } = [];

    /// <summary>
    /// Replaces the session's ingredient adjustments. Only an in-progress session can change, and
    /// the list must be valid: well-formed entries, at most <see cref="RecipeExecutionAdjustment.MaxCount"/>,
    /// one per ingredient.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the execution is not in progress.</exception>
    /// <exception cref="ArgumentException">Thrown when the list is invalid.</exception>
    public void SetAdjustments(IReadOnlyList<RecipeExecutionAdjustment> adjustments, DateTimeOffset now)
    {
        EnsureInProgress();
        if (adjustments.Count > RecipeExecutionAdjustment.MaxCount || adjustments.Select(item => item.IngredientId).Distinct(StringComparer.Ordinal).Count() != adjustments.Count)
        {
            throw new ArgumentException($"At most {RecipeExecutionAdjustment.MaxCount} adjustments, one per ingredient.");
        }

        foreach (var adjustment in adjustments)
        {
            if (!adjustment.IsValid(out var error))
            {
                throw new ArgumentException(error);
            }
        }

        Adjustments = adjustments.Select(item => item with { SubstituteName = item.SubstituteName?.Trim() }).ToList();
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }

    /// <summary>Starts cooking a recipe revision.</summary>
    /// <exception cref="ArgumentException">Thrown when identity or servings are invalid.</exception>
    public static RecipeExecution Start(Guid ownerUserId, Guid recipeId, int revisionNumber, int servings, DateTimeOffset now, Guid? menuEntryId = null)
    {
        if (ownerUserId == Guid.Empty || recipeId == Guid.Empty || revisionNumber < 1 || servings is < 1 or > 24)
        {
            throw new ArgumentException("Recipe execution identity, revision, or servings are invalid.");
        }

        return new RecipeExecution
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            RecipeId = recipeId,
            RevisionNumber = revisionNumber,
            Servings = servings,
            Status = RecipeExecutionStatus.InProgress,
            StartedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid(),
            MenuEntryId = menuEntryId
        };
    }

    /// <summary>Restores a persisted execution without exposing persistence types to the domain.</summary>
    public static RecipeExecution Restore(Guid id, Guid ownerUserId, Guid recipeId, int revisionNumber, int servings, RecipeExecutionStatus status, DateTimeOffset startedAt, DateTimeOffset updatedAt, DateTimeOffset? finishedAt, Guid? inventoryBatchId, Guid? completionKey, int? rating, string? notes, Guid version, Guid? menuEntryId = null, IReadOnlyList<RecipeExecutionAdjustment>? adjustments = null) => new()
    {
        Id = id,
        OwnerUserId = ownerUserId,
        RecipeId = recipeId,
        RevisionNumber = revisionNumber,
        Servings = servings,
        Status = status,
        StartedAt = startedAt,
        UpdatedAt = updatedAt,
        FinishedAt = finishedAt,
        InventoryBatchId = inventoryBatchId,
        CompletionKey = completionKey,
        Rating = rating,
        Notes = notes,
        Version = version,
        MenuEntryId = menuEntryId,
        Adjustments = adjustments ?? []
    };

    /// <summary>Records completion. Only an in-progress execution can complete.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the execution is not in progress.</exception>
    /// <exception cref="ArgumentException">Thrown when the rating or notes are out of bounds.</exception>
    public void Complete(Guid completionKey, Guid? inventoryBatchId, int? rating, string? notes, DateTimeOffset now)
    {
        EnsureInProgress();
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (rating is < 1 or > 5 || normalizedNotes?.Length > MaxNotesLength || completionKey == Guid.Empty)
        {
            throw new ArgumentException("Rating must be 1 to 5 and notes at most 1000 characters.");
        }

        Status = RecipeExecutionStatus.Completed;
        CompletionKey = completionKey;
        InventoryBatchId = inventoryBatchId;
        Rating = rating;
        Notes = normalizedNotes;
        Finish(now);
    }

    /// <summary>Abandons the execution without touching inventory.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the execution is not in progress.</exception>
    public void Abandon(DateTimeOffset now)
    {
        EnsureInProgress();
        Status = RecipeExecutionStatus.Abandoned;
        Finish(now);
    }

    private void EnsureInProgress()
    {
        if (Status != RecipeExecutionStatus.InProgress)
        {
            throw new InvalidOperationException("Only an in-progress recipe execution can change state.");
        }
    }

    private void Finish(DateTimeOffset now)
    {
        FinishedAt = now;
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
