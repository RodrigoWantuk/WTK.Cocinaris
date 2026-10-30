using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Owner-scoped persistence port for recipe executions.</summary>
public interface IRecipeExecutionStore
{
    /// <summary>Finds one owned execution, or null without disclosing another owner's execution.</summary>
    Task<RecipeExecution?> FindAsync(Guid ownerUserId, Guid executionId, CancellationToken cancellationToken);

    /// <summary>Lists the owner's executions started from any of the given menu entries (PLAN-0029).</summary>
    Task<IReadOnlyList<RecipeExecution>> ListForMenuEntriesAsync(Guid ownerUserId, IReadOnlyCollection<Guid> menuEntryIds, CancellationToken cancellationToken);

    /// <summary>Finds the owner's in-progress execution of a recipe, if any.</summary>
    Task<RecipeExecution?> FindInProgressAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken);

    /// <summary>Lists the owner's in-progress executions, most recently started first.</summary>
    Task<IReadOnlyList<RecipeExecution>> ListInProgressAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Gets the content of one pinned revision of an owned recipe.</summary>
    Task<RecipeDetail?> FindRevisionAsync(Guid ownerUserId, Guid recipeId, int revisionNumber, CancellationToken cancellationToken);

    /// <summary>Inserts a new execution.</summary>
    Task AddAsync(RecipeExecution execution, CancellationToken cancellationToken);

    /// <summary>Saves a state change; returns false when <paramref name="expectedVersion"/> is stale.</summary>
    Task<bool> UpdateAsync(RecipeExecution execution, Guid expectedVersion, CancellationToken cancellationToken);
}

/// <summary>Opens the database transaction that makes completion and inventory reconciliation atomic.</summary>
public interface IRecipeExecutionUnitOfWork
{
    /// <summary>Begins a transaction shared by the recipe and inventory stores of the current scope.</summary>
    Task<IRecipeExecutionTransaction> BeginAsync(CancellationToken cancellationToken);
}

