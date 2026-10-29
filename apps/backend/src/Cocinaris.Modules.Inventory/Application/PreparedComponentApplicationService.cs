using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>One measured parent-lot consumption protected by its own optimistic-concurrency token.</summary>
public sealed record PreparationInputCommand(Guid LotId, decimal? ConsumedValue, string? Unit, InventoryVersionPrecondition Precondition);

/// <summary>One stored portion of the declared preparation yield.</summary>
public sealed record PreparationOutputCommand(decimal? MeasuredValue, string? Unit, string? AvailabilityState, string? StorageLocation, string? CustomLocation, string? PackageState, DateOnly? ShelfLifeDate, string? ShelfLifeSource, string? ShelfLifeConfidence, string? ShelfLifeConditions);

/// <summary>
/// Creates one owner-scoped preparation with a single output product. A manual preparation stores
/// one or more portions that exactly partition the declared yield. A recipe-execution
/// reconciliation (<paramref name="SourceType"/> <see cref="PreparationSourceType.RecipeExecution"/>)
/// declares the portions cooked and stores zero or more leftover portions whose sum does not
/// exceed that yield; HTTP callers cannot select this source.
/// </summary>
public sealed record PrepareComponentsCommand(Guid? OutputProductId, string? OutputProductName, decimal? DeclaredYieldValue, string? DeclaredYieldUnit, string? DeclaredYieldAvailabilityState, IReadOnlyList<PreparationInputCommand>? Inputs, IReadOnlyList<PreparationOutputCommand>? Outputs, DateTimeOffset? PreparedAt, Guid? IdempotencyKey, string CorrelationId, PreparationSourceType SourceType = PreparationSourceType.ManualPreparation, Guid? SourceReferenceId = null);

/// <summary>Returns a consumed parent quantity without exposing any private parent metadata not needed for provenance.</summary>
public sealed record PreparationInputView(Guid LotId, InventoryQuantity ConsumedQuantity);

/// <summary>Returns prepared-specific state attached to an authoritative output lot.</summary>
public sealed record PreparedLotMetadataView(Guid BatchId, string LifecycleState, DateTimeOffset PreparedAt, DateOnly? ShelfLifeDate, string ShelfLifeSource, string ShelfLifeConfidence, string? ShelfLifeConditions);

/// <summary>Returns one authoritative stored output produced by a preparation batch.</summary>
public sealed record PreparationOutputView(InventoryLotView Lot, PreparedLotMetadataView PreparedMetadata);

/// <summary>Returns the owner-visible representation persisted for an idempotent preparation transaction.</summary>
public sealed record PreparationBatchView(Guid BatchId, string SourceType, Guid OutputProductId, string OutputProductName, InventoryQuantity DeclaredYield, DateTimeOffset PreparedAt, IReadOnlyList<PreparationInputView> Inputs, IReadOnlyList<PreparationOutputView> Outputs, DateTimeOffset CreatedAt);

/// <summary>
/// Returns the preparation relationship for one owned parent or output lot. Each direction is
/// bounded to fifty batches; the corresponding truncation flag tells a client that older links
/// exist but are intentionally outside this bounded v1 projection.
/// </summary>
public sealed record InventoryLotProvenanceView(Guid LotId, IReadOnlyList<PreparationBatchView> ConsumedBy, bool ConsumedByTruncated, IReadOnlyList<PreparationBatchView> ProducedBy, bool ProducedByTruncated);

