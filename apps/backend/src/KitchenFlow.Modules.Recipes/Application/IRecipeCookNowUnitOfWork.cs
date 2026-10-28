using KitchenFlow.Modules.Recipes.Domain;

namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>
/// Atomic finalization port for cook-now suggest completion and expand persistence.
/// Implementations commit session status, recipe/revision rows, and usage settle/release in a single
/// EF transaction / <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// There is an unavoidable crash window between a successful provider return and this database commit.
/// Concurrent duplicate provider invocations are prevented by the generation-session claim
/// (<see cref="IRecipeGenerationStore.TryClaimNewSessionAsync"/> /
/// <see cref="IRecipeGenerationStore.TryClaimSelectionAsync"/>), not by this finalization step.
/// </remarks>
public interface IRecipeCookNowUnitOfWork
{
    /// <summary>
    /// Attaches validated candidates, settles the usage reservation (KitchenFlow units plus optional
    /// provider token metadata), and transitions the session to CandidatesReady in one transaction.
    /// </summary>
    Task CompleteSuggestAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the suggest session failed and releases the usage reservation in one transaction.
    /// </summary>
    Task FailSuggestAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the recipe and first revision, marks the session Selected, and settles usage in one transaction.
    /// </summary>
    Task FinalizeExpansionAsync(
        Recipe recipe,
        RecipeRevision revision,
        RecipeGenerationSession session,
        Guid reservationId,
        int settledUnits,
        string provider,
        string model,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the Expanding session failed and releases the usage reservation in one transaction.
    /// </summary>
    Task FailExpansionAsync(
        RecipeGenerationSession session,
        Guid reservationId,
        string failureReason,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
