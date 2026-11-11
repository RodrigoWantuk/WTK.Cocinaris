using Cocinaris.Modules.Ai.Safety;
using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Domain;
using Cocinaris.Modules.Recipes.Ideas;
using static Cocinaris.Modules.Recipes.Application.RecipeAiInvocation;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>
/// Module-owned application service for the complete authenticated cook-now recipe generation
/// vertical slice: claim a generation session, reserve usage, invoke the AI Gateway (with at most
/// one repair), then finalize candidates or expansion atomically. AI output is never trusted or
/// persisted until it passes <see cref="RecipeProtocolValidator"/> schema and semantic validation.
/// </summary>
/// <remarks>
/// Before calling the AI, both steps ask the shared idea library (<see cref="RecipeIdeaLibrary"/>,
/// PLAN-0055 Phase 2). A suggestion request it can answer, or a library idea it can rebuild,
/// costs no AI call and no usage. After an AI expansion, the recipe is offered to the library.
/// Session and selection claims prevent concurrent duplicate provider calls. An unavoidable crash
/// window remains between a successful provider return and the finalization database commit.
/// </remarks>
public sealed class RecipeCookNowApplicationService(
    ICurrentUserAccessor currentUser,
    IRecipeGenerationStore generationStore,
    IRecipeStore recipeStore,
    IRecipeCookNowUnitOfWork unitOfWork,
    IRecipeContextAssembler contextAssembler,
    IAiProvider aiProvider,
    AiOperationRegistry operationRegistry,
    AiUsageGovernor usageGovernor,
    TimeProvider timeProvider,
    RecipeIdeaLibrary? ideas = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a cook-now generation session and requests exactly three validated candidates.
    /// Replays the prior result when <see cref="RequestCandidatesCommand.IdempotencyKey"/> was
    /// already claimed by this owner, without re-invoking the AI Gateway or re-reserving usage.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeGenerationSessionView>> RequestCandidatesAsync(RequestCandidatesCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is null)
        {
            return Failure<RecipeGenerationSessionView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (!operationRegistry.TryGet(AiOperationRegistry.SuggestCandidates, out var definition))
        {
            return Failure<RecipeGenerationSessionView>("ai_capability_unavailable", "The recipe suggestion capability is not registered.");
        }

        if (command.Guidance?.Validate() is { Count: > 0 } guidanceErrors)
        {
            return Failure<RecipeGenerationSessionView>("validation_failed", "The suggestion guidance is invalid.", guidanceErrors);
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        // The hint is cleaned once, here, so the stored session and the model see the same text.
        var guidanceHint = UntrustedText.Normalize(command.Guidance?.Hint, RecipeSuggestionGuidance.MaxHintLength);
        var requested = command.Guidance is null ? null : command.Guidance with { Hint = guidanceHint };
        var session = RecipeGenerationSession.Create(user.Id, command.IdempotencyKey.Value, now, command.MenuTarget is null ? "cook_now" : "menu_planning", guidanceHint, requested?.Adaptation);
        var claim = await generationStore.TryClaimNewSessionAsync(session, cancellationToken);
        if (!claim.WasCreated)
        {
            return RecipeApplicationResult<RecipeGenerationSessionView>.Succeeded(ToSessionView(claim.Session));
        }

        var context = await contextAssembler.AssembleSuggestContextAsync(user.Id, Guid.NewGuid().ToString("n"), definition.ContextBudgetCharacters, cancellationToken, command.MenuTarget, requested);
        context = context with { Presets = WithOutputLanguage(context.Presets, command.RequestLanguage) };
        // A required item must be in the pantry with a measured quantity; otherwise no candidate could use it.
        if (requested is { RequiredLotIds.Count: > 0 } guidance && context.InventoryItems.Count(item => item.MustUse) != guidance.RequiredLotIds.Count)
        {
            claim.Session.MarkFailed("guidance_required_unavailable", timeProvider.GetUtcNow());
            await generationStore.SaveAsync(claim.Session, cancellationToken);
            return Failure<RecipeGenerationSessionView>("validation_failed", "A required pantry item is not available.", new Dictionary<string, string[]> { ["guidance.requiredLotIds"] = ["A required pantry item is not available."] });
        }

        if (ideas is not null && await ideas.TrySuggestAsync(user.Id, context, cancellationToken) is { } fromLibrary)
        {
            claim.Session.AttachValidatedCandidates(JsonSerializer.Serialize(fromLibrary, SerializerOptions), timeProvider.GetUtcNow());
            await generationStore.SaveAsync(claim.Session, cancellationToken);
            return RecipeApplicationResult<RecipeGenerationSessionView>.Succeeded(ToSessionView(claim.Session, fromLibrary.Candidates));
        }

        var reservation = await usageGovernor.ReserveAsync(user.Id, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, command.CorrelationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            claim.Session.MarkFailed($"usage_{reservation.Outcome}", timeProvider.GetUtcNow());
            await generationStore.SaveAsync(claim.Session, cancellationToken);
            return Failure<RecipeGenerationSessionView>(MapReservationFailure(reservation.Outcome), ReservationDetail(reservation.Outcome));
        }

        var invocation = await InvokeSuggestWithRepairAsync(definition, context, command.CorrelationId, cancellationToken);
        if (invocation.Response is null)
        {
            await unitOfWork.FailSuggestAsync(
                claim.Session,
                reservation.ReservationId!.Value,
                invocation.FailureCode ?? "ai_output_invalid",
                invocation.Usage,
                usageGovernor.CostMicroUsd(invocation.Usage),
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Failure<RecipeGenerationSessionView>(invocation.FailureCode ?? "ai_output_invalid", invocation.FailureDetail ?? "The AI provider did not return three validated candidates.");
        }

        var completedAt = timeProvider.GetUtcNow();
        claim.Session.AttachValidatedCandidates(JsonSerializer.Serialize(invocation.Response, SerializerOptions), completedAt);
        await unitOfWork.CompleteSuggestAsync(
            claim.Session,
            reservation.ReservationId!.Value,
            definition.EstimatedUsageUnits,
            invocation.Usage,
            usageGovernor.CostMicroUsd(invocation.Usage),
            completedAt,
            cancellationToken);
        return RecipeApplicationResult<RecipeGenerationSessionView>.Succeeded(ToSessionView(claim.Session, invocation.Response.Candidates));
    }

    /// <summary>Gets one owner-scoped generation session and its validated candidates, when present.</summary>
    public async Task<RecipeApplicationResult<RecipeGenerationSessionView>> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await generationStore.FindByIdAsync(user.Id, sessionId, cancellationToken);
        return session is null
            ? Failure<RecipeGenerationSessionView>("resource_not_found", "The generation session was not found.")
            : RecipeApplicationResult<RecipeGenerationSessionView>.Succeeded(ToSessionView(session));
    }

    /// <summary>
    /// Claims Expanding selection, expands one candidate through the AI Gateway, and persists
    /// exactly one new owner-isolated immutable recipe and its first revision on success. Replays the
    /// prior recipe when <see cref="SelectCandidateCommand.IdempotencyKey"/> was already used.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeDetailView>> SelectCandidateAsync(SelectCandidateCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is null)
        {
            return Failure<RecipeDetailView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (string.IsNullOrWhiteSpace(command.CandidateId))
        {
            return Failure<RecipeDetailView>("validation_failed", "A candidate identifier is required.");
        }

        if (!operationRegistry.TryGet(AiOperationRegistry.ExpandSelected, out var definition))
        {
            return Failure<RecipeDetailView>("ai_capability_unavailable", "The recipe expansion capability is not registered.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var claim = await generationStore.TryClaimSelectionAsync(
            user.Id,
            command.SessionId,
            command.CandidateId,
            command.IdempotencyKey.Value,
            now,
            cancellationToken);

        switch (claim.Outcome)
        {
            case RecipeSelectionClaimOutcome.NotFound:
                return Failure<RecipeDetailView>("resource_not_found", "The generation session was not found.");
            case RecipeSelectionClaimOutcome.NotReady:
                return Failure<RecipeDetailView>("precondition_failed", claim.Session!.IsExpired(now)
                    ? "The generation session has expired."
                    : "Candidates are not ready for selection on this session.");
            case RecipeSelectionClaimOutcome.Conflict:
                return Failure<RecipeDetailView>("ai_operation_conflict", "A conflicting candidate selection is already in progress or completed for this request.");
            case RecipeSelectionClaimOutcome.InProgress:
                return Failure<RecipeDetailView>("ai_operation_conflict", "Selection expansion is already in progress for this request.");
            case RecipeSelectionClaimOutcome.AlreadySelected:
                return await ReplaySelectedRecipeAsync(user.Id, claim.Session!, cancellationToken);
            case RecipeSelectionClaimOutcome.Claimed:
                break;
            default:
                return Failure<RecipeDetailView>("ai_operation_conflict", "The selection claim could not be completed.");
        }

        var session = claim.Session!;
        var candidates = JsonSerializer.Deserialize<SuggestCandidatesResponse>(session.CandidatesSnapshotJson!, SerializerOptions)!;
        var selected = candidates.Candidates.FirstOrDefault(item => string.Equals(item.CandidateId, command.CandidateId, StringComparison.Ordinal));
        if (selected is null)
        {
            await generationStore.SaveAsync(MarkFailedCopy(session, "resource_not_found", timeProvider.GetUtcNow()), cancellationToken);
            return Failure<RecipeDetailView>("resource_not_found", "The candidate was not found in this generation session.");
        }

        var context = await contextAssembler.AssembleExpandContextAsync(user.Id, Guid.NewGuid().ToString("n"), selected, definition.ContextBudgetCharacters, cancellationToken);
        context = context with { Presets = WithOutputLanguage(context.Presets, command.RequestLanguage), Adaptation = session.Adaptation };
        if (ideas is not null && RecipeIdeaLibrary.TryParseCandidateId(selected.CandidateId, out var ideaId)
            && await ideas.TryBuildRecipeAsync(user.Id, ideaId, context, cancellationToken) is { } fromIdea)
        {
            var builtAt = timeProvider.GetUtcNow();
            var ideaRecipe = Recipe.Create(user.Id, builtAt);
            var ideaRevision = RecipeRevision.CreateFirst(
                ideaRecipe.Id,
                user.Id,
                fromIdea.Name,
                fromIdea.MealTypes,
                fromIdea.Servings,
                RecipeRevisionMapper.NormalizeRecipe(fromIdea),
                RecipeRevisionMapper.NormalizeThumbnailVisual(fromIdea.ThumbnailVisual),
                selected.CandidateId,
                session.Id,
                builtAt);
            await unitOfWork.FinalizeLibrarySelectionAsync(ideaRecipe, ideaRevision, session, builtAt, cancellationToken);
            return RecipeApplicationResult<RecipeDetailView>.Succeeded(ToDetailView(ideaRevision), RecipeApplicationSuccess.Created);
        }

        var reservation = await usageGovernor.ReserveAsync(user.Id, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, command.CorrelationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            session.MarkFailed($"usage_{reservation.Outcome}", timeProvider.GetUtcNow());
            await generationStore.SaveAsync(session, cancellationToken);
            return Failure<RecipeDetailView>(MapReservationFailure(reservation.Outcome), ReservationDetail(reservation.Outcome));
        }

        var invocation = await InvokeExpandWithRepairAsync(definition, context, command.CorrelationId, cancellationToken);
        if (invocation.Response is null)
        {
            await unitOfWork.FailExpansionAsync(
                session,
                reservation.ReservationId!.Value,
                invocation.FailureCode ?? "ai_output_invalid",
                invocation.Usage,
                usageGovernor.CostMicroUsd(invocation.Usage),
                timeProvider.GetUtcNow(),
                cancellationToken);
            return Failure<RecipeDetailView>(invocation.FailureCode ?? "ai_output_invalid", invocation.FailureDetail ?? "The AI provider did not return a valid expanded recipe.");
        }

        var completedAt = timeProvider.GetUtcNow();
        var expandedRecipe = invocation.Response.Recipe;
        var recipe = Recipe.Create(user.Id, completedAt);
        var revision = RecipeRevision.CreateFirst(
            recipe.Id,
            user.Id,
            expandedRecipe.Name,
            expandedRecipe.MealTypes,
            expandedRecipe.Servings,
            RecipeRevisionMapper.NormalizeRecipe(expandedRecipe),
            RecipeRevisionMapper.NormalizeThumbnailVisual(expandedRecipe.ThumbnailVisual),
            selected.CandidateId,
            session.Id,
            completedAt);

        await unitOfWork.FinalizeExpansionAsync(
            recipe,
            revision,
            session,
            reservation.ReservationId!.Value,
            definition.EstimatedUsageUnits,
            invocation.Usage,
            usageGovernor.CostMicroUsd(invocation.Usage),
            completedAt,
            cancellationToken);
        // A recipe shaped by the person's own words may carry them, and the library is shared with
        // everyone, so an injected or private hint must never reach it (PLAN-0060).
        if (ideas is not null && !session.PrivateDirection)
        {
            try
            {
                await ideas.TryCaptureAsync(user.Id, selected, expandedRecipe, context, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Capture is best effort: the owner's recipe is already saved, and a missing idea
                // only means a later request asks the AI again.
            }
        }

        return RecipeApplicationResult<RecipeDetailView>.Succeeded(ToDetailView(revision), RecipeApplicationSuccess.Created);
    }

    /// <summary>Gets one owned recipe's current revision without cross-user disclosure.</summary>
    public async Task<RecipeApplicationResult<RecipeDetailView>> GetRecipeAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var detail = await recipeStore.FindDetailAsync(user.Id, recipeId, cancellationToken);
        return detail is null
            ? Failure<RecipeDetailView>("resource_not_found", "The recipe was not found.")
            : RecipeApplicationResult<RecipeDetailView>.Succeeded(ToDetailView(detail));
    }

    private async Task<RecipeApplicationResult<RecipeDetailView>> ReplaySelectedRecipeAsync(Guid ownerUserId, RecipeGenerationSession session, CancellationToken cancellationToken)
    {
        if (session.SelectedRecipeId is not Guid recipeId)
        {
            return Failure<RecipeDetailView>("resource_not_found", "The previously selected recipe was not found.");
        }

        var priorDetail = await recipeStore.FindDetailAsync(ownerUserId, recipeId, cancellationToken);
        return priorDetail is null
            ? Failure<RecipeDetailView>("resource_not_found", "The previously selected recipe was not found.")
            : RecipeApplicationResult<RecipeDetailView>.Succeeded(ToDetailView(priorDetail));
    }

    private async Task<AiInvocationOutcome<SuggestCandidatesResponse>> InvokeSuggestWithRepairAsync(
        AiOperationDefinition definition, RecipeSuggestRequestContext context, string correlationId, CancellationToken cancellationToken)
    {
        var initialPayload = RecipeAiRequestEnvelopes.BuildSuggestRequest(context);
        using var requestDocument = JsonDocument.Parse(initialPayload);
        var requestElement = requestDocument.RootElement;
        return await InvokeWithRepairAsync(
            aiProvider,
            definition,
            correlationId,
            initialPayload,
            errors => RecipeAiRequestEnvelopes.BuildSuggestRepairRequest(context, errors.Errors, errors.Output),
            raw => RecipeProtocolValidator.ValidateSuggest(raw, requestElement),
            response => RecipeOutputQuality.CheckSuggest(response, context.Presets.OutputLanguage, context.Guidance?.Adaptation),
            cancellationToken);
    }

    private async Task<AiInvocationOutcome<ExpandSelectedResponse>> InvokeExpandWithRepairAsync(
        AiOperationDefinition definition, RecipeExpandRequestContext context, string correlationId, CancellationToken cancellationToken)
    {
        var initialPayload = RecipeAiRequestEnvelopes.BuildExpandRequest(context);
        using var requestDocument = JsonDocument.Parse(initialPayload);
        var requestElement = requestDocument.RootElement;
        var detailLevel = RecipeOutputQuality.DetailLevel(context.Presets);
        return await InvokeWithRepairAsync(
            aiProvider,
            definition,
            correlationId,
            initialPayload,
            errors => RecipeAiRequestEnvelopes.BuildExpandRepairRequest(context, errors.Errors, errors.Output),
            raw => RecipeProtocolValidator.ValidateExpand(raw, requestElement),
            response => RecipeOutputQuality.CheckExpand(response, context.Presets.OutputLanguage, detailLevel),
            cancellationToken);
    }

    /// <summary>
    /// Compact, content-free labels of validation errors for the usage ledger (at most 100
    /// characters): indices, identifiers, and quoted values are removed, so no recipe or pantry text is
    /// stored, only the rule with its limits (for example <c>storage.reheatNotes must be a string with
    /// length between 1 and 240</c>; PLAN-0039 R5).
    /// </summary>
    public static string InvalidFlags(IReadOnlyList<string> errors) => "invalid:" + RecipeAiInvocation.RuleLabels(errors, 92);

    private static RecipeGenerationSession MarkFailedCopy(RecipeGenerationSession session, string reason, DateTimeOffset now)
    {
        session.MarkFailed(reason, now);
        return session;
    }

    private static RecipeGenerationSessionView ToSessionView(RecipeGenerationSession session, IReadOnlyList<SuggestCandidate>? candidates = null)
    {
        var resolved = candidates ?? (session.CandidatesSnapshotJson is null ? null : JsonSerializer.Deserialize<SuggestCandidatesResponse>(session.CandidatesSnapshotJson, SerializerOptions)!.Candidates);
        return new RecipeGenerationSessionView(session.Id, session.Status.ToString(), resolved?.Select(ToCandidateView).ToList(), session.FailureReason);
    }

    /// <summary>API-facing projection of one validated candidate.</summary>
    public static RecipeCandidateView ToCandidateView(SuggestCandidate candidate) => new(
        candidate.CandidateId,
        candidate.CandidateStrategy,
        candidate.Name,
        candidate.TargetMealType,
        candidate.DishFormat,
        candidate.PrimaryTechnique,
        candidate.PrimaryIngredientRefs,
        candidate.Summary,
        candidate.Servings,
        candidate.Time.ActiveMinutes,
        candidate.Time.PassiveMinutes,
        candidate.Time.TotalMinutes,
        candidate.Difficulty,
        candidate.RequiredEquipmentIds,
        candidate.RequiredCapabilities);

    private static RecipeDetailView ToDetailView(RecipeRevision revision) => new(revision.RecipeId, revision.RevisionNumber, revision.Name, revision.MealTypes, revision.Servings, revision.NormalizedRecipeJson, revision.ThumbnailVisualJson, revision.CreatedAt);
    private static RecipeDetailView ToDetailView(RecipeDetail detail) => new(detail.RecipeId, detail.RevisionNumber, detail.Name, detail.MealTypes, detail.Servings, detail.NormalizedRecipeJson, detail.ThumbnailVisualJson, detail.CreatedAt);
    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => RecipeApplicationResult<T>.Failure(code, detail, errors);

    /// <summary>
    /// Resolves the language the model must write in: the profile language, else the request's
    /// interface language, else Brazilian Portuguese (the primary market). Anything outside the
    /// supported set falls through, so a header can never inject free text into the envelope.
    /// </summary>
    /// <param name="presets">Presets assembled from the profile.</param>
    /// <param name="requestLanguage">Interface language of the request, if any.</param>
    /// <returns>Presets with a resolved, supported output language.</returns>
    public static RecipeUserPresets WithOutputLanguage(RecipeUserPresets presets, string? requestLanguage)
    {
        static string? Supported(string? tag) => tag is "pt-BR" or "en" or "es" ? tag : null;
        return presets with { OutputLanguage = Supported(presets.OutputLanguage) ?? Supported(requestLanguage) ?? "pt-BR" };
    }
}