/// <summary>Persists and reads preparation transactions without exposing database or HTTP concerns to the application workflow.</summary>
public interface IInventoryPreparationStore
{
    /// <summary>Finds a completed owner-scoped idempotency record for preparation creation.</summary>
    Task<PreparationIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, Guid key, CancellationToken cancellationToken);

    /// <summary>Loads every active owned input lot needed by a preparation command.</summary>
    Task<IReadOnlyList<PreparationInputState>> LoadActiveInputsAsync(Guid ownerUserId, IReadOnlyList<Guid> lotIds, CancellationToken cancellationToken);

    /// <summary>Loads an owned active output product, returning no result for absent or foreign identity.</summary>
    Task<Product?> FindActiveProductAsync(Guid ownerUserId, Guid productId, CancellationToken cancellationToken);

    /// <summary>Atomically persists parent consumption, output creation, provenance, history, audit, and idempotency.</summary>
    Task<PreparationWriteOutcome> SaveAsync(PreparationWrite write, CancellationToken cancellationToken);

    /// <summary>Gets an owned preparation batch and its immutable provenance.</summary>
    Task<PreparationBatchView?> GetAsync(Guid ownerUserId, Guid batchId, CancellationToken cancellationToken);

    /// <summary>Gets preparation relationships for an owned lot without revealing another owner's provenance.</summary>
    Task<InventoryLotProvenanceView?> GetLotProvenanceAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken);
}

/// <summary>Represents a loaded owned parent lot and product used to validate a preparation.</summary>
public sealed record PreparationInputState(InventoryLot Lot, Product Product);

/// <summary>Represents a completed preparation idempotency replay record.</summary>
public sealed record PreparationIdempotencyRead(string RequestHash, PreparationBatchView? Response, DateTimeOffset? CompletedAt);

/// <summary>Describes the classified persistence outcome of one preparation transaction attempt.</summary>
public enum PreparationWriteOutcome
{
    /// <summary>The complete transaction committed.</summary>
    Saved,
    /// <summary>One or more parent versions changed before the transaction committed.</summary>
    ConcurrencyConflict,
    /// <summary>A concurrent request claimed the same owner-scoped idempotency key.</summary>
    IdempotencyConflict
}

/// <summary>Contains a fully validated preparation graph for one atomic write.</summary>
public sealed record PreparationWrite(Guid OwnerUserId, PreparationBatch Batch, Product OutputProduct, Product? NewOutputProduct, InventoryQuantity DeclaredYield, IReadOnlyList<PreparationInputWrite> Inputs, IReadOnlyList<PreparationOutputWrite> Outputs, PreparationIdempotencyWrite Idempotency, string CorrelationId);

/// <summary>Contains one consumed parent mutation, its expected persistence version, and immutable history.</summary>
public sealed record PreparationInputWrite(InventoryLot Lot, Product Product, long ExpectedVersion, decimal ConsumedValue, InventoryTransaction Transaction);

/// <summary>Contains one new output lot, its immutable creation history, and prepared metadata.</summary>
public sealed record PreparationOutputWrite(InventoryLot Lot, InventoryTransaction Transaction, PreparedShelfLifeEvidence ShelfLifeEvidence);

/// <summary>Contains the semantic idempotency representation persisted with a preparation transaction.</summary>
public sealed record PreparationIdempotencyWrite(Guid Key, string RequestHash, PreparationBatchView Response, DateTimeOffset CreatedAt);

/// <summary>Executes owner-scoped preparation creation, replay, retrieval, and provenance inspection.</summary>
public interface IPrepareComponentsUseCase
{
    /// <summary>Consumes all declared parents and creates all declared output portions atomically.</summary>
    Task<InventoryApplicationResult<PreparationBatchView>> PrepareAsync(PrepareComponentsCommand command, CancellationToken cancellationToken);
}

/// <summary>Reads an owner-scoped preparation batch.</summary>
public interface IGetPreparationBatchUseCase
{
    /// <summary>Returns the immutable batch representation or a nondisclosing absence.</summary>
    Task<InventoryApplicationResult<PreparationBatchView>> GetAsync(Guid batchId, CancellationToken cancellationToken);
}

/// <summary>Reads preparation ancestry and descendants for one owned lot.</summary>
public interface IGetInventoryLotProvenanceUseCase
{
    /// <summary>Returns immutable preparation relationships or a nondisclosing absence.</summary>
    Task<InventoryApplicationResult<InventoryLotProvenanceView>> GetAsync(Guid lotId, CancellationToken cancellationToken);
}

