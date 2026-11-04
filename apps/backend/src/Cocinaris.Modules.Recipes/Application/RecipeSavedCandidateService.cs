using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>One candidate the owner kept for later (PLAN-0056 Phase 3).</summary>
/// <param name="SavedId">Saved entry id.</param>
/// <param name="SessionId">Generation session the candidate came from.</param>
/// <param name="CandidateJson">The validated candidate snapshot as JSON.</param>
/// <param name="CreatedAt">When it was saved.</param>
public sealed record SavedCandidateRecord(Guid SavedId, Guid SessionId, string CandidateJson, DateTimeOffset CreatedAt);

/// <summary>API-facing saved candidate.</summary>
/// <param name="SavedId">Saved entry id.</param>
/// <param name="SessionId">Generation session it came from.</param>
/// <param name="Candidate">The candidate as shown when it was suggested.</param>
/// <param name="CreatedAt">When it was saved.</param>
public sealed record SavedCandidateView(Guid SavedId, Guid SessionId, RecipeCandidateView Candidate, DateTimeOffset CreatedAt);

/// <summary>One candidate of a past suggestion request (PLAN-0060).</summary>
/// <param name="Candidate">The candidate as it was suggested.</param>
/// <param name="RecipeId">The recipe it became, if the person already asked for it.</param>
public sealed record SuggestionHistoryCandidateView(RecipeCandidateView Candidate, Guid? RecipeId);

/// <summary>One past suggestion request (PLAN-0060).</summary>
/// <param name="SessionId">Generation session.</param>
/// <param name="CreatedAt">When it was requested.</param>
/// <param name="Hint">The person's hint, if any.</param>
/// <param name="Adaptation">The ingredient approach asked, if any.</param>
/// <param name="Candidates">The candidates suggested.</param>
public sealed record SuggestionHistoryEntryView(Guid SessionId, DateTimeOffset CreatedAt, string? Hint, string? Adaptation, IReadOnlyList<SuggestionHistoryCandidateView> Candidates);

/// <summary>Owner-scoped persistence of saved candidates.</summary>
public interface ISavedCandidateStore
{
    /// <summary>Saves a candidate once per owner, session, and candidate id; returns the entry (existing or new).</summary>
    Task<SavedCandidateRecord> AddAsync(Guid ownerUserId, Guid sessionId, string candidateId, string candidateJson, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Lists the owner's saved candidates, newest first.</summary>
    Task<IReadOnlyList<SavedCandidateRecord>> ListAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Finds one of the owner's saved candidates.</summary>
    Task<SavedCandidateRecord?> FindAsync(Guid ownerUserId, Guid savedId, CancellationToken cancellationToken);

    /// <summary>Removes one of the owner's saved candidates; true when it existed.</summary>
    Task<bool> DeleteAsync(Guid ownerUserId, Guid savedId, CancellationToken cancellationToken);

    /// <summary>Marks a saved candidate cooked into <paramref name="recipeId"/>; it leaves the list but still answers replays.</summary>
    Task MarkCookedAsync(Guid ownerUserId, Guid savedId, Guid recipeId, CancellationToken cancellationToken);
}

/// <summary>
/// Save candidates for later and cook them on demand (PLAN-0056 Phase 3, owner report 5: "salvar
/// para mais tarde, não ser obrigado a selecionar"). Saving keeps the already validated candidate,
/// so it costs nothing.
/// </summary>
/// <remarks>
/// Cooking a saved candidate opens a new generation session holding just that candidate, keyed by
/// the request's Idempotency-Key, and then selects it through
/// <see cref="RecipeCookNowApplicationService.SelectCandidateAsync"/>. Selection therefore keeps its
/// claims, replay, usage ledger, idea-library shortcut, and capture. Before that, inventory uses of
/// lots that are no longer in the pantry become ingredients to buy, so the expansion never
/// references a lot that is gone.
/// </remarks>
public sealed class RecipeSavedCandidateService(
    ICurrentUserAccessor currentUser,
    ISavedCandidateStore store,
    IRecipeGenerationStore generationStore,
    IInventoryLotReadStore inventory,
    RecipeCookNowApplicationService cookNow,
    TimeProvider timeProvider)
{
    /// <summary>Most saved candidates listed.</summary>
    public const int MaxListed = 50;

    /// <summary>Most past requests listed in the suggestion history.</summary>
    public const int MaxHistory = 40;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Keeps one candidate of an owned session; saving it again returns the same entry.</summary>
    public async Task<RecipeApplicationResult<SavedCandidateView>> SaveAsync(Guid sessionId, string candidateId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await generationStore.FindByIdAsync(user.Id, sessionId, cancellationToken);
        var candidate = session?.CandidatesSnapshotJson is { } json
            ? JsonSerializer.Deserialize<SuggestCandidatesResponse>(json, SerializerOptions)?.Candidates.FirstOrDefault(item => item.CandidateId == candidateId)
            : null;
        if (candidate is null)
        {
            return RecipeApplicationResult<SavedCandidateView>.Failure("resource_not_found", "The candidate was not found.");
        }

        var saved = await store.AddAsync(user.Id, sessionId, candidateId, JsonSerializer.Serialize(candidate, SerializerOptions), timeProvider.GetUtcNow(), cancellationToken);
        return RecipeApplicationResult<SavedCandidateView>.Succeeded(ToView(saved), RecipeApplicationSuccess.Created);
    }

    /// <summary>Lists the owner's saved candidates, newest first.</summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<SavedCandidateView>>> ListAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var saved = await store.ListAsync(user.Id, MaxListed, cancellationToken);
        return RecipeApplicationResult<IReadOnlyList<SavedCandidateView>>.Succeeded(saved.Select(ToView).ToList());
    }

