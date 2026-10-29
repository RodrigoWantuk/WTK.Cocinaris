using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Cocinaris.Modules.Inventory.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Cocinaris.Api.Inventory;

/// <summary>
/// HTTP-only adapter for Inventory use cases. It owns all opaque HTTP token parsing and formatting;
/// the Inventory module accepts only decoded versions and cursor positions.
/// </summary>
public sealed class InventoryApplicationService(
    ICreateInventoryLotUseCase createLot,
    IListInventoryLotsUseCase listLots,
    IGetInventoryLotUseCase getLot,
    IUpdateInventoryLotUseCase updateLot,
    IAdjustInventoryLotUseCase adjustLot,
    ITransitionInventoryLotUseCase transitionLot,
    IDeleteInventoryLotUseCase deleteLot,
    IGetInventoryLotHistoryUseCase getHistory,
    IPrepareComponentsUseCase prepareComponents,
    IGetPreparationBatchUseCase getPreparation,
    IGetInventoryLotProvenanceUseCase getProvenance,
    IGetInventoryAttentionUseCase attention,
    InventoryCalendar calendar,
    IInventoryHttpTokenService tokens,
    InventoryMetrics metrics)
{
    /// <summary>Maps the attention query, evaluated in the user's calendar, to its transport result.</summary>
    public async Task<IResult> AttentionAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var items = await attention.GetAsync(calendar.Today, calendar.Zone, cancellationToken);
        return Results.Json(new AttentionListResponse(items.Select(item => ToResponse(item.Lot, tokens.WriteVersion(item.Lot.LotId, item.Lot.ConcurrencyToken), item.Attention)).ToList()));
    }

    /// <summary>Maps the lot-list query and its optional opaque cursor to a transport result.</summary>
    public async Task<IResult> ListAsync(int? pageSize, string? status, string? storageLocation, string? search, string? cursor, HttpContext context, CancellationToken cancellationToken)
    {
        if (!tokens.TryReadCursor(cursor, out var position))
        {
            return Problem("invalid_cursor", "The cursor is invalid.", StatusCodes.Status400BadRequest, context.TraceIdentifier);
        }

        return ToResult(await listLots.ListAsync(new ListInventoryLotsQuery(pageSize, status, storageLocation, search, position), cancellationToken), page => new ListLotsResponse(page.Items.Select(ToResponse).ToList(), page.NextCursor is null ? null : tokens.WriteCursor(page.NextCursor)), context.TraceIdentifier);
    }

    /// <summary>Maps the lot-read query to a transport result.</summary>
    public async Task<IResult> GetAsync(Guid lotId, HttpContext context, CancellationToken cancellationToken) =>
        ToLotResult("get", await getLot.GetAsync(lotId, cancellationToken), context.TraceIdentifier);

    /// <summary>Maps a create-lot HTTP DTO and idempotency header to the application command.</summary>
    public async Task<IResult> CreateAsync(CreateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        var key = Guid.TryParse(requestContext.Headers["Idempotency-Key"], out var parsed) ? parsed : (Guid?)null;
        var command = new CreateInventoryLotCommand(request.ProductName, request.Quantity?.MeasuredValue, request.Quantity?.Unit, request.Quantity?.AvailabilityState, request.StorageLocation, request.CustomLocation, request.PackageState, request.PrintedExpirationDate, request.Notes, key, requestContext.HttpContext.TraceIdentifier, request.Quantity?.Estimated == true);
        return ToLotResult("create", await createLot.CreateAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps a metadata-correction DTO and decoded ETag precondition to the application command.</summary>
    public async Task<IResult> UpdateAsync(Guid lotId, UpdateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        var command = new UpdateInventoryLotCommand(lotId, request.ProductName, request.StorageLocation, request.CustomLocation, request.PackageState, request.PrintedExpirationDate, request.Notes, ReadPrecondition(lotId, requestContext), requestContext.HttpContext.TraceIdentifier);
        return ToLotResult("metadata_update", await updateLot.UpdateAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps an adjustment DTO, idempotency header, and decoded ETag precondition to the application command.</summary>
    public async Task<IResult> AdjustAsync(Guid lotId, AdjustmentRequest request, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        var key = Guid.TryParse(requestContext.Headers["Idempotency-Key"], out var parsed) ? parsed : (Guid?)null;
        var command = new AdjustInventoryLotCommand(lotId, request.Type, request.Value, request.AvailabilityState, request.ReasonCode, request.Note, key, ReadPrecondition(lotId, requestContext), requestContext.HttpContext.TraceIdentifier, request.Estimated == true);
        return ToLotResult("adjust", await adjustLot.AdjustAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps a transition DTO, idempotency header, and decoded ETag precondition to the application command.</summary>
    public async Task<IResult> TransitionAsync(Guid lotId, TransitionRequest request, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        var key = Guid.TryParse(requestContext.Headers["Idempotency-Key"], out var parsed) ? parsed : (Guid?)null;
        var command = new TransitionInventoryLotCommand(lotId, request.Kind, request.Quantity?.MeasuredValue, request.Quantity?.Unit, request.TargetStorageLocation, request.TargetCustomLocation, key, ReadPrecondition(lotId, requestContext), requestContext.HttpContext.TraceIdentifier);
        return ToLotResult("transition", await transitionLot.TransitionAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps a delete request and decoded ETag precondition to the application command.</summary>
    public async Task<IResult> DeleteAsync(Guid lotId, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        var command = new DeleteInventoryLotCommand(lotId, ReadPrecondition(lotId, requestContext), requestContext.HttpContext.TraceIdentifier);
        return ToLotResult("delete", await deleteLot.DeleteAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps immutable lot history to its API DTO.</summary>
    public async Task<IResult> HistoryAsync(Guid lotId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await getHistory.HistoryAsync(lotId, cancellationToken), items => (IReadOnlyList<LotHistoryResponse>)items.Select(item => new LotHistoryResponse(item.EntryId, item.Kind, item.TransactionType, ToQuantity(item.PreviousQuantity), ToQuantity(item.ResultingQuantity), item.ReasonCode, item.ChangedFields, item.OccurredAt, item.Transition is null ? null : new LotTransitionResponse(item.Transition.Kind, item.Transition.PreviousState, item.Transition.ResultingState, item.Transition.PreviousStorageLocation, item.Transition.ResultingStorageLocation, item.Transition.RelatedLotId))).ToList(), context.TraceIdentifier);

    /// <summary>Maps a multi-parent preparation DTO and each resource-bound input version to the application contract.</summary>
    public async Task<IResult> PrepareAsync(PrepareComponentsRequest request, HttpRequest requestContext, CancellationToken cancellationToken)
    {
        if (request is null || request.Inputs is null || request.Inputs.Any(item => item is null))
        {
            return Problem("validation_failed", "Preparation inputs must not contain null items.", StatusCodes.Status400BadRequest, requestContext.HttpContext.TraceIdentifier, new Dictionary<string, string[]> { ["inputs"] = ["Preparation inputs must not contain null items."] });
        }

        if (request.Outputs is null || request.Outputs.Any(item => item is null))
        {
            return Problem("validation_failed", "Preparation outputs must not contain null items.", StatusCodes.Status400BadRequest, requestContext.HttpContext.TraceIdentifier, new Dictionary<string, string[]> { ["outputs"] = ["Preparation outputs must not contain null items."] });
        }

        var key = Guid.TryParse(requestContext.Headers["Idempotency-Key"], out var parsed) ? parsed : (Guid?)null;
        var command = new PrepareComponentsCommand(
            request.OutputProduct?.ProductId,
            request.OutputProduct?.ProductName,
            request.DeclaredYield?.MeasuredValue,
            request.DeclaredYield?.Unit,
            request.DeclaredYield?.AvailabilityState,
            request.Inputs?.Select(item => new PreparationInputCommand(item.LotId, item.Quantity?.MeasuredValue, item.Quantity?.Unit, ReadPreparationPrecondition(item.LotId, item.Version))).ToList(),
            request.Outputs?.Select(item => new PreparationOutputCommand(item.Quantity?.MeasuredValue, item.Quantity?.Unit, item.Quantity?.AvailabilityState, item.StorageLocation, item.CustomLocation, item.PackageState, item.ShelfLifeEvidence?.Date, item.ShelfLifeEvidence?.Source, item.ShelfLifeEvidence?.Confidence, item.ShelfLifeEvidence?.Conditions)).ToList(),
            request.PreparedAt,
            key,
            requestContext.HttpContext.TraceIdentifier);
        return ToPreparationResult("prepare", await prepareComponents.PrepareAsync(command, cancellationToken), requestContext.HttpContext.TraceIdentifier);
    }

    /// <summary>Maps an owner-scoped preparation query to its HTTP representation.</summary>
    public async Task<IResult> GetPreparationAsync(Guid batchId, HttpContext context, CancellationToken cancellationToken) =>
        ToPreparationResult("preparation_get", await getPreparation.GetAsync(batchId, cancellationToken), context.TraceIdentifier);

    /// <summary>Maps an owner-scoped lot provenance query to its HTTP representation.</summary>
    public async Task<IResult> ProvenanceAsync(Guid lotId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await getProvenance.GetAsync(lotId, cancellationToken), item => new LotProvenanceResponse(item.LotId, item.ConsumedBy.Select(ToPreparationResponse).ToList(), item.ConsumedByTruncated, item.ProducedBy.Select(ToPreparationResponse).ToList(), item.ProducedByTruncated), context.TraceIdentifier);

    private LotResponse ToResponse(InventoryLotView item) => ToResponse(item, tokens.WriteVersion(item.LotId, item.ConcurrencyToken));
    private LotResponse ToResponse(InventoryLotView item, string version, InventoryAttention? known = null)
    {
        var advice = known ?? InventoryAttentionAdvisor.Evaluate(InventoryLotApplicationWorkflow.ToAttentionFacts(item), calendar.Today, calendar.Zone);
        return new(item.LotId, item.ProductId, item.ProductName, ToQuantity(item.Quantity)!, item.StorageLocation, item.CustomLocation, item.PackageState, item.PrintedExpirationDate, item.Notes, version, item.CreatedAt, item.UpdatedAt, ToLifecycle(item.Lifecycle), new LotAttentionResponse(advice.State.ToString(), advice.EstimatedUseBy, advice.Evidence.ToString(), advice.Reasons));
    }
    // Idempotency replays stored before lifecycle support carry no lifecycle; those lots were AsAcquired.
    private static LotLifecycleResponse ToLifecycle(InventoryLifecycleView? lifecycle) => lifecycle is null ? new LotLifecycleResponse("AsAcquired", null, null, null, null, null) : new LotLifecycleResponse(lifecycle.State, lifecycle.OpenedAt, lifecycle.FrozenAt, lifecycle.ThawStartedAt, lifecycle.ThawedAt, lifecycle.ParentLotId);
    private static QuantityResponse? ToQuantity(InventoryQuantity? quantity) => quantity is null ? null : new QuantityResponse(quantity.MeasuredValue, quantity.Unit, quantity.AvailabilityState, quantity.Estimated);
    private PreparationResponse ToPreparationResponse(PreparationBatchView item) => new(item.BatchId, item.SourceType, item.OutputProductId, item.OutputProductName, ToQuantity(item.DeclaredYield)!, item.PreparedAt, item.Inputs.Select(input => new PreparationInputResponse(input.LotId, ToQuantity(input.ConsumedQuantity)!)).ToList(), item.Outputs.Select(output => new PreparationOutputResponse(ToResponse(output.Lot), new PreparedLotMetadataResponse(output.PreparedMetadata.BatchId, output.PreparedMetadata.LifecycleState, output.PreparedMetadata.PreparedAt, output.PreparedMetadata.ShelfLifeDate, output.PreparedMetadata.ShelfLifeSource, output.PreparedMetadata.ShelfLifeConfidence, output.PreparedMetadata.ShelfLifeConditions))).ToList(), item.CreatedAt);

    private IResult ToLotResult(string operation, InventoryApplicationResult<InventoryLotView> result, string traceId)
    {
        metrics.RecordMutation(operation, result);
        if (result.Problem is not null)
        {
            return Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors);
        }

        if (result.Success == InventoryApplicationSuccess.Deleted)
        {
            return Results.NoContent();
        }

        var version = tokens.WriteVersion(result.Value!.LotId, result.Value.ConcurrencyToken);
        return new EtagResult<LotResponse>(ToResponse(result.Value!, version), Quote(version), StatusFor(result.Success));
    }

    private IResult ToPreparationResult(string operation, InventoryApplicationResult<PreparationBatchView> result, string traceId)
    {
        metrics.RecordMutation(operation, result);
        return result.Problem is not null
            ? Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors)
            : Results.Json(ToPreparationResponse(result.Value!), statusCode: StatusFor(result.Success));
    }

    private static IResult ToResult<TSource, TResponse>(InventoryApplicationResult<TSource> result, Func<TSource, TResponse> map, string traceId) =>
        result.Problem is not null
            ? Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors)
            : Results.Json(map(result.Value!), statusCode: StatusFor(result.Success));

    private InventoryVersionPrecondition ReadPrecondition(Guid lotId, HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return InventoryVersionPrecondition.Missing;
        }

        return tokens.TryReadVersion(lotId, raw.Trim('"'), out var version) ? InventoryVersionPrecondition.Valid(version) : InventoryVersionPrecondition.Invalid;
    }

    private InventoryVersionPrecondition ReadPreparationPrecondition(Guid lotId, string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? InventoryVersionPrecondition.Missing
            : tokens.TryReadVersion(lotId, version.Trim('"'), out var token) ? InventoryVersionPrecondition.Valid(token) : InventoryVersionPrecondition.Invalid;

    private static int StatusFor(InventoryApplicationSuccess success) => success switch
    {
        InventoryApplicationSuccess.Created => StatusCodes.Status201Created,
        InventoryApplicationSuccess.Deleted => StatusCodes.Status204NoContent,
        _ => StatusCodes.Status200OK
    };

    private static int StatusFor(string errorCode) => errorCode switch
    {
        "validation_failed" or "invalid_cursor" => StatusCodes.Status400BadRequest,
        "resource_not_found" => StatusCodes.Status404NotFound,
        "precondition_required" => StatusCodes.Status428PreconditionRequired,
        "precondition_failed" => StatusCodes.Status412PreconditionFailed,
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict
    };

    private static string Quote(string version) => $"\"{version}\"";

    private static IResult Problem(string errorCode, string detail, int statusCode, string traceId, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["errorCode"] = errorCode, ["traceId"] = Activity.Current?.Id ?? traceId };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }

    private sealed class EtagResult<T>(T body, string etag, int statusCode) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.Headers.ETag = etag;
            return httpContext.Response.WriteAsJsonAsync(body);
        }
    }
}

/// <summary>Protects and parses HTTP ETags and cursors at the API transport boundary.</summary>
public interface IInventoryHttpTokenService
{
    /// <summary>Formats an opaque HTTP ETag from a trusted raw concurrency version.</summary>
    string WriteVersion(Guid lotId, Guid concurrencyToken);

    /// <summary>Parses an opaque resource-bound HTTP ETag into a concurrency token.</summary>
    bool TryReadVersion(Guid lotId, string token, out Guid concurrencyToken);

    /// <summary>Formats a tamper-evident HTTP cursor from a trusted persistence sort position.</summary>
    string WriteCursor(InventoryLotReadCursor cursor);

    /// <summary>Parses an optional HTTP cursor into a trusted persistence sort position.</summary>
    bool TryReadCursor(string? cursor, out InventoryLotReadCursor? position);
}

/// <summary>ASP.NET Core Data Protection implementation of the API-only inventory token boundary.</summary>
public sealed class DataProtectionInventoryHttpTokenService(IDataProtectionProvider dataProtection) : IInventoryHttpTokenService
{
    private const string CursorPurpose = "Cocinaris.Inventory.LotCursor.v1";

    /// <inheritdoc />
    public string WriteVersion(Guid lotId, Guid concurrencyToken)
    {
        Span<byte> payload = stackalloc byte[32];
        lotId.TryWriteBytes(payload[..16]);
        concurrencyToken.TryWriteBytes(payload[16..]);
        return WebEncoders.Base64UrlEncode(payload);
    }

    /// <inheritdoc />
    public bool TryReadVersion(Guid lotId, string token, out Guid concurrencyToken)
    {
        concurrencyToken = Guid.Empty;
        try
        {
            var payload = WebEncoders.Base64UrlDecode(token);
            if (payload.Length != 32 || new Guid(payload.AsSpan(0, 16)) != lotId)
            {
                return false;
            }

            concurrencyToken = new Guid(payload.AsSpan(16, 16));
            return concurrencyToken != Guid.Empty;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string WriteCursor(InventoryLotReadCursor cursor) => dataProtection.CreateProtector(CursorPurpose).Protect(JsonSerializer.Serialize(cursor));

    /// <inheritdoc />
    public bool TryReadCursor(string? cursor, out InventoryLotReadCursor? position)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            position = null;
            return true;
        }

        try
        {
            position = JsonSerializer.Deserialize<InventoryLotReadCursor>(dataProtection.CreateProtector(CursorPurpose).Unprotect(cursor));
            return position is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            position = null;
            return false;
        }
    }
}