/// <summary>
/// Inventory-owned workflow for manual prepared components. It validates every owner-scoped input
/// before state transitions and sends one complete graph to the persistence boundary so a retry
/// cannot consume only a subset of parent lots.
/// </summary>
public sealed class PreparedComponentApplicationWorkflow(ICurrentUserAccessor currentUser, IInventoryPreparationStore store, TimeProvider timeProvider)
{
    private const string IdempotencyScope = "inventory.preparations.create";

    /// <inheritdoc />
    public async Task<InventoryApplicationResult<PreparationBatchView>> PrepareAsync(PrepareComponentsCommand command, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (command.IdempotencyKey is null || command.IdempotencyKey == Guid.Empty)
        {
            return Failure("validation_failed", "A UUID Idempotency-Key is required.", "Idempotency-Key", "A UUID Idempotency-Key is required.");
        }

        var hash = Hash(new
        {
            command.OutputProductId,
            OutputProductName = command.OutputProductName?.Trim(),
            command.DeclaredYieldValue,
            command.DeclaredYieldUnit,
            command.DeclaredYieldAvailabilityState,
            Inputs = command.Inputs?.Select(item => new { item.LotId, item.ConsumedValue, item.Unit }).OrderBy(item => item.LotId),
            Outputs = command.Outputs?.Select(item => new { item.MeasuredValue, item.Unit, item.AvailabilityState, item.StorageLocation, item.CustomLocation, item.PackageState, item.ShelfLifeDate, item.ShelfLifeSource, item.ShelfLifeConfidence, ShelfLifeConditions = item.ShelfLifeConditions?.Trim() }).OrderBy(item => JsonSerializer.Serialize(item)),
            PreparedAt = command.PreparedAt?.ToUniversalTime(),
            SourceType = command.SourceType.ToString(),
            command.SourceReferenceId
        });
        var existing = await store.FindIdempotencyAsync(user.Id, command.IdempotencyKey.Value, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, hash);
        }

        // A recipe execution may store leftovers of a dish made only from untracked ingredients.
        var minimumInputs = command.SourceType == PreparationSourceType.RecipeExecution ? 0 : 1;
        if (command.Inputs is null || command.Inputs.Count < minimumInputs || command.Inputs.Count > 32 || command.Inputs.Any(item => item.LotId == Guid.Empty) || command.Inputs.Select(item => item.LotId).Distinct().Count() != command.Inputs.Count)
        {
            return Failure("validation_failed", "Preparation inputs must contain one to 32 unique lot identifiers.", "inputs", "Preparation inputs must contain one to 32 unique lot identifiers.");
        }

        // Owner-scoped lookup happens before quantity and ETag evaluation to avoid revealing a foreign lot's state.
        var loadedInputs = await store.LoadActiveInputsAsync(user.Id, command.Inputs.Select(item => item.LotId).ToList(), cancellationToken);
        if (loadedInputs.Count != command.Inputs.Count)
        {
            return Failure("resource_not_found", "The inventory resource was not found.");
        }

