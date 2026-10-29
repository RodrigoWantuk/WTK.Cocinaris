using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>
/// Represents a validated inventory adjustment command independent of HTTP and persistence.
/// The command preserves the measured-versus-availability mode boundary before a use case
/// can invoke the authoritative <see cref="InventoryLot"/> transition.
/// </summary>
public sealed record InventoryAdjustmentCommand(
    InventoryTransactionType Type,
    decimal? Value,
    AvailabilityState? AvailabilityState,
    string ReasonCode,
    string? Note)
{
    /// <summary>For a correction, whether the resulting value is the owner's approximation (PLAN-0054).</summary>
    public bool Estimated { get; init; }

    /// <summary>
    /// Validates and normalizes an adjustment received from an adapter.
    /// </summary>
    /// <param name="type">Stable command type supplied by the adapter.</param>
    /// <param name="value">Measured delta or resulting correction quantity, when applicable.</param>
    /// <param name="availabilityState">Resulting qualitative availability state, when applicable.</param>
    /// <param name="reasonCode">Required, non-localized reason identifier retained in immutable history.</param>
    /// <param name="note">Optional private note retained in immutable history.</param>
    /// <param name="command">The normalized command when validation succeeds.</param>
    /// <param name="errors">Field-keyed validation errors when validation fails.</param>
    /// <returns><see langword="true"/> when the command is valid for domain execution.</returns>
    public static bool TryCreate(
        string? type,
        decimal? value,
        string? availabilityState,
        string? reasonCode,
        string? note,
        out InventoryAdjustmentCommand? command,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var validationErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var hasType = Enum.TryParse<InventoryTransactionType>(type, ignoreCase: false, out var transactionType) && transactionType is InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.Correct or InventoryTransactionType.AvailabilityChanged;
        if (!hasType)
        {
            validationErrors["type"] = ["type must be Consume, Discard, Correct, or AvailabilityChanged."];
        }

        var normalizedReasonCode = reasonCode?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReasonCode) || normalizedReasonCode.Length > 100)
        {
            validationErrors["reasonCode"] = ["reasonCode is required and must be at most 100 characters."];
        }

        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalizedNote?.Length > 1000)
        {
            validationErrors["note"] = ["note must be at most 1000 characters."];
        }

        AvailabilityState? parsedAvailabilityState = null;
        if (hasType && transactionType is InventoryTransactionType.Consume or InventoryTransactionType.Discard or InventoryTransactionType.Correct)
        {
            var validValue = value is { } measuredValue && measuredValue <= LotQuantity.MaximumMeasuredValue && decimal.Round(measuredValue, 3) == measuredValue && (transactionType == InventoryTransactionType.Correct ? measuredValue >= 0m : measuredValue > 0m);
            if (!validValue)
            {
                validationErrors["value"] = [transactionType == InventoryTransactionType.Correct ? "value must be a nonnegative decimal with at most three decimal places." : "value must be a positive decimal with at most three decimal places."];
            }

            if (availabilityState is not null)
            {
                validationErrors["availabilityState"] = ["availabilityState is only valid for AvailabilityChanged."];
            }
        }

        if (hasType && transactionType == InventoryTransactionType.AvailabilityChanged)
        {
            if (value is not null)
            {
                validationErrors["value"] = ["value must be omitted for AvailabilityChanged."];
            }

            if (!Enum.TryParse<AvailabilityState>(availabilityState, ignoreCase: false, out var parsedState))
            {
                validationErrors["availabilityState"] = ["availabilityState must be Available, Low, or Unavailable."];
            }
            else
            {
                parsedAvailabilityState = parsedState;
            }
        }

        errors = validationErrors;
        command = validationErrors.Count == 0 ? new InventoryAdjustmentCommand(transactionType, value, parsedAvailabilityState, normalizedReasonCode!, normalizedNote) : null;
        return command is not null;
    }
}
