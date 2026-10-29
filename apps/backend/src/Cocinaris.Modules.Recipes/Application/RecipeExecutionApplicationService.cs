using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Owner-scoped persistence port for recipe executions.</summary>
public interface IRecipeExecutionStore
{
    /// <summary>Finds one owned execution, or null without disclosing another owner's execution.</summary>
    Task<RecipeExecution?> FindAsync(Guid ownerUserId, Guid executionId, CancellationToken cancellationToken);

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
public sealed record StartRecipeExecutionCommand(Guid RecipeId, int? Servings);

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
public sealed record ExecutionProposalLineView(string IngredientId, string DisplayName, bool Optional, decimal RequiredQuantity, string Unit, string Status, Guid? LotId, decimal? ProposedQuantity);

/// <summary>An active measured pantry lot the user can pick for any ingredient.</summary>
public sealed record ExecutionPantryLotView(Guid LotId, string ProductName, decimal Quantity, string Unit, string StorageLocation, Guid Version);

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

        var execution = RecipeExecution.Start(user.Id, recipe.RecipeId, recipe.RevisionNumber, command.Servings ?? recipe.Servings, timeProvider.GetUtcNow());
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
            proposal = BuildProposal(revision.NormalizedRecipeJson, revision.Servings, execution.Servings, lots);
        }

        return new RecipeExecutionView(execution.Id, execution.RecipeId, execution.RevisionNumber, revision.Name, execution.Servings, revision.Servings, execution.Status.ToString(), execution.StartedAt, execution.FinishedAt, execution.InventoryBatchId, execution.Rating, execution.Notes, revision.NormalizedRecipeJson, proposal);
    }

    /// <summary>
    /// Builds the deterministic proposal. Quantities scale with the portions cooked; whole units
    /// round up (half an egg is still an egg used) and grams and milliliters round to whole numbers.
    /// A proposal never exceeds what the lot holds.
    /// </summary>
    /// <param name="normalizedRecipeJson">Validated revision content.</param>
    /// <param name="recipeServings">Servings the revision was written for.</param>
    /// <param name="servings">Servings being cooked.</param>
    /// <param name="lots">The owner's active lots.</param>
    /// <returns>The proposal lines and the pickable measured lots.</returns>
    public static ExecutionProposalView BuildProposal(string normalizedRecipeJson, int recipeServings, int servings, IReadOnlyList<InventoryLotReadModel> lots)
    {
        var measured = lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null).ToList();
        var byId = measured.ToDictionary(lot => lot.LotId);
        var scale = (decimal)servings / Math.Max(1, recipeServings);
        var lines = new List<ExecutionProposalLineView>();
        foreach (var ingredient in ReadIngredients(normalizedRecipeJson))
        {
            var unit = ToLotUnit(ingredient.Unit);
            var required = unit == "Unit" ? Math.Ceiling(ingredient.Quantity * scale) : Math.Round(ingredient.Quantity * scale, 0, MidpointRounding.AwayFromZero);
            required = Math.Max(required, unit == "Unit" ? 1 : 0.001m);
            if (ingredient.SourceType != "inventory")
            {
                lines.Add(new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "not_tracked", null, null));
                continue;
            }

            if (ingredient.LotId is not { } lotId || !byId.TryGetValue(lotId, out var lot) || lot.MeasuredUnit != unit)
            {
                lines.Add(new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "lot_unavailable", null, null));
                continue;
            }

            var available = lot.MeasuredValue!.Value;
            lines.Add(required <= available
                ? new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "ready", lot.LotId, required)
                : new(ingredient.Id, ingredient.Name, ingredient.Optional, required, unit, "insufficient", lot.LotId, available));
        }

        var pantry = measured.OrderBy(lot => lot.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .Select(lot => new ExecutionPantryLotView(lot.LotId, lot.ProductName, lot.MeasuredValue!.Value, lot.MeasuredUnit!, lot.StorageLocation, lot.ConcurrencyToken)).ToList();
        return new ExecutionProposalView(lines, pantry);
    }

    private sealed record Ingredient(string Id, string Name, string SourceType, Guid? LotId, decimal Quantity, string Unit, bool Optional);

    private static IEnumerable<Ingredient> ReadIngredients(string json)
    {
        // The revision was schema-validated before it was saved; this read stays defensive so a
        // malformed row degrades to an empty proposal instead of failing the cooking page.
        JsonDocument document;
        try { document = JsonDocument.Parse(json); } catch (JsonException) { yield break; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("recipe", out var recipe)) { root = recipe; }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ingredients", out var items) || items.ValueKind != JsonValueKind.Array) { yield break; }
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("requiredQuantity", out var quantity) || quantity.ValueKind != JsonValueKind.Number) { continue; }
                Guid? lotId = item.TryGetProperty("inventoryItemId", out var reference) && reference.ValueKind == JsonValueKind.String && Guid.TryParse(reference.GetString(), out var parsed) ? parsed : null;
                yield return new Ingredient(
                    Text(item, "ingredientId") ?? string.Empty,
                    Text(item, "displayName") ?? string.Empty,
                    Text(item, "sourceType") ?? "assumption",
                    lotId,
                    quantity.GetDecimal(),
                    Text(item, "unit") ?? "unit",
                    item.TryGetProperty("optional", out var optional) && optional.ValueKind == JsonValueKind.True);
            }
        }
    }

    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ToLotUnit(string recipeUnit) => recipeUnit switch { "g" => "Gram", "ml" => "Milliliter", _ => "Unit" };

    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, string? field = null) =>
        RecipeApplicationResult<T>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [detail] });
}
