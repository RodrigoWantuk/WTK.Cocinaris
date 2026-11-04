using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Asks the AI for a new version of a recipe.</summary>
/// <param name="RecipeId">Recipe to revise.</param>
/// <param name="ExpectedRevisionNumber">Revision the owner is looking at; a newer one makes the request stale.</param>
/// <param name="Instruction">What the owner wants changed (at most 300 characters); untrusted text.</param>
/// <param name="IdempotencyKey">Request key; a retry returns the same proposal.</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="RequestLanguage">Interface language, used when the profile declares none.</param>
public sealed record ProposeRevisionCommand(Guid RecipeId, int ExpectedRevisionNumber, string? Instruction, Guid? IdempotencyKey, string CorrelationId, string? RequestLanguage = null);

/// <summary>API-facing proposal.</summary>
/// <param name="ProposalId">Proposal identifier.</param>
/// <param name="RecipeId">Recipe being revised.</param>
/// <param name="BaseRevisionNumber">Revision it was written from.</param>
/// <param name="Instruction">The owner's request.</param>
/// <param name="Status"><c>Generating</c>, <c>Ready</c>, <c>Accepted</c>, <c>Discarded</c>, or <c>Failed</c>.</param>
/// <param name="Name">Proposed name.</param>
/// <param name="MealTypes">Proposed meal types.</param>
/// <param name="Servings">Proposed servings.</param>
/// <param name="NormalizedRecipeJson">Proposed content while <c>Ready</c>.</param>
/// <param name="AcceptedRevisionNumber">Revision created by accepting.</param>
/// <param name="CreatedAt">When it was requested.</param>
public sealed record RecipeRevisionProposalView(
    Guid ProposalId,
    Guid RecipeId,
    int BaseRevisionNumber,
    string Instruction,
    string Status,
    string? Name,
    IReadOnlyList<string> MealTypes,
    int? Servings,
    string? NormalizedRecipeJson,
    int? AcceptedRevisionNumber,
    DateTimeOffset CreatedAt);

/// <summary>Owner-scoped persistence of revision proposals.</summary>
public interface IRecipeRevisionProposalStore
{
    /// <summary>Inserts the proposal, or returns the owner's existing one for the same key.</summary>
    /// <returns>Whether this call created it, and the proposal.</returns>
    Task<(bool WasCreated, RecipeRevisionProposal Proposal)> TryClaimAsync(RecipeRevisionProposal proposal, CancellationToken cancellationToken);

