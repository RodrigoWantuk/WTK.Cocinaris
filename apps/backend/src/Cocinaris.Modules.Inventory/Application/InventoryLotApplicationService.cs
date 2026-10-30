using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>Represents a measured or qualitative inventory quantity without mixing quantity modes.</summary>
public sealed record InventoryQuantity(decimal? MeasuredValue, string? Unit, string? AvailabilityState, bool Estimated = false);

/// <summary>
/// Application representation of one owner-scoped inventory lot. <see cref="ConcurrencyToken"/>
/// is random, resource-owned state; only an outer HTTP adapter may encode it as an ETag.
/// </summary>
/// <remarks>
/// <see cref="Lifecycle"/> is optional only so that idempotency replays stored before lifecycle
/// support still deserialize; current reads and writes always populate it.
/// </remarks>
public sealed record InventoryLotView(Guid LotId, Guid ProductId, string ProductName, InventoryQuantity Quantity, string StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes, Guid ConcurrencyToken, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, InventoryLifecycleView? Lifecycle = null, DateOnly? AutoRemoveOn = null);

/// <summary>Current lifecycle facts of a lot: state name, most recent transition instants, and parent lot.</summary>
/// <remarks><see cref="PreparedShelfLifeDate"/> is filled on reads of prepared lots; mutation responses leave it null.</remarks>
public sealed record InventoryLifecycleView(string State, DateTimeOffset? OpenedAt, DateTimeOffset? FrozenAt, DateTimeOffset? ThawStartedAt, DateTimeOffset? ThawedAt, Guid? ParentLotId, DateOnly? PreparedShelfLifeDate = null);

/// <summary>Lifecycle facts of a transition history entry, with the related lot of a partial transition.</summary>
public sealed record InventoryTransitionView(string Kind, string PreviousState, string ResultingState, string PreviousStorageLocation, string ResultingStorageLocation, Guid? RelatedLotId);

/// <summary>Application representation of an immutable inventory history entry.</summary>
public sealed record InventoryHistoryEntry(Guid EntryId, string Kind, string? TransactionType, InventoryQuantity? PreviousQuantity, InventoryQuantity? ResultingQuantity, string? ReasonCode, IReadOnlyList<string>? ChangedFields, DateTimeOffset OccurredAt, InventoryTransitionView? Transition = null);

/// <summary>Cursor-paginated application inventory lot list with a trusted persistence position.</summary>
public sealed record InventoryLotList(IReadOnlyList<InventoryLotView> Items, InventoryLotReadCursor? NextCursor);