    /// <summary>Removes a saved candidate.</summary>
    public async Task<RecipeApplicationResult<bool>> DeleteAsync(Guid savedId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        return await store.DeleteAsync(user.Id, savedId, cancellationToken)
            ? RecipeApplicationResult<bool>.Succeeded(true)
            : RecipeApplicationResult<bool>.Failure("resource_not_found", "The saved candidate was not found.");
    }

    /// <summary>
    /// Turns a saved candidate into a recipe. A repeated request with the same key returns the same
    /// recipe. On success the entry is marked cooked and leaves the saved list, since the recipe is
    /// now in the library.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeDetailView>> CookAsync(Guid savedId, Guid? idempotencyKey, string correlationId, string? requestLanguage, CancellationToken cancellationToken)
    {
        if (idempotencyKey is not { } key)
        {
            return RecipeApplicationResult<RecipeDetailView>.Failure("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var saved = await store.FindAsync(user.Id, savedId, cancellationToken);
        var candidate = saved is null ? null : JsonSerializer.Deserialize<SuggestCandidate>(saved.CandidateJson, SerializerOptions);
        if (saved is null || candidate is null)
        {
            return RecipeApplicationResult<RecipeDetailView>.Failure("resource_not_found", "The saved candidate was not found.");
        }

        var source = await generationStore.FindByIdAsync(user.Id, saved.SessionId, cancellationToken);
        var result = await OpenCandidateAsync(user.Id, source, candidate, key, correlationId, requestLanguage, cancellationToken);
        // Kept, not deleted, so a retry with the same key still finds it and replays the same recipe.
        if (result.Problem is null && result.Value is { } recipe)
        {
            await store.MarkCookedAsync(user.Id, savedId, recipe.RecipeId, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Lists the owner's past suggestion requests, newest first, with the recipe each candidate
    /// already became. Failed requests have no candidates and do not appear, and neither do sessions
    /// that only reopened a candidate.
    /// </summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<SuggestionHistoryEntryView>>> ListHistoryAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var sessions = await generationStore.ListHistoryAsync(user.Id, MaxHistory, cancellationToken);
        var reopened = await generationStore.ListReopenedAsync(user.Id, sessions.Select(session => session.Id).ToList(), cancellationToken);
        var recipes = sessions.Concat(reopened)
            .Where(session => session.SelectedRecipeId is not null && session.SelectedCandidateId is not null)
            .GroupBy(session => (Source: session.SourceSessionId ?? session.Id, Candidate: session.SelectedCandidateId!))
            .ToDictionary(group => group.Key, group => group.First().SelectedRecipeId);
        var entries = new List<SuggestionHistoryEntryView>();
        foreach (var session in sessions)
        {
            var candidates = JsonSerializer.Deserialize<SuggestCandidatesResponse>(session.CandidatesSnapshotJson!, SerializerOptions)?.Candidates ?? [];
            // Before PLAN-0060, a cooked saved candidate opened a one-candidate session without a source.
            if (candidates.Count < 2)
            {
                continue;
            }

            entries.Add(new SuggestionHistoryEntryView(
                session.Id,
                session.CreatedAt,
                session.UserHint,
                session.Adaptation,
                candidates.Select(candidate => new SuggestionHistoryCandidateView(
                    RecipeCookNowApplicationService.ToCandidateView(candidate),
                    recipes.GetValueOrDefault((session.Id, candidate.CandidateId)))).ToList()));
        }

        return RecipeApplicationResult<IReadOnlyList<SuggestionHistoryEntryView>>.Succeeded(entries);
    }

    /// <summary>
    /// Turns a candidate of a past request into a recipe, against today's pantry. Works however old
    /// the request is, because it opens a new session; a repeated request with the same key returns
    /// the same recipe.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeDetailView>> ReopenAsync(Guid sessionId, string candidateId, Guid? idempotencyKey, string correlationId, string? requestLanguage, CancellationToken cancellationToken)
    {
        if (idempotencyKey is not { } key)
        {
            return RecipeApplicationResult<RecipeDetailView>.Failure("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var source = await generationStore.FindByIdAsync(user.Id, sessionId, cancellationToken);
        var candidate = source?.CandidatesSnapshotJson is { } json
            ? JsonSerializer.Deserialize<SuggestCandidatesResponse>(json, SerializerOptions)?.Candidates.FirstOrDefault(item => item.CandidateId == candidateId)
            : null;
        return candidate is null
            ? RecipeApplicationResult<RecipeDetailView>.Failure("resource_not_found", "The candidate was not found.")
            : await OpenCandidateAsync(user.Id, source, candidate, key, correlationId, requestLanguage, cancellationToken);
    }

    /// <summary>Removes a past request from the owner's history and forgets its hint.</summary>
    public async Task<RecipeApplicationResult<bool>> RemoveFromHistoryAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await generationStore.FindByIdAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return RecipeApplicationResult<bool>.Failure("resource_not_found", "The suggestion was not found.");
        }

        session.RemoveFromHistory(timeProvider.GetUtcNow());
        await generationStore.SaveAsync(session, cancellationToken);
        return RecipeApplicationResult<bool>.Succeeded(true);
    }

    // A new session holding just this candidate: selection keeps its claims, replay, usage ledger,
    // and library shortcut, and the source's privacy and ingredient approach carry over.
    private async Task<RecipeApplicationResult<RecipeDetailView>> OpenCandidateAsync(Guid ownerUserId, RecipeGenerationSession? source, SuggestCandidate candidate, Guid key, string correlationId, string? requestLanguage, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var claim = await generationStore.TryClaimNewSessionAsync(RecipeGenerationSession.Create(ownerUserId, key, now, source: source), cancellationToken);
        if (claim.WasCreated)
        {
            var active = (await inventory.ListActiveAsync(ownerUserId, 200, cancellationToken)).Select(lot => lot.LotId.ToString("n")).ToHashSet(StringComparer.Ordinal);
            var response = new SuggestCandidatesResponse { Operation = "recipe.suggest_candidates.v1", SchemaVersion = "0.3", Candidates = [WithCurrentPantry(candidate, active)], Clarifications = [] };
            claim.Session.AttachValidatedCandidates(JsonSerializer.Serialize(response, SerializerOptions), now);
            await generationStore.SaveAsync(claim.Session, cancellationToken);
        }

        return await cookNow.SelectCandidateAsync(new SelectCandidateCommand(claim.Session.Id, candidate.CandidateId, key, correlationId, requestLanguage), cancellationToken);
    }

    /// <summary>Moves inventory uses of lots no longer in the pantry to ingredients to buy, within the protocol's limit.</summary>
    private static SuggestCandidate WithCurrentPantry(SuggestCandidate candidate, IReadOnlySet<string> activeLots)
    {
        var gone = candidate.InventoryUses.Where(use => !activeLots.Contains(use.InventoryItemId)).ToList();
        if (gone.Count == 0)
        {
            return candidate;
        }

        var additional = candidate.AdditionalIngredients
            .Concat(gone.Select(use => new SuggestAdditionalIngredient { Name = use.UserName, RequiredQuantity = use.RequiredQuantity, Unit = use.Unit, Optional = false }))
            .Take(SuggestCandidate.MaxAdditionalIngredients)
            .ToList();
        return new SuggestCandidate
        {
            CandidateId = candidate.CandidateId,
            CandidateStrategy = candidate.CandidateStrategy,
            Name = candidate.Name,
            TargetMealType = candidate.TargetMealType,
            DishFormat = candidate.DishFormat,
            PrimaryTechnique = candidate.PrimaryTechnique,
            PrimaryIngredientRefs = candidate.PrimaryIngredientRefs,
            Summary = candidate.Summary,
            Servings = candidate.Servings,
            Time = candidate.Time,
            Difficulty = candidate.Difficulty,
            RequiredEquipmentIds = candidate.RequiredEquipmentIds,
            RequiredCapabilities = candidate.RequiredCapabilities,
            InventoryUses = candidate.InventoryUses.Except(gone).ToList(),
            AdditionalIngredients = additional,
            PreparationProfile = candidate.PreparationProfile,
            AssumptionsUsed = candidate.AssumptionsUsed
        };
    }

    private static SavedCandidateView ToView(SavedCandidateRecord saved) => new(
        saved.SavedId,
        saved.SessionId,
        RecipeCookNowApplicationService.ToCandidateView(JsonSerializer.Deserialize<SuggestCandidate>(saved.CandidateJson, SerializerOptions)!),
        saved.CreatedAt);
}