        // The owner-scoped parent lookup intentionally precedes this second replay lookup so a
        // foreign parent remains indistinguishable from an absent one. Once ownership is known,
        // a winner that committed between the first lookup and this read is authoritative.
        existing = await store.FindIdempotencyAsync(user.Id, command.IdempotencyKey.Value, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, hash);
        }

        Product outputProduct;
        Product? newOutputProduct = null;
        if (command.OutputProductId is { } outputProductId)
        {
            if (!string.IsNullOrWhiteSpace(command.OutputProductName))
            {
                return Failure("validation_failed", "Specify either outputProductId or outputProductName.", "outputProduct", "Specify either outputProductId or outputProductName.");
            }

            var existingProduct = await store.FindActiveProductAsync(user.Id, outputProductId, cancellationToken);
            if (existingProduct is null)
            {
                return Failure("resource_not_found", "The inventory resource was not found.");
            }

            outputProduct = existingProduct;
        }
        else
        {
            if (!ProductName.TryCreate(command.OutputProductName, out var outputName))
            {
                return Failure("validation_failed", "A valid output product name is required.", "outputProductName", "A valid output product name is required.");
            }

            newOutputProduct = Product.Create(user.Id, outputName!, timeProvider.GetUtcNow());
            outputProduct = newOutputProduct;
        }

        if (!TryQuantity(command.DeclaredYieldValue, command.DeclaredYieldUnit, command.DeclaredYieldAvailabilityState, out var declaredYield))
        {
            return Failure("validation_failed", "Declared yield must be a positive canonical measured value or availability state.", "declaredYield", "Declared yield must be a positive canonical measured value or availability state.");
        }

        var fromRecipe = command.SourceType == PreparationSourceType.RecipeExecution;
        // Eating everything that was cooked is the common case, so a recipe execution may store no leftovers.
        if (command.Outputs is null || command.Outputs.Count > 32 || (command.Outputs.Count == 0 && !fromRecipe))
        {
            return Failure("validation_failed", "Preparation outputs must contain one to 32 portions.", "outputs", "Preparation outputs must contain one to 32 portions.");
        }

        var now = timeProvider.GetUtcNow();
        if (command.PreparedAt is null || command.PreparedAt > now)
        {
            return Failure("validation_failed", "preparedAt is required and cannot be in the future.", "preparedAt", "preparedAt is required and cannot be in the future.");
        }

        var inputById = loadedInputs.ToDictionary(item => item.Lot.Id);
        var validatedInputs = new List<(PreparationInputState State, decimal ConsumedValue)>(command.Inputs.Count);
        foreach (var input in command.Inputs)
        {
            if (!input.Precondition.IsPresent)
            {
                return Failure("precondition_required", "Every preparation input requires its own current version.", "inputs", "Every preparation input requires its own current version.");
            }

            if (!input.Precondition.IsValid || inputById[input.LotId].Lot.ConcurrencyToken != input.Precondition.Token)
            {
                // A concurrent same-key winner updates the parent and persists the replay record
                // atomically. Re-read before reporting a stale input so duplicate delivery gets
                // the winner's 201 representation, while a different key still receives 412.
                var replay = await store.FindIdempotencyAsync(user.Id, command.IdempotencyKey.Value, cancellationToken);
                return replay is not null
                    ? Replay(replay, hash)
                    : Failure("precondition_failed", "An inventory input was modified.", "inputs", "An inventory input was modified.");
            }

            if (!TryMeasuredConsumption(input.ConsumedValue, input.Unit, out var consumed))
            {
                return Failure("validation_failed", "Input consumption requires a positive measured canonical quantity.", "inputs", "Input consumption requires a positive measured canonical quantity.");
            }

            var state = inputById[input.LotId];
            if (state.Lot.Quantity is not LotQuantity.Measured measured || measured.Unit.ToString() != input.Unit)
            {
                return Failure("domain_rule_violated", "Input consumption must use the parent lot's measured canonical unit.", "inputs", "Input consumption must use the parent lot's measured canonical unit.");
            }

            if (consumed > measured.Value)
            {
                return Failure("domain_rule_violated", "The adjustment cannot exceed the current measured quantity.", "inputs", "The adjustment cannot exceed the current measured quantity.");
            }

            validatedInputs.Add((state, consumed));
        }

        var outputWrites = new List<PreparationOutputWrite>(command.Outputs.Count);
        foreach (var output in command.Outputs)
        {
            if (!TryQuantity(output.MeasuredValue, output.Unit, output.AvailabilityState, out var quantity) || !TryStorage(output.StorageLocation, output.CustomLocation, out var storage) || !TryPackageState(output.PackageState, out var packageState) || !TryShelfLifeEvidence(output, out var evidence))
            {
                return Failure("validation_failed", "An output quantity, storage, package state, or shelf-life evidence is invalid.", "outputs", "An output quantity, storage, package state, or shelf-life evidence is invalid.");
            }

            var lot = InventoryLot.Create(user.Id, outputProduct.Id, quantity!, storage!, packageState, null, null, now, LotLifecycle.Prepared);
            var transaction = InventoryTransaction.Create(lot.Id, user.Id, InventoryTransactionType.PreparationOutputCreated, null, lot.Quantity, ReasonCode(command.SourceType), null, command.IdempotencyKey, now);
            outputWrites.Add(new PreparationOutputWrite(lot, transaction, evidence!));
        }

        if (fromRecipe ? !OutputsFitWithinYield(declaredYield!, outputWrites) : !OutputPartitionsYield(declaredYield!, outputWrites))
        {
            return fromRecipe
                ? Failure("domain_rule_violated", "Leftover portions cannot exceed the portions cooked and must use the same quantity mode and unit.", "outputs", "Leftover portions cannot exceed the portions cooked and must use the same quantity mode and unit.")
                : Failure("domain_rule_violated", "Output portions must exactly partition the declared yield using the same quantity mode and unit.", "outputs", "Output portions must exactly partition the declared yield using the same quantity mode and unit.");
        }

        // Complete all validation before mutating detached aggregates. This makes command failure
        // side-effect free for in-process callers as well as for the later PostgreSQL transaction.
        var inputWrites = new List<PreparationInputWrite>(validatedInputs.Count);
        foreach (var input in validatedInputs)
        {
            var expectedVersion = input.State.Lot.Version;
            var transaction = input.State.Lot.AdjustMeasured(InventoryTransactionType.PreparationInputConsumed, input.ConsumedValue, ReasonCode(command.SourceType), null, command.IdempotencyKey, now);
            inputWrites.Add(new PreparationInputWrite(input.State.Lot, input.State.Product, expectedVersion, input.ConsumedValue, transaction));
        }

        PreparationBatch batch;
        try
        {
            batch = PreparationBatch.Create(user.Id, outputProduct.Id, command.PreparedAt.Value, now, command.SourceType, command.SourceReferenceId);
        }
        catch (ArgumentException exception)
        {
            return Failure("validation_failed", exception.Message, "preparedAt", exception.Message);
        }

        var response = ToView(batch, outputProduct, declaredYield!, inputWrites, outputWrites);
        var write = new PreparationWrite(user.Id, batch, outputProduct, newOutputProduct, ToQuantity(declaredYield!), inputWrites, outputWrites, new PreparationIdempotencyWrite(command.IdempotencyKey.Value, hash, response, now), command.CorrelationId);
        var outcome = await store.SaveAsync(write, cancellationToken);
        if (outcome == PreparationWriteOutcome.Saved)
        {
            return InventoryApplicationResult<PreparationBatchView>.Succeeded(response, InventoryApplicationSuccess.Created, InventoryIdempotencyDisposition.Completed);
        }

        if (outcome == PreparationWriteOutcome.IdempotencyConflict)
        {
            return Replay(await store.FindIdempotencyAsync(user.Id, command.IdempotencyKey.Value, cancellationToken), hash);
        }

        // A same-key winner can advance a parent before this attempt reaches the persistence
        // boundary. Re-read its owner-scoped record so a retry receives the original result.
        var concurrentRecord = await store.FindIdempotencyAsync(user.Id, command.IdempotencyKey.Value, cancellationToken);
        return concurrentRecord is not null ? Replay(concurrentRecord, hash) : Failure("precondition_failed", "An inventory input was modified.");
    }

    /// <inheritdoc />
    public async Task<InventoryApplicationResult<PreparationBatchView>> GetAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var result = await store.GetAsync(user.Id, batchId, cancellationToken);
        return result is null ? Failure("resource_not_found", "The preparation was not found.") : InventoryApplicationResult<PreparationBatchView>.Succeeded(result);
    }

    /// <inheritdoc />
    public async Task<InventoryApplicationResult<InventoryLotProvenanceView>> GetLotProvenanceAsync(Guid lotId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var result = await store.GetLotProvenanceAsync(user.Id, lotId, cancellationToken);
        return result is null
            ? InventoryApplicationResult<InventoryLotProvenanceView>.Failure("resource_not_found", "The inventory resource was not found.")
            : InventoryApplicationResult<InventoryLotProvenanceView>.Succeeded(result);
    }

    private static InventoryApplicationResult<PreparationBatchView> Replay(PreparationIdempotencyRead? record, string hash)
    {
        if (record is null || record.CompletedAt is null || record.Response is null)
        {
            return Failure("idempotency_in_progress", "The request is still being processed.");
        }

        return !string.Equals(record.RequestHash, hash, StringComparison.Ordinal)
            ? Failure("idempotency_key_reused", "The Idempotency-Key was used for a different request.")
            : InventoryApplicationResult<PreparationBatchView>.Succeeded(record.Response, InventoryApplicationSuccess.Created, InventoryIdempotencyDisposition.Replayed);
    }

    private static PreparationBatchView ToView(PreparationBatch batch, Product product, LotQuantity declaredYield, IReadOnlyList<PreparationInputWrite> inputs, IReadOnlyList<PreparationOutputWrite> outputs) =>
        new(batch.Id, batch.SourceType.ToString(), product.Id, product.DisplayName, ToQuantity(declaredYield), batch.PreparedAt,
            inputs.Select(item => new PreparationInputView(item.Lot.Id, new InventoryQuantity(item.ConsumedValue, ((LotQuantity.Measured)item.Transaction.PreviousQuantity!).Unit.ToString(), null))).ToList(),
            outputs.Select(item => new PreparationOutputView(ToLotView(item.Lot, product), new PreparedLotMetadataView(batch.Id, PreparedComponentLifecycleState.Prepared.ToString(), batch.PreparedAt, item.ShelfLifeEvidence.Date, item.ShelfLifeEvidence.Source.ToString(), item.ShelfLifeEvidence.Confidence.ToString(), item.ShelfLifeEvidence.Conditions))).ToList(), batch.CreatedAt);

    private static InventoryLotView ToLotView(InventoryLot lot, Product product) => new(lot.Id, lot.ProductId, product.DisplayName, ToQuantity(lot.Quantity), lot.Storage.Location.ToString(), lot.Storage.CustomLocation, lot.PackageState?.ToString(), lot.PrintedExpiration?.Date, lot.Notes?.Value, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt);
    private static InventoryQuantity ToQuantity(LotQuantity quantity) => new(quantity is LotQuantity.Measured measured ? measured.Value : null, quantity is LotQuantity.Measured measuredUnit ? measuredUnit.Unit.ToString() : null, quantity is LotQuantity.Availability availability ? availability.State.ToString() : null);
    private static bool TryMeasuredConsumption(decimal? value, string? unit, out decimal result) { result = 0m; return value is > 0m and { } measured && measured <= LotQuantity.MaximumMeasuredValue && decimal.Round(measured, 3) == measured && unit is "Gram" or "Milliliter" or "Unit" && (result = measured) > 0m; }
    private static bool TryQuantity(decimal? value, string? unit, string? availability, out LotQuantity? quantity)
    {
        quantity = null;
        if (value is > 0m and { } measured && decimal.Round(measured, 3) == measured && measured <= LotQuantity.MaximumMeasuredValue && unit is "Gram" or "Milliliter" or "Unit" && availability is null)
        {
            quantity = new LotQuantity.Measured(measured, Enum.Parse<CanonicalUnit>(unit));
            return true;
        }
        if (value is null && unit is null && availability is "Available" or "Low" or "Unavailable")
        {
            quantity = LotQuantity.FromAvailability(Enum.Parse<AvailabilityState>(availability));
            return true;
        }
        return false;
    }
    private static bool TryStorage(string? location, string? custom, out LotStorage? storage)
    {
        storage = null;
        return Enum.TryParse<StorageLocation>(location, out var parsed) && Enum.IsDefined(parsed) && LotStorage.TryCreate(parsed, custom, out storage);
    }
    private static bool TryPackageState(string? value, out PackageState? state) { state = null; return value is null || Enum.TryParse(value, out PackageState parsed) && Enum.IsDefined(parsed) && (state = parsed) == parsed; }
    private static bool TryShelfLifeEvidence(PreparationOutputCommand output, out PreparedShelfLifeEvidence? evidence)
    {
        evidence = null;
        if (!Enum.TryParse<PreparedShelfLifeEvidenceSource>(output.ShelfLifeSource ?? "Unknown", out var source) || !Enum.TryParse<ShelfLifeEvidenceConfidence>(output.ShelfLifeConfidence ?? "Unknown", out var confidence))
        {
            return false;
        }

        try { evidence = new PreparedShelfLifeEvidence(output.ShelfLifeDate, source, confidence, output.ShelfLifeConditions); return true; } catch (ArgumentException) { return false; }
    }
    private static string ReasonCode(PreparationSourceType source) => source == PreparationSourceType.RecipeExecution ? "recipe_execution" : "preparation";
    private static bool OutputsFitWithinYield(LotQuantity yield, IReadOnlyList<PreparationOutputWrite> outputs) =>
        outputs.Count == 0 || (yield is LotQuantity.Measured measured && outputs.All(item => item.Lot.Quantity is LotQuantity.Measured output && output.Unit == measured.Unit) && outputs.Sum(item => ((LotQuantity.Measured)item.Lot.Quantity).Value) <= measured.Value);
    private static bool OutputPartitionsYield(LotQuantity yield, IReadOnlyList<PreparationOutputWrite> outputs) => yield switch
    {
        LotQuantity.Measured measured => outputs.All(item => item.Lot.Quantity is LotQuantity.Measured output && output.Unit == measured.Unit) && outputs.Sum(item => ((LotQuantity.Measured)item.Lot.Quantity).Value) == measured.Value,
        LotQuantity.Availability availability => outputs.Count == 1 && outputs[0].Lot.Quantity is LotQuantity.Availability output && output.State == availability.State,
        _ => false
    };
    private static InventoryApplicationResult<PreparationBatchView> Failure(string code, string detail, string? field = null, string? error = null) => InventoryApplicationResult<PreparationBatchView>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [error ?? detail] });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

/// <summary>Concrete preparation-command handler.</summary>
public sealed class PrepareComponentsHandler(PreparedComponentApplicationWorkflow workflow) : IPrepareComponentsUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<PreparationBatchView>> PrepareAsync(PrepareComponentsCommand command, CancellationToken cancellationToken) => workflow.PrepareAsync(command, cancellationToken);
}

/// <summary>Concrete preparation-batch query handler.</summary>
public sealed class GetPreparationBatchHandler(PreparedComponentApplicationWorkflow workflow) : IGetPreparationBatchUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<PreparationBatchView>> GetAsync(Guid batchId, CancellationToken cancellationToken) => workflow.GetAsync(batchId, cancellationToken);
}

/// <summary>Concrete owner-scoped lot-provenance query handler.</summary>
public sealed class GetInventoryLotProvenanceHandler(PreparedComponentApplicationWorkflow workflow) : IGetInventoryLotProvenanceUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotProvenanceView>> GetAsync(Guid lotId, CancellationToken cancellationToken) => workflow.GetLotProvenanceAsync(lotId, cancellationToken);
}