/// <summary>A transaction that rolls back when disposed without <see cref="CommitAsync"/>.</summary>
public interface IRecipeExecutionTransaction : IAsyncDisposable
{
    /// <summary>Commits every change made in the transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Starts an execution with the given portions (the revision's servings when null).</summary>
/// <param name="RecipeId">Recipe to cook.</param>
/// <param name="Servings">Portions to cook.</param>
/// <param name="MenuEntryId">Menu entry the session starts from; recorded only on a new execution.</param>
public sealed record StartRecipeExecutionCommand(Guid RecipeId, int? Servings, Guid? MenuEntryId = null);

/// <summary>One lot quantity the user confirms as used, in the lot's canonical unit, with the lot version read.</summary>
public sealed record ExecutionConsumptionCommand(Guid LotId, decimal Quantity, string Unit, Guid Version);

/// <summary>Leftover portions to store as one prepared lot.</summary>
public sealed record ExecutionLeftoverCommand(int Portions, string StorageLocation, DateOnly? UseBy);

/// <summary>Completes an execution and reconciles inventory atomically.</summary>
public sealed record CompleteRecipeExecutionCommand(Guid ExecutionId, IReadOnlyList<ExecutionConsumptionCommand>? Consumptions, ExecutionLeftoverCommand? Leftover, int? Rating, string? Notes, Guid? IdempotencyKey, string CorrelationId);

/// <summary>
/// One recipe ingredient matched against the pantry. <see cref="Status"/> is <c>ready</c>,
/// <c>insufficient</c> (the lot has less than needed; the proposal uses all of it),
/// <c>lot_unavailable</c> (the lot was used up, deleted, or is not measured), or
/// <c>not_tracked</c> (the recipe did not take it from the pantry).
/// </summary>
/// <remarks>
/// <see cref="Status"/> is <c>ready</c>, <c>insufficient</c>, <c>protected</c> (the owner locked the whole usable
/// quantity), <c>lot_unavailable</c>, or <c>not_tracked</c>. <see cref="ProtectedQuantity"/> is never proposed;
/// <see cref="ReservedElsewhere"/> lists other menu meals counting on the same lot (PLAN-0029 Phase 3).
/// </remarks>
public sealed record ExecutionProposalLineView(string IngredientId, string DisplayName, bool Optional, decimal RequiredQuantity, string Unit, string Status, Guid? LotId, decimal? ProposedQuantity, decimal ProtectedQuantity = 0, IReadOnlyList<LotReservationView>? ReservedElsewhere = null);

/// <summary>An active measured pantry lot the user can pick for any ingredient.</summary>
/// <remarks>
/// <see cref="ProtectedQuantity"/> is locked by the owner; <see cref="ReservedQuantity"/> is reserved for
/// the other menu meals in <see cref="ReservedFor"/>, so any lot the cook picks can name them.
/// </remarks>
public sealed record ExecutionPantryLotView(Guid LotId, string ProductName, decimal Quantity, string Unit, string StorageLocation, Guid Version, decimal ProtectedQuantity = 0, decimal ReservedQuantity = 0, IReadOnlyList<LotReservationView>? ReservedFor = null);

/// <summary>Deterministic completion proposal: what to deduct, editable by the user before confirming.</summary>
public sealed record ExecutionProposalView(IReadOnlyList<ExecutionProposalLineView> Lines, IReadOnlyList<ExecutionPantryLotView> PantryLots);

/// <summary>Owner-visible execution state; <see cref="Proposal"/> is present while in progress.</summary>
public sealed record RecipeExecutionView(Guid ExecutionId, Guid RecipeId, int RevisionNumber, string RecipeName, int Servings, int RecipeServings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, Guid? InventoryBatchId, int? Rating, string? Notes, string NormalizedRecipeJson, ExecutionProposalView? Proposal);

/// <summary>Compact in-progress execution for the home and recipe pages.</summary>
public sealed record RecipeExecutionSummaryView(Guid ExecutionId, Guid RecipeId, string RecipeName, int Servings, DateTimeOffset StartedAt);

/// <summary>
/// Recipe execution workflow (PLAN-0042 Phase 1): explicit start, deterministic completion
/// proposal from the pinned revision and the current pantry, and completion that consumes the
/// confirmed lots and stores leftovers through the inventory preparation contract in the same
/// database transaction as the execution state change. Nothing is consumed without the user's
/// confirmation, and the proposal never blocks a choice: the user may change every line.
/// </summary>
public sealed class RecipeExecutionApplicationService(
    ICurrentUserAccessor currentUser,
    IRecipeExecutionStore store,
    IRecipeStore recipeStore,
    IRecipeExecutionUnitOfWork unitOfWork,
    IInventoryLotReadStore inventory,
    IPrepareComponentsUseCase prepare,
    IPantryCommitmentLookup commitments,
    IIngredientMatching matching,
    TimeProvider timeProvider)
{
    private const int MaxPantryLots = 200;
    private const int MaxConsumptions = 32;
    private static readonly HashSet<string> StorageLocations = ["Pantry", "Refrigerator", "Freezer"];

    /// <summary>Starts cooking, or returns the recipe's execution already in progress.</summary>
    public async Task<RecipeApplicationResult<RecipeExecutionView>> StartAsync(StartRecipeExecutionCommand command, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await recipeStore.FindDetailAsync(user.Id, command.RecipeId, cancellationToken);
        if (recipe is null)
        {
            return Failure<RecipeExecutionView>("resource_not_found", "The recipe was not found.");
        }

        if (command.Servings is < 1 or > 24)
        {
            return Failure<RecipeExecutionView>("validation_failed", "Servings must be between 1 and 24.", "servings");
        }

        // One active execution per recipe keeps a double tap or a second tab from starting twice.
        var existing = await store.FindInProgressAsync(user.Id, recipe.RecipeId, cancellationToken);
        if (existing is not null)
        {
            return RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(existing, user.Id, cancellationToken));
        }

        var execution = RecipeExecution.Start(user.Id, recipe.RecipeId, recipe.RevisionNumber, command.Servings ?? recipe.Servings, timeProvider.GetUtcNow(), command.MenuEntryId);
        await store.AddAsync(execution, cancellationToken);
        return RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken), RecipeApplicationSuccess.Created);
    }

    /// <summary>Gets one owned execution with its current proposal.</summary>
    public async Task<RecipeApplicationResult<RecipeExecutionView>> GetAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var execution = await store.FindAsync(user.Id, executionId, cancellationToken);
        return execution is null
            ? Failure<RecipeExecutionView>("resource_not_found", "The recipe execution was not found.")
            : RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken));
    }

    /// <summary>Lists the owner's in-progress executions.</summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<RecipeExecutionSummaryView>>> ListInProgressAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var executions = await store.ListInProgressAsync(user.Id, 10, cancellationToken);
        var result = new List<RecipeExecutionSummaryView>(executions.Count);
        foreach (var execution in executions)
        {
            var revision = await store.FindRevisionAsync(user.Id, execution.RecipeId, execution.RevisionNumber, cancellationToken);
            if (revision is not null)
            {
                result.Add(new RecipeExecutionSummaryView(execution.Id, execution.RecipeId, revision.Name, execution.Servings, execution.StartedAt));
            }
        }

        return RecipeApplicationResult<IReadOnlyList<RecipeExecutionSummaryView>>.Succeeded(result);
    }

    /// <summary>Abandons an in-progress execution without touching inventory.</summary>
    public async Task<RecipeApplicationResult<RecipeExecutionView>> AbandonAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var execution = await store.FindAsync(user.Id, executionId, cancellationToken);
        if (execution is null)
        {
            return Failure<RecipeExecutionView>("resource_not_found", "The recipe execution was not found.");
        }

        if (execution.Status == RecipeExecutionStatus.Abandoned)
        {
            return RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken));
        }

        if (execution.Status != RecipeExecutionStatus.InProgress)
        {
            return Failure<RecipeExecutionView>("domain_rule_violated", "A completed execution cannot be abandoned.");
        }

        var expected = execution.Version;
        execution.Abandon(timeProvider.GetUtcNow());
        return await store.UpdateAsync(execution, expected, cancellationToken)
            ? RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken))
            : Failure<RecipeExecutionView>("precondition_failed", "The recipe execution changed. Reload it.");
    }

    /// <summary>
    /// Completes an execution. The confirmed consumptions and the leftover lot are written by the
    /// inventory preparation workflow inside the same transaction as the execution state change, so
    /// either everything commits or nothing does. A retry with the same Idempotency-Key replays.
    /// </summary>
    public async Task<RecipeApplicationResult<RecipeExecutionView>> CompleteAsync(CompleteRecipeExecutionCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not { } key || key == Guid.Empty)
        {
            return Failure<RecipeExecutionView>("validation_failed", "A UUID Idempotency-Key header is required.", "Idempotency-Key");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var execution = await store.FindAsync(user.Id, command.ExecutionId, cancellationToken);
        if (execution is null)
        {
            return Failure<RecipeExecutionView>("resource_not_found", "The recipe execution was not found.");
        }

        if (execution.Status != RecipeExecutionStatus.InProgress)
        {
            return execution.Status == RecipeExecutionStatus.Completed && execution.CompletionKey == key
                ? RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken))
                : Failure<RecipeExecutionView>("domain_rule_violated", "This recipe execution is already finished.");
        }

        var consumptions = command.Consumptions ?? [];
        if (consumptions.Count > MaxConsumptions || consumptions.Any(item => item.LotId == Guid.Empty || item.Quantity <= 0))
        {
            return Failure<RecipeExecutionView>("validation_failed", "Consumptions must contain at most 32 positive lot quantities.", "consumptions");
        }

        if (command.Rating is < 1 or > 5 || command.Notes?.Trim().Length > RecipeExecution.MaxNotesLength)
        {
            return Failure<RecipeExecutionView>("validation_failed", "Rating must be 1 to 5 and notes at most 1000 characters.", "rating");
        }

        if (command.Leftover is { } leftover && (leftover.Portions < 1 || leftover.Portions > execution.Servings || !StorageLocations.Contains(leftover.StorageLocation)))
        {
            return Failure<RecipeExecutionView>("validation_failed", "Leftover portions must be between 1 and the portions cooked, stored in the pantry, refrigerator, or freezer.", "leftover");
        }

        // Two ingredients may come from the same lot; the inventory contract takes one line per lot.
        var grouped = consumptions.GroupBy(item => item.LotId).ToList();
        if (grouped.Any(group => group.Select(item => (item.Unit, item.Version)).Distinct().Count() > 1))
        {
            return Failure<RecipeExecutionView>("validation_failed", "Lines for the same lot must use the same unit and version.", "consumptions");
        }

        var revision = await store.FindRevisionAsync(user.Id, execution.RecipeId, execution.RevisionNumber, cancellationToken);
        if (revision is null)
        {
            return Failure<RecipeExecutionView>("resource_not_found", "The recipe was not found.");
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await unitOfWork.BeginAsync(cancellationToken);
        Guid? batchId = null;
        if (grouped.Count > 0 || command.Leftover is not null)
        {
            var inputs = grouped.Select(group => new PreparationInputCommand(group.Key, group.Sum(item => item.Quantity), group.First().Unit, InventoryVersionPrecondition.Valid(group.First().Version))).ToList();
            var outputs = command.Leftover is { } stored
                ? new[] { new PreparationOutputCommand(stored.Portions, "Unit", null, stored.StorageLocation, null, null, stored.UseBy, stored.UseBy is null ? null : "UserEntered", stored.UseBy is null ? null : "Medium", null) }
                : [];
            var result = await prepare.PrepareAsync(new PrepareComponentsCommand(null, revision.Name, execution.Servings, "Unit", null, inputs, outputs, now, key, command.CorrelationId, PreparationSourceType.RecipeExecution, execution.Id), cancellationToken);
            if (result.Problem is { } problem)
            {
                return RecipeApplicationResult<RecipeExecutionView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
            }

            batchId = result.Value!.BatchId;
        }

        var expected = execution.Version;
        execution.Complete(key, batchId, command.Rating, command.Notes, now);
        if (!await store.UpdateAsync(execution, expected, cancellationToken))
        {
            // A concurrent completion won; the inventory writes of this attempt roll back with the transaction.
            return Failure<RecipeExecutionView>("precondition_failed", "The recipe execution changed. Reload it.");
        }

        await transaction.CommitAsync(cancellationToken);
        return RecipeApplicationResult<RecipeExecutionView>.Succeeded(await ViewAsync(execution, user.Id, cancellationToken));
    }

    private async Task<RecipeExecutionView> ViewAsync(RecipeExecution execution, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var revision = await store.FindRevisionAsync(ownerUserId, execution.RecipeId, execution.RevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("An execution references a missing recipe revision.");
        ExecutionProposalView? proposal = null;
        if (execution.Status == RecipeExecutionStatus.InProgress)
        {
            var lots = await inventory.ListActiveAsync(ownerUserId, MaxPantryLots, cancellationToken);
            var committed = await commitments.GetAsync(ownerUserId, cancellationToken);
            var keys = await matching.KeysAsync(ownerUserId, lots.Select(lot => lot.ProductName).Concat(RecipeIngredientReader.Read(revision.NormalizedRecipeJson).Select(ingredient => ingredient.Name)), cancellationToken);
            proposal = BuildProposal(revision.NormalizedRecipeJson, revision.Servings, execution.Servings, lots, committed, execution.MenuEntryId, keys);
        }

        return new RecipeExecutionView(execution.Id, execution.RecipeId, execution.RevisionNumber, revision.Name, execution.Servings, revision.Servings, execution.Status.ToString(), execution.StartedAt, execution.FinishedAt, execution.InventoryBatchId, execution.Rating, execution.Notes, revision.NormalizedRecipeJson, proposal);
    }

    /// <summary>
    /// Builds the deterministic proposal. Quantities scale with the portions cooked; whole units
    /// round up (half an egg is still an egg used) and grams and milliliters round to whole numbers.
    /// A proposal never exceeds what the lot holds, and never includes quantity the owner protected.
    /// An ingredient without its own lot is matched to a pantry lot by name and unit.
    /// Menu reservations of other meals do not change the proposal; they are returned so the cook
    /// sees which meal would be short (the owner decides, the proposal does not block).
    /// </summary>
    /// <param name="normalizedRecipeJson">Validated revision content.</param>
    /// <param name="recipeServings">Servings the revision was written for.</param>
    /// <param name="servings">Servings being cooked.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <param name="commitments">Planning commitments by lot; none when null.</param>
    /// <param name="ownMenuEntryId">Menu meal this session cooks; its own reservations are not warnings.</param>
    /// <param name="matchKeys">Ingredient matching keys (PLAN-0055); names only when null.</param>
    /// <returns>The proposal lines and the pickable measured lots.</returns>
    public static ExecutionProposalView BuildProposal(string normalizedRecipeJson, int recipeServings, int servings, IReadOnlyList<InventoryLotReadModel> lots, IReadOnlyDictionary<Guid, LotCommitment>? commitments = null, Guid? ownMenuEntryId = null, IngredientMatchKeys? matchKeys = null)
    {
        commitments ??= new Dictionary<Guid, LotCommitment>();
        matchKeys ??= IngredientMatchKeys.NamesOnly;
        decimal Protected(Guid lotId) => commitments.TryGetValue(lotId, out var commitment) ? commitment.ProtectedQuantity : 0;
        IReadOnlyList<LotReservationView> Elsewhere(Guid lotId) => commitments.TryGetValue(lotId, out var commitment)
            ? commitment.Reservations.Where(item => item.MenuEntryId != ownMenuEntryId).ToList()
            : [];
        var measured = lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null).ToList();
        var byId = measured.ToDictionary(lot => lot.LotId);
        var scale = (decimal)servings / Math.Max(1, recipeServings);
        var lines = new List<ExecutionProposalLineView>();
        foreach (var ingredient in RecipeIngredientReader.Read(normalizedRecipeJson))
        {
            var unit = RecipeIngredientReader.ToLotUnit(ingredient.Unit);
            var required = RecipeIngredientReader.Scale(ingredient.Quantity, unit, scale);
            // The lot the recipe was written for comes first; otherwise a pantry lot with the same
            // name or the same catalog ingredient, and the same unit (case and accents ignored), so a
            // recipe written by hand, in another language, or for a lot used up and bought again
            // still proposes the pantry item.
            var linked = ingredient.SourceType == "inventory" && ingredient.LotId is { } lotId && byId.TryGetValue(lotId, out var exact) && exact.MeasuredUnit == unit ? exact : null;
            var ingredientKey = matchKeys.Key(ingredient.Name);
            var named = linked ?? measured
                .Where(candidate => candidate.MeasuredUnit == unit && matchKeys.Key(candidate.ProductName) == ingredientKey)
                .OrderByDescending(candidate => candidate.MeasuredValue)
                .FirstOrDefault();
            if (named is not { } lot)
            {
                lines.Add(new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, ingredient.SourceType == "inventory" ? "lot_unavailable" : "not_tracked", null, null));
                continue;
            }

            var protectedQuantity = Math.Min(Protected(lot.LotId), lot.MeasuredValue!.Value);
            var available = lot.MeasuredValue!.Value - protectedQuantity;
            var elsewhere = Elsewhere(lot.LotId);
            lines.Add(available <= 0
                ? new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "protected", lot.LotId, null, protectedQuantity, elsewhere)
                : required <= available
                    ? new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "ready", lot.LotId, required, protectedQuantity, elsewhere)
                    : new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "insufficient", lot.LotId, available, protectedQuantity, elsewhere));
        }

        var pantry = measured.OrderBy(lot => lot.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .Select(lot => new ExecutionPantryLotView(lot.LotId, lot.ProductName, lot.MeasuredValue!.Value, lot.MeasuredUnit!, lot.StorageLocation, lot.ConcurrencyToken, Math.Min(Protected(lot.LotId), lot.MeasuredValue!.Value), Elsewhere(lot.LotId).Sum(item => item.Quantity), Elsewhere(lot.LotId))).ToList();
        return new ExecutionProposalView(lines, pantry);
    }


    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, string? field = null) =>
        RecipeApplicationResult<T>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [detail] });
}