/// <summary>Describes a stable application error that an outer transport maps to Problem Details.</summary>
public sealed record InventoryApplicationProblem(string ErrorCode, string Detail, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Transport-neutral successful state of an inventory application operation.</summary>
public enum InventoryApplicationSuccess
{
    /// <summary>A read or mutation completed with a representation.</summary>
    Succeeded,
    /// <summary>A new lot was created.</summary>
    Created,
    /// <summary>A deletion completed without a representation.</summary>
    Deleted
}

/// <summary>Describes whether an idempotent command was completed or replayed.</summary>
public enum InventoryIdempotencyDisposition
{
    /// <summary>The operation did not use idempotency.</summary>
    NotApplicable,
    /// <summary>The command completed for the first time.</summary>
    Completed,
    /// <summary>A previously completed semantic command was replayed.</summary>
    Replayed
}

/// <summary>Typed transport-neutral outcome of an inventory application command or query.</summary>
public sealed record InventoryApplicationResult<T>(InventoryApplicationSuccess Success, T? Value, InventoryApplicationProblem? Problem, InventoryIdempotencyDisposition Idempotency)
{
    /// <summary>Creates a successful typed application result.</summary>
    public static InventoryApplicationResult<T> Succeeded(T? value, InventoryApplicationSuccess success = InventoryApplicationSuccess.Succeeded, InventoryIdempotencyDisposition idempotency = InventoryIdempotencyDisposition.NotApplicable) => new(success, value, null, idempotency);

    /// <summary>Creates an unsuccessful typed application result.</summary>
    public static InventoryApplicationResult<T> Failure(string errorCode, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => new(InventoryApplicationSuccess.Succeeded, default, new InventoryApplicationProblem(errorCode, detail, errors), InventoryIdempotencyDisposition.NotApplicable);
}

/// <summary>
/// Transport-neutral create input supplied by an authenticated adapter. <c>Estimated</c> marks the
/// measured value as the owner's approximation (PLAN-0054) and is valid only with a measured value.
/// </summary>
public sealed record CreateInventoryLotCommand(string? ProductName, decimal? MeasuredValue, string? Unit, string? AvailabilityState, string? StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes, Guid? IdempotencyKey, string CorrelationId, bool Estimated = false, DateOnly? AutoRemoveOn = null);

/// <summary>Transport-neutral inventory-list input with a decoded trusted cursor position.</summary>
public sealed record ListInventoryLotsQuery(int? PageSize, string? Status, string? StorageLocation, string? Search, InventoryLotReadCursor? Cursor);

/// <summary>Transport-neutral mutable lot metadata correction input.</summary>
public sealed record UpdateInventoryLotCommand(Guid LotId, string? ProductName, string? StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes, InventoryVersionPrecondition Precondition, string CorrelationId, DateOnly? AutoRemoveOn = null);

/// <summary>
/// Transport-neutral immutable inventory adjustment input. <c>Estimated</c> states, for a
/// correction, whether the resulting value is the owner's approximation; other types reject it.
/// </summary>
public sealed record AdjustInventoryLotCommand(Guid LotId, string? Type, decimal? Value, string? AvailabilityState, string? ReasonCode, string? Note, Guid? IdempotencyKey, InventoryVersionPrecondition Precondition, string CorrelationId, bool Estimated = false);

/// <summary>
/// Transport-neutral lifecycle transition input. A measured value makes the transition partial; its
/// unit must match the lot's unit. A target storage location is required for a move.
/// </summary>
public sealed record TransitionInventoryLotCommand(Guid LotId, string? Kind, decimal? MeasuredValue, string? Unit, string? TargetStorageLocation, string? TargetCustomLocation, Guid? IdempotencyKey, InventoryVersionPrecondition Precondition, string CorrelationId);

/// <summary>Transport-neutral soft-deletion input.</summary>
public sealed record DeleteInventoryLotCommand(Guid LotId, InventoryVersionPrecondition Precondition, string CorrelationId);

/// <summary>Represents the client concurrency precondition after an outer transport decodes it.</summary>
public sealed record InventoryVersionPrecondition(bool IsPresent, bool IsValid, Guid Token)
{
    /// <summary>Creates the missing-header precondition state.</summary>
    public static InventoryVersionPrecondition Missing { get; } = new(false, false, Guid.Empty);

    /// <summary>Creates the invalid-token precondition state.</summary>
    public static InventoryVersionPrecondition Invalid { get; } = new(true, false, Guid.Empty);

    /// <summary>Creates a valid expected-version precondition.</summary>
    public static InventoryVersionPrecondition Valid(Guid token) => new(true, true, token);
}

/// <summary>Executes the owner-scoped create-lot use case.</summary>
public interface ICreateInventoryLotUseCase
{
    /// <summary>Creates one lot and its immutable initial history atomically.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> CreateAsync(CreateInventoryLotCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped lot-list use case.</summary>
public interface IListInventoryLotsUseCase
{
    /// <summary>Returns one owner-scoped cursor page.</summary>
    Task<InventoryApplicationResult<InventoryLotList>> ListAsync(ListInventoryLotsQuery query, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped lot-read use case.</summary>
public interface IGetInventoryLotUseCase
{
    /// <summary>Returns an active owned lot without cross-user disclosure.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> GetAsync(Guid lotId, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped metadata-correction use case.</summary>
public interface IUpdateInventoryLotUseCase
{
    /// <summary>Corrects mutable lot metadata under optimistic concurrency.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> UpdateAsync(UpdateInventoryLotCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped immutable adjustment use case.</summary>
public interface IAdjustInventoryLotUseCase
{
    /// <summary>Applies one idempotent inventory adjustment.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> AdjustAsync(AdjustInventoryLotCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped lifecycle transition use case.</summary>
public interface ITransitionInventoryLotUseCase
{
    /// <summary>Applies one idempotent whole or partial lifecycle transition under optimistic concurrency.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> TransitionAsync(TransitionInventoryLotCommand command, CancellationToken cancellationToken);
}

/// <summary>Lists the lots that need attention, most urgent first (PLAN-0037 Phase 2).</summary>
public interface IGetInventoryAttentionUseCase
{
    /// <summary>Evaluates every active, non-depleted lot of the current user and returns those not in the normal state.</summary>
    /// <param name="today">Current calendar date in the user's time zone.</param>
    /// <param name="timeZone">The user's time zone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<(InventoryLotView Lot, InventoryAttention Attention)>> GetAsync(DateOnly today, TimeZoneInfo timeZone, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped soft-delete use case.</summary>
public interface IDeleteInventoryLotUseCase
{
    /// <summary>Soft-deletes an active lot under optimistic concurrency.</summary>
    Task<InventoryApplicationResult<InventoryLotView>> DeleteAsync(DeleteInventoryLotCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped immutable-history query use case.</summary>
public interface IGetInventoryLotHistoryUseCase
{
    /// <summary>Returns owner-visible immutable history for a lot.</summary>
    Task<InventoryApplicationResult<IReadOnlyList<InventoryHistoryEntry>>> HistoryAsync(Guid lotId, CancellationToken cancellationToken);
}

/// <summary>
/// Module-owned application service for the complete authenticated inventory-lot slice. It owns
/// validation, owner-scoped orchestration, domain transitions, idempotency semantics, and typed
/// results while HTTP adapters retain only request/response and token parsing concerns.
/// </summary>
public sealed class InventoryLotApplicationWorkflow(
    ICurrentUserAccessor currentUser,
    IInventoryLotReadStore readStore,
    IInventoryLotWriteStore writeStore,
    TimeProvider timeProvider,
    InventoryLotLifecycleUseCase lifecycleUseCase)
{
    /// <summary>Lists active, depleted, or deleted lots owned by the current internal user.</summary>
    public async Task<InventoryApplicationResult<InventoryLotList>> ListAsync(ListInventoryLotsQuery query, CancellationToken cancellationToken)
    {
        if (query.PageSize is < 1 or > 100)
        {
            return Failure<InventoryLotList>("validation_failed", "pageSize must be between 1 and 100.", FieldErrors("pageSize", "pageSize must be between 1 and 100."));
        }

        if (query.Status is not null && query.Status is not ("active" or "depleted" or "deleted"))
        {
            return Failure<InventoryLotList>("validation_failed", "status must be active, depleted, or deleted.", FieldErrors("status", "status must be active, depleted, or deleted."));
        }

        if (query.StorageLocation is not null && query.StorageLocation is not ("Pantry" or "Refrigerator" or "Freezer" or "Other"))
        {
            return Failure<InventoryLotList>("validation_failed", "storageLocation is invalid.", FieldErrors("storageLocation", "storageLocation is invalid."));
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var page = await readStore.ListAsync(new InventoryLotReadQuery(user.Id, query.PageSize ?? 25, query.Status, query.StorageLocation, query.Search, query.Cursor), cancellationToken);
        return InventoryApplicationResult<InventoryLotList>.Succeeded(new InventoryLotList(page.Items.Select(ToView).ToList(), page.NextCursor));
    }

    /// <summary>Gets one active lot owned by the current internal user without cross-user disclosure.</summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> GetAsync(Guid lotId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var record = await readStore.FindActiveAsync(user.Id, lotId, cancellationToken);
        return record is null
            ? Failure<InventoryLotView>("resource_not_found", "The inventory lot was not found.")
            : Success(ToView(record));
    }

    /// <summary>Creates a product, lot, initial transaction, audit record, and replay response atomically.</summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> CreateAsync(CreateInventoryLotCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is null)
        {
            return Failure<InventoryLotView>("validation_failed", "A UUID Idempotency-Key header is required.", FieldErrors("Idempotency-Key", "A UUID Idempotency-Key header is required."));
        }

        var validProductName = ValidateProductName(command.ProductName, out var productName);
        var validQuantity = ValidateQuantity(command.MeasuredValue, command.Unit, command.AvailabilityState, out var quantityError) && (!command.Estimated || command.MeasuredValue is not null || (quantityError = "An estimate requires a measured value.") is null);
        var validMetadata = ValidateMetadata(command.StorageLocation, command.CustomLocation, command.PackageState, command.Notes, out var metadataError)
            && ValidateAutoRemoveOn(command.AutoRemoveOn, out metadataError);
        if (!validProductName || !validQuantity || !validMetadata)
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (!validProductName) { errors["productName"] = ["The product name is invalid."]; }
            if (!validQuantity) { errors["quantity"] = [quantityError!]; }
            if (!validMetadata) { errors[MetadataField(metadataError)] = [metadataError!]; }
            return Failure<InventoryLotView>("domain_rule_violated", quantityError ?? metadataError ?? "The product name is invalid.", errors);
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var hash = Hash(new { ProductName = productName, Quantity = new { MeasuredValue = command.MeasuredValue?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + (command.Estimated ? "~" : ""), command.Unit, command.AvailabilityState }, StorageLocation = command.StorageLocation, CustomLocation = command.CustomLocation?.Trim(), command.PackageState, command.PrintedExpirationDate, Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes.Trim(), AutoRemoveOn = command.AutoRemoveOn });
        var prior = await writeStore.FindIdempotencyAsync(user.Id, "inventory.lots.create", command.IdempotencyKey.Value, cancellationToken);
        if (prior is not null)
        {
            return Replay(prior, hash);
        }

        var now = timeProvider.GetUtcNow();
        ProductName.TryCreate(productName, out var domainProductName);
        var domainProduct = Product.Create(user.Id, domainProductName!, now);
        var domainLot = CreateDomainLot(user.Id, domainProduct.Id, command, now);
        var response = ToView(domainLot, domainProduct);
        var initial = InventoryTransaction.Create(domainLot.Id, user.Id, InventoryTransactionType.Initial, null, domainLot.Quantity, null, null, command.IdempotencyKey, now);
        var idempotency = new InventoryIdempotencyWrite(command.IdempotencyKey.Value, "inventory.lots.create", hash, InventoryApplicationSuccess.Created, response, now);
        var outcome = await writeStore.SaveCreatedAsync(new InventoryLotCreationWrite(user.Id, domainProduct, domainLot, initial, command.CorrelationId, idempotency), cancellationToken);
        return outcome == InventoryWriteOutcome.Saved
            ? InventoryApplicationResult<InventoryLotView>.Succeeded(response, InventoryApplicationSuccess.Created, InventoryIdempotencyDisposition.Completed)
            : Replay(await writeStore.FindIdempotencyAsync(user.Id, "inventory.lots.create", command.IdempotencyKey.Value, cancellationToken), hash);
    }

    /// <summary>Corrects mutable metadata for an owner-scoped lot using optimistic concurrency.</summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> UpdateAsync(UpdateInventoryLotCommand command, CancellationToken cancellationToken)
    {
        // Owner scope before body validation or precondition evaluation (nondisclosing 404).
        if (await FailIfNotOwnedActiveAsync(command.LotId, cancellationToken) is { } missing)
        {
            return missing;
        }

        var validMetadata = ValidateMetadata(command.StorageLocation, command.CustomLocation, command.PackageState, command.Notes, out var metadataError)
            && ValidateAutoRemoveOn(command.AutoRemoveOn, out metadataError);
        var validProductName = command.ProductName is null || ValidateProductName(command.ProductName, out _);
        if (!validMetadata || !validProductName)
        {
            return Failure<InventoryLotView>("domain_rule_violated", metadataError ?? "The product name is invalid.", FieldErrors(!validMetadata ? MetadataField(metadataError) : "productName", metadataError ?? "The product name is invalid."));
        }

        return await MutateAsync(command.LotId, command.Precondition, command.CorrelationId, null, null, null, "inventory.lot.metadata_corrected", (lot, product) => JsonSerializer.Serialize(new { changedFields = MetadataChangedFields(lot, product, command) }), cancellationToken, (lot, product, now) =>
        {
            lot.UpdateMetadata(ToStorage(command.StorageLocation!, command.CustomLocation), ToPackageState(command.PackageState), ToExpiration(command.PrintedExpirationDate), ToNotes(command.Notes), now, command.AutoRemoveOn);
            if (command.ProductName is not null)
            {
                ProductName.TryCreate(command.ProductName, out var name);
                product.Rename(name!, now);
            }
            return null;
        });
    }

    /// <summary>Applies one idempotent consume, discard, correction, or availability transition.</summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> AdjustAsync(AdjustInventoryLotCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is null)
        {
            return Failure<InventoryLotView>("validation_failed", "A UUID Idempotency-Key header is required.", FieldErrors("Idempotency-Key", "A UUID Idempotency-Key header is required."));
        }

        // Owner scope before adjustment-body validation or precondition evaluation (nondisclosing 404).
        if (await FailIfNotOwnedActiveAsync(command.LotId, cancellationToken) is { } missing)
        {
            return missing;
        }

        if (!InventoryAdjustmentCommand.TryCreate(command.Type, command.Value, command.AvailabilityState, command.ReasonCode, command.Note, out var adjustment, out var errors))
        {
            return Failure<InventoryLotView>("domain_rule_violated", errors.First().Value[0], errors);
        }

        if (command.Estimated && adjustment!.Type != InventoryTransactionType.Correct)
        {
            return Failure<InventoryLotView>("domain_rule_violated", "estimated is only valid for Correct.", FieldErrors("estimated", "estimated is only valid for Correct."));
        }

        adjustment = adjustment! with { Estimated = command.Estimated };
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        // The "~" marks an estimate without changing the hash of exact requests recorded before it existed.
        var hash = Hash(new { command.LotId, adjustment.Type, Value = adjustment.Value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + (adjustment.Estimated ? "~" : ""), adjustment.AvailabilityState, adjustment.ReasonCode, adjustment.Note });
        var prior = await writeStore.FindIdempotencyAsync(user.Id, "inventory.lots.adjust", command.IdempotencyKey.Value, cancellationToken);
        if (prior is not null)
        {
            return Replay(prior, hash);
        }

        return await MutateAsync(command.LotId, command.Precondition, command.CorrelationId, command.IdempotencyKey, "inventory.lots.adjust", hash, "inventory.lot.adjusted", static (_, _) => "{}", cancellationToken, (lot, _, now) => lifecycleUseCase.ApplyAdjustment(lot, adjustment, command.IdempotencyKey.Value, now));
    }

    /// <summary>
    /// Applies one idempotent lifecycle transition. A partial transition splits the lot: the source
    /// keeps the remainder and the derived lot takes the transition, in one atomic write. The
    /// returned representation is always the source lot.
    /// </summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> TransitionAsync(TransitionInventoryLotCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is null)
        {
            return Failure<InventoryLotView>("validation_failed", "A UUID Idempotency-Key header is required.", FieldErrors("Idempotency-Key", "A UUID Idempotency-Key header is required."));
        }

        // Owner scope before body validation or precondition evaluation (nondisclosing 404).
        if (await FailIfNotOwnedActiveAsync(command.LotId, cancellationToken) is { } missing)
        {
            return missing;
        }

        if (!Enum.TryParse<LotTransitionKind>(command.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind) || int.TryParse(command.Kind, out _))
        {
            return Failure<InventoryLotView>("domain_rule_violated", "kind must be Open, Move, Freeze, StartThaw, or CompleteThaw.", FieldErrors("kind", "kind must be Open, Move, Freeze, StartThaw, or CompleteThaw."));
        }

        LotStorage? target = null;
        if (command.TargetStorageLocation is not null || command.TargetCustomLocation is not null)
        {
            if (!ValidateMetadata(command.TargetStorageLocation, command.TargetCustomLocation, null, null, out _))
            {
                return Failure<InventoryLotView>("domain_rule_violated", "The target storage location is invalid.", FieldErrors("targetStorageLocation", "The target storage location is invalid."));
            }

            target = ToStorage(command.TargetStorageLocation!, command.TargetCustomLocation);
        }

        var partial = command.MeasuredValue is not null || command.Unit is not null;
        if (partial && (command.MeasuredValue is not > 0m || command.Unit is not ("Gram" or "Milliliter" or "Unit")))
        {
            return Failure<InventoryLotView>("domain_rule_violated", "A partial quantity needs a positive value and a canonical unit.", FieldErrors("quantity", "A partial quantity needs a positive value and a canonical unit."));
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var hash = Hash(new { command.LotId, Kind = kind.ToString(), Value = command.MeasuredValue?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), command.Unit, Target = target?.Location.ToString(), TargetCustom = target?.CustomLocation });
        var prior = await writeStore.FindIdempotencyAsync(user.Id, "inventory.lots.transition", command.IdempotencyKey.Value, cancellationToken);
        if (prior is not null)
        {
            return Replay(prior, hash);
        }

        (InventoryLot Lot, InventoryTransaction Transaction)? derived = null;
        var audit = JsonSerializer.Serialize(new { kind = kind.ToString(), partial });
        return await MutateAsync(command.LotId, command.Precondition, command.CorrelationId, command.IdempotencyKey, "inventory.lots.transition", hash, "inventory.lot.transitioned", (_, _) => audit, cancellationToken, (lot, _, now) =>
        {
            if (!partial)
            {
                return lot.Transition(kind, target, command.IdempotencyKey.Value, now);
            }

            if (lot.Quantity is not LotQuantity.Measured measured || measured.Unit.ToString() != command.Unit)
            {
                throw new InvalidOperationException("A partial quantity must use the lot's measured unit.");
            }

            var split = lot.SplitTransition(kind, command.MeasuredValue!.Value, target, command.IdempotencyKey.Value, now);
            derived = (split.Derived, split.DerivedTransaction);
            return split.SourceTransaction;
        }, derivedResult: () => derived);
    }

    /// <summary>Soft-deletes one active owner-scoped lot and records its immutable deletion transition.</summary>
    public async Task<InventoryApplicationResult<InventoryLotView>> DeleteAsync(DeleteInventoryLotCommand command, CancellationToken cancellationToken)
    {
        if (await FailIfNotOwnedActiveAsync(command.LotId, cancellationToken) is { } missing)
        {
            return missing;
        }

        return await MutateAsync(command.LotId, command.Precondition, command.CorrelationId, null, null, null, "inventory.lot.deleted", static (_, _) => "{}", cancellationToken, (lot, _, now) => lifecycleUseCase.Delete(lot, now), InventoryApplicationSuccess.Deleted);
    }

    /// <summary>
    /// Evaluates the attention state of the current user's active lots. The read is bounded by
    /// <see cref="InventoryAttentionCandidateLimit"/> most recently updated lots and returns at most
    /// <see cref="InventoryAttentionResultLimit"/> results, most urgent and earliest-dated first.
    /// </summary>
    public async Task<IReadOnlyList<(InventoryLotView Lot, InventoryAttention Attention)>> AttentionAsync(DateOnly today, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var lots = await readStore.ListActiveAsync(user.Id, InventoryAttentionCandidateLimit, cancellationToken);
        return lots
            .Select(ToView)
            .Select(view => (Lot: view, Attention: InventoryAttentionAdvisor.Evaluate(ToAttentionFacts(view), today, timeZone)))
            .Where(item => item.Attention.State != InventoryAttentionState.Normal)
            .OrderBy(item => item.Attention.State)
            .ThenBy(item => item.Attention.EstimatedUseBy ?? DateOnly.MaxValue)
            .ThenBy(item => item.Lot.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .Take(InventoryAttentionResultLimit)
            .ToList();
    }

    /// <summary>Maximum lots evaluated by <see cref="AttentionAsync"/>.</summary>
    public const int InventoryAttentionCandidateLimit = 500;

    /// <summary>Maximum attention results returned by <see cref="AttentionAsync"/>.</summary>
    public const int InventoryAttentionResultLimit = 100;

    /// <summary>Projects a lot view onto the facts the attention advisor evaluates.</summary>
    public static InventoryAttentionFacts ToAttentionFacts(InventoryLotView lot) =>
        new(lot.PrintedExpirationDate, lot.Lifecycle?.PreparedShelfLifeDate, lot.StorageLocation, lot.PackageState, lot.Lifecycle?.State ?? "AsAcquired", lot.Lifecycle?.FrozenAt, lot.Lifecycle?.ThawStartedAt, lot.Lifecycle?.ThawedAt,
            Ingredients.IngredientCatalog.Default.KeepsWhenOpened(lot.ProductName));

    /// <summary>Lists immutable transaction history for one owner-scoped lot.</summary>
    public async Task<InventoryApplicationResult<IReadOnlyList<InventoryHistoryEntry>>> HistoryAsync(Guid lotId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var transactions = await readStore.GetHistoryAsync(user.Id, lotId, cancellationToken);
        return transactions is null
            ? Failure<IReadOnlyList<InventoryHistoryEntry>>("resource_not_found", "The inventory lot was not found.")
            : InventoryApplicationResult<IReadOnlyList<InventoryHistoryEntry>>.Succeeded(transactions.Select(item => new InventoryHistoryEntry(item.EntryId, item.Kind, item.TransactionType, ToQuantity(item.PreviousMeasuredValue, item.PreviousMeasuredUnit, item.PreviousAvailabilityState), ToQuantity(item.ResultingMeasuredValue, item.ResultingMeasuredUnit, item.ResultingAvailabilityState), item.ReasonCode, item.ChangedFields, item.OccurredAt, item.Transition)).ToList());
    }

    /// <summary>
    /// Returns a nondisclosing not-found failure when the lot is absent from the caller's ownership scope.
    /// Must run before precondition, validation-detail, and idempotency-conflict responses that would otherwise leak existence.
    /// </summary>
    private async Task<InventoryApplicationResult<InventoryLotView>?> FailIfNotOwnedActiveAsync(Guid lotId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var state = await writeStore.LoadActiveAsync(user.Id, lotId, cancellationToken);
        return state is null
            ? Failure<InventoryLotView>("resource_not_found", "The inventory lot was not found.")
            : null;
    }

    private async Task<InventoryApplicationResult<InventoryLotView>> MutateAsync(Guid lotId, InventoryVersionPrecondition precondition, string correlationId, Guid? idempotencyKey, string? idempotencyScope, string? idempotencyHash, string auditEventName, Func<InventoryLot, Product, string> auditMetadata, CancellationToken cancellationToken, Func<InventoryLot, Product, DateTimeOffset, InventoryTransaction?> operation, InventoryApplicationSuccess success = InventoryApplicationSuccess.Succeeded, Func<(InventoryLot Lot, InventoryTransaction Transaction)?>? derivedResult = null)
    {
        // Ownership-scoped load precedes If-Match evaluation so foreign/missing lots never surface 412/428.
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var state = await writeStore.LoadActiveAsync(user.Id, lotId, cancellationToken);
        if (state is null) { return Failure<InventoryLotView>("resource_not_found", "The inventory lot was not found."); }

        // Same-key replay wins over a stale precondition. The first delivery may have committed
        // between the caller's original ETag read and this load; returning 412 here would turn a
        // safe retry into a false failure even though the authoritative response is available.
        if (idempotencyKey is not null)
        {
            var prior = await writeStore.FindIdempotencyAsync(user.Id, idempotencyScope!, idempotencyKey.Value, cancellationToken);
            if (prior is not null)
            {
                return Replay(prior, idempotencyHash!);
            }
        }

        if (!precondition.IsPresent) { return Failure<InventoryLotView>("precondition_required", "If-Match is required."); }
        if (!precondition.IsValid) { return Failure<InventoryLotView>("precondition_failed", "The inventory lot was modified."); }
        if (state.Lot.ConcurrencyToken != precondition.Token) { return Failure<InventoryLotView>("precondition_failed", "The inventory lot was modified."); }

        try
        {
            var auditMetadataJson = auditMetadata(state.Lot, state.Product);
            if (auditEventName == "inventory.lot.metadata_corrected" && auditMetadataJson == "{\"changedFields\":[]}")
            {
                // A validated identical correction is intentionally a read-like no-op: preserve
                // the current version and immutable history instead of manufacturing an event.
                return InventoryApplicationResult<InventoryLotView>.Succeeded(ToView(state.Lot, state.Product));
            }

            var expectedVersion = state.Lot.Version;
            var now = timeProvider.GetUtcNow();
            var transaction = operation(state.Lot, state.Product, now);
            var response = ToView(state.Lot, state.Product);
            var idempotency = idempotencyKey is null ? null : new InventoryIdempotencyWrite(idempotencyKey.Value, idempotencyScope!, idempotencyHash!, success, response, now);
            var derived = derivedResult?.Invoke();
            var outcome = await writeStore.SaveMutationAsync(new InventoryLotMutationWrite(user.Id, state.Lot, state.Product, expectedVersion, transaction, auditEventName, auditMetadataJson, correlationId, idempotency, derived?.Lot, derived?.Transaction), cancellationToken);
            if (outcome == InventoryWriteOutcome.Saved) { return success == InventoryApplicationSuccess.Deleted ? InventoryApplicationResult<InventoryLotView>.Succeeded(null, InventoryApplicationSuccess.Deleted) : InventoryApplicationResult<InventoryLotView>.Succeeded(response, success, idempotencyKey is null ? InventoryIdempotencyDisposition.NotApplicable : InventoryIdempotencyDisposition.Completed); }
            if (outcome == InventoryWriteOutcome.ConcurrencyConflict)
            {
                if (idempotencyKey is not null)
                {
                    var replay = Replay(await writeStore.FindIdempotencyAsync(user.Id, idempotencyScope!, idempotencyKey.Value, cancellationToken), idempotencyHash!);
                    if (replay.Problem is null || replay.Problem.ErrorCode is "idempotency_key_reused" or "idempotency_in_progress")
                    {
                        return replay;
                    }
                }

                return Failure<InventoryLotView>("precondition_failed", "The inventory lot was modified.");
            }
            return idempotencyKey is null
                ? Failure<InventoryLotView>("persistence_conflict", "The inventory lot could not be persisted.")
                : Replay(await writeStore.FindIdempotencyAsync(user.Id, idempotencyScope!, idempotencyKey.Value, cancellationToken), idempotencyHash!);
        }
        catch (InvalidOperationException exception)
        {
            return Failure<InventoryLotView>("domain_rule_violated", exception.Message);
        }
    }

    private InventoryApplicationResult<InventoryLotView> Replay(InventoryIdempotencyRead? record, string hash)
    {
        if (record is null || record.CompletedAt is null) { return Failure<InventoryLotView>("idempotency_in_progress", "The request is still being processed."); }
        if (!string.Equals(record.RequestHash, hash, StringComparison.Ordinal)) { return Failure<InventoryLotView>("idempotency_key_reused", "The Idempotency-Key was used for a different request."); }
        return record.Response is null || record.Success is null
            ? Failure<InventoryLotView>("idempotency_in_progress", "The request is still being processed.")
            : InventoryApplicationResult<InventoryLotView>.Succeeded(record.Response, record.Success.Value, InventoryIdempotencyDisposition.Replayed);
    }

    private InventoryApplicationResult<InventoryLotView> Success(InventoryLotView view) => InventoryApplicationResult<InventoryLotView>.Succeeded(view);
    private static InventoryLotView ToView(InventoryLot lot, Product product) => new(lot.Id, lot.ProductId, product.DisplayName, new InventoryQuantity(lot.Quantity is LotQuantity.Measured measured ? measured.Value : null, lot.Quantity is LotQuantity.Measured unit ? unit.Unit.ToString() : null, lot.Quantity is LotQuantity.Availability availability ? availability.State.ToString() : null, lot.Quantity is LotQuantity.Measured { IsEstimate: true }), lot.Storage.Location.ToString(), lot.Storage.CustomLocation, lot.PackageState?.ToString(), lot.PrintedExpiration?.Date, lot.Notes?.Value, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt, new InventoryLifecycleView(lot.Lifecycle.State.ToString(), lot.Lifecycle.OpenedAt, lot.Lifecycle.FrozenAt, lot.Lifecycle.ThawStartedAt, lot.Lifecycle.ThawedAt, lot.Lifecycle.ParentLotId), lot.AutoRemoveOn);
    private static InventoryLotView ToView(InventoryLotReadModel lot) => new(lot.LotId, lot.ProductId, lot.ProductName, new InventoryQuantity(lot.MeasuredValue, lot.MeasuredUnit, lot.AvailabilityState, lot.QuantityIsEstimate), lot.StorageLocation, lot.CustomLocation, lot.PackageState, lot.PrintedExpirationDate, lot.Notes, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt, lot.Lifecycle, lot.AutoRemoveOn);
    private static InventoryQuantity? ToQuantity(decimal? measuredValue, string? unit, string? availabilityState) => measuredValue is null && unit is null && availabilityState is null ? null : new InventoryQuantity(measuredValue, unit, availabilityState);
    private static InventoryApplicationResult<T> Failure<T>(string code, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => InventoryApplicationResult<T>.Failure(code, detail, errors);
    private static InventoryLot CreateDomainLot(Guid ownerUserId, Guid productId, CreateInventoryLotCommand command, DateTimeOffset now)
    {
        var quantity = command.MeasuredValue is { } measured ? new LotQuantity.Measured(measured, Enum.Parse<CanonicalUnit>(command.Unit!), command.Estimated) : LotQuantity.FromAvailability(Enum.Parse<AvailabilityState>(command.AvailabilityState!));
        return InventoryLot.Create(ownerUserId, productId, quantity, ToStorage(command.StorageLocation!, command.CustomLocation), ToPackageState(command.PackageState), ToExpiration(command.PrintedExpirationDate), ToNotes(command.Notes), now, autoRemoveOn: command.AutoRemoveOn);
    }

    private static LotStorage ToStorage(string location, string? customLocation) { LotStorage.TryCreate(Enum.Parse<StorageLocation>(location), customLocation, out var storage); return storage!; }
    private static PackageState? ToPackageState(string? packageState) => packageState is null ? null : Enum.Parse<PackageState>(packageState);
    private static PrintedExpiration? ToExpiration(DateOnly? expiration) => expiration is null ? null : new PrintedExpiration(expiration.Value, ExpirationProvenance.UserEntered);
    private static PrivateNotes? ToNotes(string? notes) { PrivateNotes.TryCreate(notes, out var privateNotes); return privateNotes; }
    private static bool ValidateProductName(string? value, out string? productName) { productName = value?.Trim(); return !string.IsNullOrWhiteSpace(productName) && productName.Length <= 160; }
    private static bool ValidateMetadata(string? location, string? custom, string? packageState, string? notes, out string? error)
    {
        var validStorage = location is "Pantry" or "Refrigerator" or "Freezer" && string.IsNullOrWhiteSpace(custom) || location == "Other" && !string.IsNullOrWhiteSpace(custom) && custom.Trim().Length <= 80;
        var validPackage = packageState is null or "Sealed" or "Opened" or "Unknown";
        var validNotes = notes is null || notes.Trim().Length <= 1000;
        error = !validStorage ? "The storage location is invalid." : !validPackage ? "The package state is invalid." : !validNotes ? "Notes must be at most 1000 characters." : null;
        return error is null;
    }

    private static bool ValidateQuantity(decimal? value, string? unit, string? availabilityState, out string? error)
    {
        var measured = value is > 0m and { } measuredValue && measuredValue <= LotQuantity.MaximumMeasuredValue && decimal.Round(measuredValue, 3) == measuredValue && unit is "Gram" or "Milliliter" or "Unit" && availabilityState is null;
        var available = value is null && unit is null && availabilityState is "Available" or "Low" or "Unavailable";
        error = measured || available ? null : "Quantity must be either a positive canonical measured value with a maximum of three decimal places or an availability state.";
        return error is null;
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static IReadOnlyList<string> MetadataChangedFields(InventoryLot lot, Product product, UpdateInventoryLotCommand command)
    {
        var fields = new List<string>();
        var storage = ToStorage(command.StorageLocation!, command.CustomLocation);
        if (lot.Storage.Location != storage.Location) { fields.Add("storageLocation"); }
        if (!string.Equals(lot.Storage.CustomLocation, storage.CustomLocation, StringComparison.Ordinal)) { fields.Add("customLocation"); }
        if (lot.PackageState != ToPackageState(command.PackageState)) { fields.Add("packageState"); }
        if (lot.PrintedExpiration?.Date != command.PrintedExpirationDate) { fields.Add("printedExpirationDate"); }
        if (lot.AutoRemoveOn != command.AutoRemoveOn) { fields.Add("autoRemoveOn"); }
        if (!string.Equals(lot.Notes?.Value, ToNotes(command.Notes)?.Value, StringComparison.Ordinal)) { fields.Add("notes"); }
        if (command.ProductName is not null && !string.Equals(product.DisplayName, command.ProductName.Trim(), StringComparison.Ordinal)) { fields.Add("productName"); }
        return fields;
    }
    private static IReadOnlyDictionary<string, string[]> FieldErrors(string field, string error) => new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [error] };
    private static string MetadataField(string? error) => error?.StartsWith("Notes", StringComparison.Ordinal) == true ? "notes" : error?.StartsWith("The package", StringComparison.Ordinal) == true ? "packageState" : error?.StartsWith("The removal", StringComparison.Ordinal) == true ? "autoRemoveOn" : "storageLocation";

    /// <summary>
    /// An auto-removal date may not lie in the past. A day of slack covers owners ahead of UTC,
    /// whose "today" is already UTC tomorrow; the removal job decides in the owner's calendar.
    /// </summary>
    private bool ValidateAutoRemoveOn(DateOnly? date, out string? error)
    {
        error = date is { } value && value < DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1) ? "The removal date must not be in the past." : null;
        return error is null;
    }
}

/// <summary>Concrete create-lot application handler.</summary>
public sealed class CreateInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : ICreateInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> CreateAsync(CreateInventoryLotCommand command, CancellationToken cancellationToken) => workflow.CreateAsync(command, cancellationToken);
}

/// <summary>Concrete owner-scoped lot-list application handler.</summary>
public sealed class ListInventoryLotsHandler(InventoryLotApplicationWorkflow workflow) : IListInventoryLotsUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotList>> ListAsync(ListInventoryLotsQuery query, CancellationToken cancellationToken) => workflow.ListAsync(query, cancellationToken);
}

/// <summary>Concrete owner-scoped lot-read application handler.</summary>
public sealed class GetInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : IGetInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> GetAsync(Guid lotId, CancellationToken cancellationToken) => workflow.GetAsync(lotId, cancellationToken);
}

/// <summary>Concrete metadata-correction application handler.</summary>
public sealed class UpdateInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : IUpdateInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> UpdateAsync(UpdateInventoryLotCommand command, CancellationToken cancellationToken) => workflow.UpdateAsync(command, cancellationToken);
}

/// <summary>Concrete immutable adjustment application handler.</summary>
public sealed class AdjustInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : IAdjustInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> AdjustAsync(AdjustInventoryLotCommand command, CancellationToken cancellationToken) => workflow.AdjustAsync(command, cancellationToken);
}

/// <summary>Concrete lifecycle transition application handler.</summary>
public sealed class TransitionInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : ITransitionInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> TransitionAsync(TransitionInventoryLotCommand command, CancellationToken cancellationToken) => workflow.TransitionAsync(command, cancellationToken);
}

/// <summary>Concrete attention application handler.</summary>
public sealed class GetInventoryAttentionHandler(InventoryLotApplicationWorkflow workflow) : IGetInventoryAttentionUseCase
{
    /// <inheritdoc />
    public Task<IReadOnlyList<(InventoryLotView Lot, InventoryAttention Attention)>> GetAsync(DateOnly today, TimeZoneInfo timeZone, CancellationToken cancellationToken) => workflow.AttentionAsync(today, timeZone, cancellationToken);
}

/// <summary>Concrete soft-delete application handler.</summary>
public sealed class DeleteInventoryLotHandler(InventoryLotApplicationWorkflow workflow) : IDeleteInventoryLotUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<InventoryLotView>> DeleteAsync(DeleteInventoryLotCommand command, CancellationToken cancellationToken) => workflow.DeleteAsync(command, cancellationToken);
}

/// <summary>Concrete immutable-history application handler.</summary>
public sealed class GetInventoryLotHistoryHandler(InventoryLotApplicationWorkflow workflow) : IGetInventoryLotHistoryUseCase
{
    /// <inheritdoc />
    public Task<InventoryApplicationResult<IReadOnlyList<InventoryHistoryEntry>>> HistoryAsync(Guid lotId, CancellationToken cancellationToken) => workflow.HistoryAsync(lotId, cancellationToken);
}
