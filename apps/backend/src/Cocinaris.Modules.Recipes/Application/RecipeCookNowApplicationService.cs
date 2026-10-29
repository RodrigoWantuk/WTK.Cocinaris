using System.Text.Json;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>
/// Module-owned application service for the complete authenticated cook-now recipe generation
/// vertical slice: claim a generation session, reserve usage, invoke the AI Gateway (with at most
/// one repair), then finalize candidates or expansion atomically. AI output is never trusted or
/// persisted until it passes <see cref="RecipeProtocolValidator"/> schema and semantic validation.
/// </summary>
/// <remarks>
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
    TimeProvider timeProvider)
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

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var session = RecipeGenerationSession.Create(user.Id, command.IdempotencyKey.Value, now, command.MenuTarget is null ? "cook_now" : "menu_planning");
        var claim = await generationStore.TryClaimNewSessionAsync(session, cancellationToken);
        if (!claim.WasCreated)
        {
            return RecipeApplicationResult<RecipeGenerationSessionView>.Succeeded(ToSessionView(claim.Session));
        }

        var reservation = await usageGovernor.ReserveAsync(user.Id, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, command.CorrelationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            claim.Session.MarkFailed($"usage_{reservation.Outcome}", timeProvider.GetUtcNow());
            await generationStore.SaveAsync(claim.Session, cancellationToken);
            return Failure<RecipeGenerationSessionView>(MapReservationFailure(reservation.Outcome), ReservationDetail(reservation.Outcome));
        }

        var context = await contextAssembler.AssembleSuggestContextAsync(user.Id, Guid.NewGuid().ToString("n"), definition.ContextBudgetCharacters, cancellationToken, command.MenuTarget);
        context = context with { Presets = WithOutputLanguage(context.Presets, command.RequestLanguage) };
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

        var reservation = await usageGovernor.ReserveAsync(user.Id, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, command.CorrelationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            session.MarkFailed($"usage_{reservation.Outcome}", timeProvider.GetUtcNow());
            await generationStore.SaveAsync(session, cancellationToken);
            return Failure<RecipeDetailView>(MapReservationFailure(reservation.Outcome), ReservationDetail(reservation.Outcome));
        }

        var context = await contextAssembler.AssembleExpandContextAsync(user.Id, Guid.NewGuid().ToString("n"), selected, definition.ContextBudgetCharacters, cancellationToken);
        context = context with { Presets = WithOutputLanguage(context.Presets, command.RequestLanguage) };
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
            definition,
            correlationId,
            initialPayload,
            errors => RecipeAiRequestEnvelopes.BuildSuggestRepairRequest(context, errors.Errors, errors.Output),
            raw =>
            {
                var validation = RecipeProtocolValidator.ValidateSuggest(raw, requestElement);
                return (validation.IsValid ? validation.Response : null, validation.Errors);
            },
            response => RecipeOutputQuality.CheckSuggest(response, context.Presets.OutputLanguage),
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
            definition,
            correlationId,
            initialPayload,
            errors => RecipeAiRequestEnvelopes.BuildExpandRepairRequest(context, errors.Errors, errors.Output),
            raw =>
            {
                var validation = RecipeProtocolValidator.ValidateExpand(raw, requestElement);
                return (validation.IsValid ? validation.Response : null, validation.Errors);
            },
            response => RecipeOutputQuality.CheckExpand(response, context.Presets.OutputLanguage, detailLevel),
            cancellationToken);
    }

    /// <summary>
    /// Calls the provider and allows <see cref="AiOperationDefinition.MaxRepairAttempts"/> repairs.
    /// A schema-invalid answer is repaired with its validation errors. A schema-valid answer with
    /// soft quality issues (language, detail) is also sent for repair, but it is kept: if the repair
    /// is not clean, the first valid answer is returned with its quality flags (PLAN-0040). Usage
    /// (tokens, latency, attempts) is summed across every call, failed ones included.
    /// </summary>
    private async Task<AiInvocationOutcome<T>> InvokeWithRepairAsync<T>(
        AiOperationDefinition definition,
        string correlationId,
        string initialPayload,
        Func<(IReadOnlyList<string> Errors, string Output), string> buildRepair,
        Func<string, (T? Response, IReadOnlyList<string> Errors)> validate,
        Func<T, IReadOnlyList<RecipeQualityIssue>> checkQuality,
        CancellationToken cancellationToken)
        where T : class
    {
        string? modelUsed = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var attempts = 0;
        int? latencyMs = null;
        AiProviderFailureKind? lastTransportFailure = null;
        IReadOnlyList<string>? lastErrors = null;
        string? lastOutput = null;
        (T Response, IReadOnlyList<RecipeQualityIssue> Issues)? kept = null;
        AiCallUsage Usage(string? flags = null) => new(aiProvider.Name, modelUsed, promptTokens, completionTokens, attempts, latencyMs, flags);
        string Flags(IReadOnlyList<RecipeQualityIssue> issues) => string.Join(",", issues.Select(issue => issue.Flag).Distinct());

        for (var attempt = 0; attempt <= definition.MaxRepairAttempts; attempt++)
        {
            var payload = attempt == 0 ? initialPayload : buildRepair((lastErrors ?? ["validation_failed"], lastOutput ?? string.Empty));
            var invocation = await aiProvider.InvokeAsync(new AiProviderInvocationRequest(definition.Operation, payload, definition.PreferNonThinking, definition.TimeoutSeconds, correlationId, definition.MaxOutputTokens), cancellationToken);
            attempts += invocation.ProviderCalls;
            latencyMs = SumNullable(latencyMs, invocation.LatencyMs);
            if (!invocation.IsSuccess)
            {
                lastTransportFailure = invocation.FailureKind;
                continue;
            }

            modelUsed = invocation.ModelUsed;
            promptTokens = SumNullable(promptTokens, invocation.PromptTokens);
            completionTokens = SumNullable(completionTokens, invocation.CompletionTokens);
            var (response, errors) = validate(invocation.RawContent!);
            if (response is null)
            {
                lastErrors = errors;
                lastOutput = invocation.RawContent;
                continue;
            }

            var issues = checkQuality(response);
            if (issues.Count == 0)
            {
                return new AiInvocationOutcome<T>(response, Usage(), null, null);
            }

            // Keep the first valid answer; a later one replaces it only if it has fewer issues.
            if (kept is null || issues.Count < kept.Value.Issues.Count)
            {
                kept = (response, issues);
            }

            lastErrors = issues.Select(issue => issue.Instruction).ToList();
            lastOutput = invocation.RawContent;
        }

        if (kept is { } accepted)
        {
            return new AiInvocationOutcome<T>(accepted.Response, Usage(Flags(accepted.Issues)), null, null);
        }

        return new AiInvocationOutcome<T>(null, Usage(), MapProviderFailure(lastTransportFailure, lastErrors is not null), DescribeProviderFailure(lastTransportFailure, lastErrors is not null));
    }

    private sealed record AiInvocationOutcome<T>(T? Response, AiCallUsage Usage, string? FailureCode, string? FailureDetail)
        where T : class;

    private static RecipeGenerationSession MarkFailedCopy(RecipeGenerationSession session, string reason, DateTimeOffset now)
    {
        session.MarkFailed(reason, now);
        return session;
    }

    private static int? SumNullable(int? left, int? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static RecipeGenerationSessionView ToSessionView(RecipeGenerationSession session, IReadOnlyList<SuggestCandidate>? candidates = null)
    {
        var resolved = candidates ?? (session.CandidatesSnapshotJson is null ? null : JsonSerializer.Deserialize<SuggestCandidatesResponse>(session.CandidatesSnapshotJson, SerializerOptions)!.Candidates);
        return new RecipeGenerationSessionView(session.Id, session.Status.ToString(), resolved?.Select(ToCandidateView).ToList(), session.FailureReason);
    }

    private static RecipeCandidateView ToCandidateView(SuggestCandidate candidate) => new(
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

    private static string MapReservationFailure(AiUsageReservationOutcome outcome) => outcome switch
    {
        AiUsageReservationOutcome.Disabled => "ai_capability_unavailable",
        AiUsageReservationOutcome.GlobalBudgetExhausted or AiUsageReservationOutcome.UserBudgetExhausted or AiUsageReservationOutcome.MonthlyCostExhausted
            or AiUsageReservationOutcome.GlobalTokenWindowExhausted or AiUsageReservationOutcome.UserTokenWindowExhausted => "ai_budget_exhausted",
        AiUsageReservationOutcome.ConcurrencyExhausted => "ai_operation_conflict",
        _ => "ai_budget_unavailable"
    };

    private static string ReservationDetail(AiUsageReservationOutcome outcome) => outcome switch
    {
        AiUsageReservationOutcome.Disabled => "The AI Gateway is currently disabled.",
        AiUsageReservationOutcome.GlobalBudgetExhausted => "The global AI usage budget for today is exhausted.",
        AiUsageReservationOutcome.UserBudgetExhausted => "Your daily AI usage budget is exhausted.",
        AiUsageReservationOutcome.ConcurrencyExhausted => "You already have the maximum number of AI requests in progress.",
        AiUsageReservationOutcome.MonthlyCostExhausted => "The monthly AI spending limit for this deployment is reached.",
        AiUsageReservationOutcome.GlobalTokenWindowExhausted => "The AI usage limit for the last 24 hours is reached; it frees up as earlier calls leave the window.",
        AiUsageReservationOutcome.UserTokenWindowExhausted => "Your AI usage limit for the last 24 hours is reached; it frees up as your earlier calls leave the window.",
        _ => "The AI Gateway is currently unavailable."
    };

    private static string MapProviderFailure(AiProviderFailureKind? failureKind, bool sawInvalidOutput)
    {
        if (sawInvalidOutput)
        {
            return "ai_output_invalid";
        }

        return failureKind switch
        {
            AiProviderFailureKind.Timeout => "ai_provider_timeout",
            AiProviderFailureKind.Unavailable => "ai_provider_unavailable",
            _ => "ai_provider_unavailable"
        };
    }

    private static string DescribeProviderFailure(AiProviderFailureKind? failureKind, bool sawInvalidOutput)
    {
        if (sawInvalidOutput)
        {
            return "The AI provider returned output that failed protocol validation after the allowed repair attempt.";
        }

        return failureKind switch
        {
            AiProviderFailureKind.Timeout => "The AI provider timed out before returning a usable response.",
            AiProviderFailureKind.Unavailable => "The AI provider is currently unavailable.",
            _ => "The AI provider did not return a usable response."
        };
    }

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
