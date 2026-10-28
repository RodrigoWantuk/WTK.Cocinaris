namespace Cocinaris.Api.Inventory;

/// <summary>Represents exactly one inventory quantity mode supplied by an API client.</summary>
public sealed record QuantityRequest(decimal? MeasuredValue, string? Unit, string? AvailabilityState);

/// <summary>Creates one manually entered, user-owned inventory lot.</summary>
public sealed record CreateLotRequest(string ProductName, QuantityRequest Quantity, string StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes);

/// <summary>Corrects mutable lot metadata without accepting any authoritative owner identifier.</summary>
public sealed record UpdateLotRequest(string? ProductName, string StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes);

/// <summary>Records an explicit immutable inventory adjustment.</summary>
public sealed record AdjustmentRequest(string Type, decimal? Value, string? AvailabilityState, string? ReasonCode, string? Note);

/// <summary>Returns the current representation of an inventory lot.</summary>
public sealed record LotResponse(Guid LotId, Guid ProductId, string ProductName, QuantityResponse Quantity, string StorageLocation, string? CustomLocation, string? PackageState, DateOnly? PrintedExpirationDate, string? Notes, string Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Returns a measured or qualitative quantity without mixing the two modes.</summary>
public sealed record QuantityResponse(decimal? MeasuredValue, string? Unit, string? AvailabilityState);

/// <summary>
/// Returns one immutable lifecycle transaction or a safe metadata-correction audit projection.
/// Metadata projections expose only changed field names and never values, notes, tokens, or other
/// private request content.
/// </summary>
public sealed record LotHistoryResponse(Guid EntryId, string Kind, string? Type, QuantityResponse? PreviousQuantity, QuantityResponse? ResultingQuantity, string? ReasonCode, IReadOnlyList<string>? ChangedFields, DateTimeOffset OccurredAt);

/// <summary>Returns a cursor-paginated owner-scoped inventory list.</summary>
public sealed record ListLotsResponse(IReadOnlyList<LotResponse> Items, string? NextCursor);

/// <summary>References either an existing owned product or a new normalized product for all v1 preparation outputs.</summary>
public sealed record PreparationOutputProductRequest(Guid? ProductId, string? ProductName);

/// <summary>Consumes one measured parent lot using its resource-bound opaque current version.</summary>
public sealed record PreparationInputRequest(Guid LotId, QuantityRequest Quantity, string? Version);

/// <summary>Supplies advisory shelf-life evidence for one prepared output without claiming a guarantee.</summary>
public sealed record PreparedShelfLifeEvidenceRequest(DateOnly? Date, string? Source, string? Confidence, string? Conditions);

/// <summary>Stores one portion of a preparation batch's declared yield.</summary>
public sealed record PreparationOutputRequest(QuantityRequest Quantity, string StorageLocation, string? CustomLocation, string? PackageState, PreparedShelfLifeEvidenceRequest? ShelfLifeEvidence);

/// <summary>Creates an authoritative manual preparation transaction with all parent and output lines.</summary>
public sealed record PrepareComponentsRequest(PreparationOutputProductRequest OutputProduct, QuantityRequest DeclaredYield, IReadOnlyList<PreparationInputRequest> Inputs, IReadOnlyList<PreparationOutputRequest> Outputs, DateTimeOffset? PreparedAt);

/// <summary>Returns one consumed input's actual quantity in immutable preparation provenance.</summary>
public sealed record PreparationInputResponse(Guid LotId, QuantityResponse ConsumedQuantity);

/// <summary>Returns prepared-component metadata attached to an authoritative output lot.</summary>
public sealed record PreparedLotMetadataResponse(Guid BatchId, string LifecycleState, DateTimeOffset PreparedAt, DateOnly? ShelfLifeDate, string ShelfLifeSource, string ShelfLifeConfidence, string? ShelfLifeConditions);

/// <summary>Returns one produced lot and its prepared-component metadata.</summary>
public sealed record PreparationOutputResponse(LotResponse Lot, PreparedLotMetadataResponse PreparedMetadata);

/// <summary>Returns an owner-visible preparation batch and its immutable input/output provenance.</summary>
public sealed record PreparationResponse(Guid BatchId, string SourceType, Guid OutputProductId, string OutputProductName, QuantityResponse DeclaredYield, DateTimeOffset PreparedAt, IReadOnlyList<PreparationInputResponse> Inputs, IReadOnlyList<PreparationOutputResponse> Outputs, DateTimeOffset CreatedAt);

/// <summary>
/// Returns preparation batches that consumed or produced one owned lot. Each direction returns at
/// most fifty batches; its truncation flag is true when additional older relationships exist.
/// </summary>
public sealed record LotProvenanceResponse(Guid LotId, IReadOnlyList<PreparationResponse> ConsumedBy, bool ConsumedByTruncated, IReadOnlyList<PreparationResponse> ProducedBy, bool ProducedByTruncated);
