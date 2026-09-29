using KitchenFlow.Modules.Recipes.Domain;

namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>Outcome of attempting to claim a new generation session for a request-candidates idempotency key.</summary>
/// <param name="WasCreated">
/// <see langword="true"/> when this caller inserted the session and owns suggest execution;
/// <see langword="false"/> when an existing session was returned for the same owner and key.
/// </param>
/// <param name="Session">The claimed or previously existing session.</param>
public sealed record RecipeGenerationSessionClaimResult(bool WasCreated, RecipeGenerationSession Session);

/// <summary>Outcome classification for an atomic select-and-expand claim.</summary>
public enum RecipeSelectionClaimOutcome
{
    /// <summary>This caller won CandidatesReady → Expanding and owns expand execution.</summary>
    Claimed,
    /// <summary>The session is already Selected for the same select key and candidate; replay the recipe.</summary>
    AlreadySelected,
    /// <summary>The same select key and candidate are Expanding; do not re-invoke the provider.</summary>
    InProgress,
    /// <summary>Another selection, select key, or candidate conflicts with this request.</summary>
    Conflict,
    /// <summary>The session was not found for the owner.</summary>
    NotFound,
    /// <summary>The session exists but is not ready for selection (wrong status or expired).</summary>
    NotReady
}

/// <summary>Typed result of <see cref="IRecipeGenerationStore.TryClaimSelectionAsync"/>.</summary>
/// <param name="Outcome">Claim classification.</param>
/// <param name="Session">Session snapshot when found; null only for <see cref="RecipeSelectionClaimOutcome.NotFound"/>.</param>
public sealed record RecipeSelectionClaimResult(RecipeSelectionClaimOutcome Outcome, RecipeGenerationSession? Session);

/// <summary>
/// Owner-scoped persistence port for <see cref="RecipeGenerationSession"/> aggregates. The store owns
/// idempotency-key claim/lookup so that a repeated <c>request candidates</c> or <c>select candidate</c>
/// command safely replays or rejects without re-invoking the AI Gateway or re-reserving usage.
/// </summary>
public interface IRecipeGenerationStore
{
    /// <summary>
    /// Inserts <paramref name="session"/> in <see cref="RecipeGenerationSessionStatus.AwaitingCandidates"/>.
    /// On unique <c>(OwnerUserId, IdempotencyKey)</c> conflict, reloads and returns the existing session
    /// without throwing; only the first insert winner owns suggest execution.
    /// </summary>
    Task<RecipeGenerationSessionClaimResult> TryClaimNewSessionAsync(RecipeGenerationSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically claims CandidatesReady → Expanding for one owner session with the selected candidate
    /// and select idempotency key. Only one concurrent claim wins; same-key conflicts and in-progress
    /// replays are classified without throwing.
    /// </summary>
    Task<RecipeSelectionClaimResult> TryClaimSelectionAsync(
        Guid ownerUserId,
        Guid sessionId,
        string candidateId,
        Guid selectIdempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Finds a previously created session for this owner and client request-candidates idempotency key, if any.</summary>
    Task<RecipeGenerationSession?> FindByIdempotencyKeyAsync(Guid ownerUserId, Guid idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Finds a previously claimed or completed selection for this owner and select-and-expand idempotency key, if any.</summary>
    Task<RecipeGenerationSession?> FindBySelectIdempotencyKeyAsync(Guid ownerUserId, Guid selectIdempotencyKey, CancellationToken cancellationToken);

    /// <summary>Finds one owner-scoped session by identifier without cross-user disclosure.</summary>
    Task<RecipeGenerationSession?> FindByIdAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Finds one owner-scoped session by identifier without cross-user disclosure.</summary>
    Task<RecipeGenerationSession?> FindActiveAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Persists a state transition on an already-created session.</summary>
    Task SaveAsync(RecipeGenerationSession session, CancellationToken cancellationToken);
}
