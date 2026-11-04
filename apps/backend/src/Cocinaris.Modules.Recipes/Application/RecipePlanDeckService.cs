using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Safety;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Domain;
using Cocinaris.Modules.Recipes.Ideas;
using static Cocinaris.Modules.Recipes.Application.RecipeAiInvocation;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>What a shopping plan needs from the Recipes module for one batch of cards.</summary>
/// <param name="Servings">People in the plan.</param>
/// <param name="Brief">The plan's requirements for the model.</param>
/// <param name="Guidance">"More options" chips, closed codes only; null for none.</param>
/// <param name="RequestLanguage">Interface language of the request, used when the profile declares none.</param>
public sealed record PlanDeckRequest(int Servings, PlanDeckBrief Brief, RecipeSuggestionGuidance? Guidance, string? RequestLanguage);

/// <summary>
/// AI generation and expansion for shopping plan cards (PLAN-0062). It holds no plan state: the
/// Planning module owns sessions and ranking and calls this service through its public methods.
/// Output is never returned until it passes validation, and nothing here changes a menu or the pantry.
/// </summary>
public sealed class RecipePlanDeckService(
    IRecipeGenerationStore generationStore,
    IRecipeContextAssembler contextAssembler,
    RecipeCookNowApplicationService cookNow,
    IAiProvider aiProvider,
    AiOperationRegistry operationRegistry,
    AiUsageGovernor usageGovernor,
    TimeProvider timeProvider,
    RecipeIdeaLibrary? ideas = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Longest typed anchor ingredient, in characters.</summary>
    public const int MaxAnchorLength = 60;

    /// <summary>Cleans a typed anchor ingredient, so the model and the stored session see the same text.</summary>
    /// <param name="text">Text the person typed.</param>
    public static string? CleanAnchor(string? text) => UntrustedText.Normalize(text, MaxAnchorLength);

    /// <summary>Library ideas that fit the plan, without an AI call; empty when none fit.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="request">Plan requirements.</param>
    /// <param name="limit">Most ideas returned.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<IReadOnlyList<SuggestCandidate>> FindIdeasAsync(Guid ownerUserId, PlanDeckRequest request, int limit, CancellationToken cancellationToken)
    {
        if (ideas is null || !operationRegistry.TryGet(AiOperationRegistry.SuggestPlanDeck, out var definition))
        {
            return [];
        }

        return await ideas.FindPlanDeckAsync(ownerUserId, await ContextAsync(ownerUserId, request, definition, cancellationToken), request.Brief.MealTypes, limit, cancellationToken);
    }

    /// <summary>Asks the model for one batch of cards. Usage is reserved first and settled or released afterward.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="request">Plan requirements.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Six validated candidates, or a stable failure code.</returns>
    public async Task<RecipeApplicationResult<IReadOnlyList<SuggestCandidate>>> GenerateBatchAsync(Guid ownerUserId, PlanDeckRequest request, string correlationId, CancellationToken cancellationToken)
    {
        if (!operationRegistry.TryGet(AiOperationRegistry.SuggestPlanDeck, out var definition))
        {
            return Failure("ai_capability_unavailable", "The plan card capability is not registered.");
        }

        if (request.Guidance?.Validate() is { Count: > 0 } invalid)
        {
            return RecipeApplicationResult<IReadOnlyList<SuggestCandidate>>.Failure("validation_failed", "The options are invalid.", invalid);
        }

        var context = await ContextAsync(ownerUserId, request, definition, cancellationToken);
        var reservation = await usageGovernor.ReserveAsync(ownerUserId, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, correlationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            return Failure(MapReservationFailure(reservation.Outcome), ReservationDetail(reservation.Outcome));
        }

        var payload = RecipeAiRequestEnvelopes.BuildPlanDeckRequest(context, request.Brief);
        using var document = JsonDocument.Parse(payload);
        var requestElement = document.RootElement;
        var invocation = await InvokeWithRepairAsync(
            aiProvider,
            definition,
            correlationId,
            payload,
            errors => RecipeAiRequestEnvelopes.BuildPlanDeckRepairRequest(context, request.Brief, errors.Errors, errors.Output),
            raw =>
            {
                var validation = RecipeProtocolValidator.ValidatePlanDeck(raw, requestElement);
                return (validation.IsValid ? validation.Response : null, validation.Errors);
            },
            response => RecipeOutputQuality.CheckPlanDeck(response, context.Presets.OutputLanguage),
            cancellationToken);
        if (invocation.Response is null)
        {
            await usageGovernor.ReleaseAsync(reservation.ReservationId!.Value, cancellationToken, invocation.Usage);
            return Failure(invocation.FailureCode ?? "ai_output_invalid", invocation.FailureDetail ?? "The AI provider did not return valid plan cards.");
        }

        await usageGovernor.SettleAsync(reservation.ReservationId!.Value, definition.EstimatedUsageUnits, invocation.Usage, cancellationToken);
        return RecipeApplicationResult<IReadOnlyList<SuggestCandidate>>.Succeeded(invocation.Response.Candidates);
    }

    /// <summary>
    /// Expands one accepted card into a saved recipe. A selection session allows one selection, so
    /// each card has its own single-candidate generation session. The keys come from the card, so a
    /// retry replays the same recipe instead of creating a second one.
    /// </summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="candidate">The validated candidate.</param>
    /// <param name="expansionKey">The card's key for this attempt.</param>
    /// <param name="hint">The typed anchor, if any. It marks the session private, so the recipe never reaches the shared library.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<RecipeDetailView>> ExpandCardAsync(Guid ownerUserId, SuggestCandidate candidate, Guid expansionKey, string? hint, string correlationId, string? language, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var claim = await generationStore.TryClaimNewSessionAsync(RecipeGenerationSession.Create(ownerUserId, expansionKey, now, "menu_planning", hint), cancellationToken);
        if (claim.WasCreated)
        {
            var snapshot = new SuggestCandidatesResponse { Operation = "recipe.suggest_candidates.v1", SchemaVersion = "0.3", Candidates = [candidate], Clarifications = [] };
            claim.Session.AttachValidatedCandidates(JsonSerializer.Serialize(snapshot, SerializerOptions), now);
            await generationStore.SaveAsync(claim.Session, cancellationToken);
        }

        return await cookNow.SelectCandidateAsync(new SelectCandidateCommand(claim.Session.Id, candidate.CandidateId, expansionKey, correlationId, language), cancellationToken);
    }

    private async Task<RecipeSuggestRequestContext> ContextAsync(Guid ownerUserId, PlanDeckRequest request, AiOperationDefinition definition, CancellationToken cancellationToken)
    {
        // A menu target limits the snapshot to stock no other meal counts on.
        var context = await contextAssembler.AssembleSuggestContextAsync(
            ownerUserId,
            Guid.NewGuid().ToString("n"),
            definition.ContextBudgetCharacters,
            cancellationToken,
            new MenuPlanningTarget(null, null, request.Servings, request.Brief.ChosenDishes),
            request.Guidance);
        return context with { Presets = RecipeCookNowApplicationService.WithOutputLanguage(context.Presets, request.RequestLanguage) };
    }

    private static RecipeApplicationResult<IReadOnlyList<SuggestCandidate>> Failure(string code, string detail) =>
        RecipeApplicationResult<IReadOnlyList<SuggestCandidate>>.Failure(code, detail);
}