    /// <summary>Finds one of the owner's proposals for a recipe.</summary>
    Task<RecipeRevisionProposal?> FindAsync(Guid ownerUserId, Guid recipeId, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>Saves a status change that involves neither usage nor the recipe.</summary>
    Task SaveAsync(RecipeRevisionProposal proposal, CancellationToken cancellationToken);

    /// <summary>Saves the validated version and settles the usage reservation in one transaction.</summary>
    Task CompleteAsync(RecipeRevisionProposal proposal, Guid reservationId, int settledUnits, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Saves the failure and releases the usage reservation in one transaction.</summary>
    Task FailAsync(RecipeRevisionProposal proposal, Guid reservationId, AiCallUsage usage, long costMicroUsd, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// In one transaction, appends <paramref name="revision"/>, advances the recipe while it is still
    /// at <paramref name="expectedRevisionNumber"/>, and saves the accepted proposal. Returns false,
    /// persisting nothing, when another revision was saved first.
    /// </summary>
    Task<bool> TryAcceptAsync(Recipe recipe, RecipeRevision revision, int expectedRevisionNumber, RecipeRevisionProposal proposal, CancellationToken cancellationToken);
}

/// <summary>
/// "Revisar com IA" (PLAN-0060, owner request 6): the owner describes a change in a few words, the AI
/// writes a complete new version, and the owner accepts or discards it.
/// </summary>
/// <remarks>
/// The AI never changes the recipe: its output is stored only as a proposal after passing the same
/// validation as an expansion, and only the owner's acceptance appends it, as a new immutable
/// revision with its own origin. Older revisions stay reachable. The request is untrusted text and
/// reaches the model only inside a named data field. These recipes never enter the shared idea
/// library, because they can carry the owner's words.
/// </remarks>
public sealed class RecipeRevisionProposalService(
    ICurrentUserAccessor currentUser,
    IRecipeRevisionProposalStore proposals,
    IRecipeLibraryStore recipes,
    IRecipeContextAssembler contextAssembler,
    IAiProvider aiProvider,
    AiOperationRegistry operationRegistry,
    AiUsageGovernor usageGovernor,
    RecipeLibraryApplicationService library,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Generates a proposal; on failure nothing is proposed and the usage is released.</summary>
    public async Task<RecipeApplicationResult<RecipeRevisionProposalView>> ProposeAsync(ProposeRevisionCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<RecipeRevisionProposalView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (command.Instruction is { Length: > RecipeRevisionProposal.MaxInstructionLength }
            || RecipeUntrustedText.Normalize(command.Instruction, RecipeRevisionProposal.MaxInstructionLength) is not { } instruction)
        {
            return Failure<RecipeRevisionProposalView>("validation_failed", "Say what to change, in at most 300 characters.", new Dictionary<string, string[]> { ["instruction"] = ["Say what to change, in at most 300 characters."] });
        }

        if (!operationRegistry.TryGet(AiOperationRegistry.ReviseRecipe, out var definition))
        {
            return Failure<RecipeRevisionProposalView>("ai_capability_unavailable", "The recipe revision capability is not registered.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await recipes.FindAsync(user.Id, command.RecipeId, cancellationToken);
        if (recipe is null)
        {
            return Failure<RecipeRevisionProposalView>("resource_not_found", "The recipe was not found.");
        }

        var (wasCreated, proposal) = await proposals.TryClaimAsync(RecipeRevisionProposal.Create(user.Id, recipe.Id, command.ExpectedRevisionNumber, key, instruction, timeProvider.GetUtcNow()), cancellationToken);
        if (!wasCreated)
        {
            return proposal.RecipeId != recipe.Id
                ? Failure<RecipeRevisionProposalView>("domain_rule_violated", "This Idempotency-Key was already used for another recipe.")
                : RecipeApplicationResult<RecipeRevisionProposalView>.Succeeded(ToView(proposal));
        }

        if (recipe.CurrentRevisionNumber != command.ExpectedRevisionNumber)
        {
            proposal.MarkFailed("stale_revision", timeProvider.GetUtcNow());
            await proposals.SaveAsync(proposal, cancellationToken);
            return Failure<RecipeRevisionProposalView>("precondition_failed", "The recipe has a newer revision. Reload it before asking for a revision.");
        }

        var current = await recipes.FindRevisionAsync(user.Id, recipe.Id, recipe.CurrentRevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The current revision of a stored recipe is missing.");
        var context = await contextAssembler.AssembleReviseContextAsync(user.Id, Guid.NewGuid().ToString("n"), recipe.Id.ToString("n"), current.RevisionNumber + 1, current.NormalizedRecipeJson, instruction, definition.ContextBudgetCharacters, cancellationToken);
        context = context with { Presets = RecipeCookNowApplicationService.WithOutputLanguage(context.Presets, command.RequestLanguage) };

        var reservation = await usageGovernor.ReserveAsync(user.Id, definition.Operation, definition.EstimatedUsageUnits, definition.EstimatedCostMicroUsd, definition.EstimatedTokens, command.CorrelationId, cancellationToken);
        if (reservation.Outcome != AiUsageReservationOutcome.Reserved)
        {
            proposal.MarkFailed($"usage_{reservation.Outcome}", timeProvider.GetUtcNow());
            await proposals.SaveAsync(proposal, cancellationToken);
            return Failure<RecipeRevisionProposalView>(RecipeAiInvocation.MapReservationFailure(reservation.Outcome), RecipeAiInvocation.ReservationDetail(reservation.Outcome));
        }

        var initialPayload = RecipeAiRequestEnvelopes.BuildReviseRequest(context);
        using var requestDocument = JsonDocument.Parse(initialPayload);
        var requestElement = requestDocument.RootElement;
        var detailLevel = RecipeOutputQuality.DetailLevel(context.Presets);
        var invocation = await RecipeAiInvocation.InvokeWithRepairAsync<ExpandSelectedResponse>(
            aiProvider,
            definition,
            command.CorrelationId,
            initialPayload,
            errors => RecipeAiRequestEnvelopes.BuildReviseRepairRequest(context, errors.Errors, errors.Output),
            raw =>
            {
                var validation = RecipeProtocolValidator.ValidateRevise(raw, requestElement);
                return (validation.IsValid ? validation.Response : null, validation.Errors);
            },
            response => RecipeOutputQuality.CheckExpand(response, context.Presets.OutputLanguage, detailLevel),
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (invocation.Response is null)
        {
            proposal.MarkFailed(invocation.FailureCode ?? "ai_output_invalid", now);
            await proposals.FailAsync(proposal, reservation.ReservationId!.Value, invocation.Usage, usageGovernor.CostMicroUsd(invocation.Usage), now, cancellationToken);
            return Failure<RecipeRevisionProposalView>(invocation.FailureCode ?? "ai_output_invalid", invocation.FailureDetail ?? "The AI provider did not return a valid revised recipe.");
        }

        var revised = invocation.Response.Recipe;
        proposal.AttachVersion(revised.Name, revised.MealTypes, revised.Servings, WithIdentity(revised, recipe.Id, current.RevisionNumber + 1), RecipeRevisionMapper.NormalizeThumbnailVisual(revised.ThumbnailVisual), now);
        await proposals.CompleteAsync(proposal, reservation.ReservationId!.Value, definition.EstimatedUsageUnits, invocation.Usage, usageGovernor.CostMicroUsd(invocation.Usage), now, cancellationToken);
        return RecipeApplicationResult<RecipeRevisionProposalView>.Succeeded(ToView(proposal), RecipeApplicationSuccess.Created);
    }

    /// <summary>Gets one of the owner's proposals.</summary>
    public async Task<RecipeApplicationResult<RecipeRevisionProposalView>> GetAsync(Guid recipeId, Guid proposalId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var proposal = await proposals.FindAsync(user.Id, recipeId, proposalId, cancellationToken);
        return proposal is null ? NotFound<RecipeRevisionProposalView>() : RecipeApplicationResult<RecipeRevisionProposalView>.Succeeded(ToView(proposal));
    }

    /// <summary>
    /// Appends the proposed version as the recipe's next revision. A repeated acceptance returns the
    /// same revision; a recipe that moved on since the proposal fails with <c>precondition_failed</c>.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeLibraryDetailView>> AcceptAsync(Guid recipeId, Guid proposalId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var proposal = await proposals.FindAsync(user.Id, recipeId, proposalId, cancellationToken);
        if (proposal is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        if (proposal.Status == RecipeRevisionProposalStatus.Accepted)
        {
            return await library.GetAsync(recipeId, proposal.AcceptedRevisionNumber, cancellationToken);
        }

        if (proposal.Status != RecipeRevisionProposalStatus.Ready)
        {
            return Failure<RecipeLibraryDetailView>("precondition_failed", "This proposal has no version to accept.");
        }

        var recipe = await recipes.FindAsync(user.Id, recipeId, cancellationToken);
        if (recipe is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        const string Stale = "The recipe has a newer revision. Ask for the revision again.";
        if (recipe.CurrentRevisionNumber != proposal.BaseRevisionNumber)
        {
            return Failure<RecipeLibraryDetailView>("precondition_failed", Stale);
        }

        var now = timeProvider.GetUtcNow();
        var next = recipe.CurrentRevisionNumber + 1;
        var reason = proposal.Instruction.Length <= Recipe.MaxReasonLength ? proposal.Instruction : proposal.Instruction[..Recipe.MaxReasonLength].TrimEnd();
        var revision = RecipeRevision.CreateAuthored(recipe.Id, user.Id, next, RecipeRevisionOrigin.AiRevised, proposal.Name!, proposal.MealTypes, proposal.Servings!.Value, proposal.NormalizedRecipeJson!, proposal.ThumbnailVisualJson!, reason, null, now);
        var expected = recipe.CurrentRevisionNumber;
        recipe.AdvanceToRevision(next, now);
        proposal.MarkAccepted(next, now);
        if (!await proposals.TryAcceptAsync(recipe, revision, expected, proposal, cancellationToken))
        {
            return Failure<RecipeLibraryDetailView>("precondition_failed", Stale);
        }

        var detail = await library.GetAsync(recipeId, next, cancellationToken);
        return detail.Problem is null ? RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(detail.Value, RecipeApplicationSuccess.Created) : detail;
    }

    /// <summary>Discards the proposed version; discarding it again changes nothing.</summary>
    public async Task<RecipeApplicationResult<bool>> DiscardAsync(Guid recipeId, Guid proposalId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var proposal = await proposals.FindAsync(user.Id, recipeId, proposalId, cancellationToken);
        if (proposal is null)
        {
            return NotFound<bool>();
        }

        if (proposal.Status == RecipeRevisionProposalStatus.Ready)
        {
            proposal.MarkDiscarded(timeProvider.GetUtcNow());
            await proposals.SaveAsync(proposal, cancellationToken);
        }

        return proposal.Status is RecipeRevisionProposalStatus.Discarded
            ? RecipeApplicationResult<bool>.Succeeded(true)
            : Failure<bool>("precondition_failed", "This proposal can no longer be discarded.");
    }

    // The ids the model echoed are not trusted: the stored content always names this recipe and revision.
    private static string WithIdentity(ExpandRecipe revised, Guid recipeId, int revisionNumber)
    {
        var node = JsonNode.Parse(RecipeRevisionMapper.NormalizeRecipe(revised))!.AsObject();
        node["recipeId"] = recipeId.ToString("n");
        node["revision"] = revisionNumber;
        return node.ToJsonString(SerializerOptions);
    }

    private static RecipeRevisionProposalView ToView(RecipeRevisionProposal proposal) => new(
        proposal.Id,
        proposal.RecipeId,
        proposal.BaseRevisionNumber,
        proposal.Instruction,
        proposal.Status.ToString(),
        proposal.Name,
        proposal.MealTypes,
        proposal.Servings,
        proposal.NormalizedRecipeJson,
        proposal.AcceptedRevisionNumber,
        proposal.CreatedAt);

    private static RecipeApplicationResult<T> NotFound<T>() => Failure<T>("resource_not_found", "The proposal was not found.");

    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => RecipeApplicationResult<T>.Failure(code, detail, errors);
}
