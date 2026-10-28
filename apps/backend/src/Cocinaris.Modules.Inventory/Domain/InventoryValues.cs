using System.Text;

namespace Cocinaris.Modules.Inventory.Domain;

/// <summary>Canonical measured units persisted by the inventory module.</summary>
public enum CanonicalUnit
{
    /// <summary>Mass expressed in grams.</summary>
    Gram,
    /// <summary>Volume expressed in milliliters.</summary>
    Milliliter,
    /// <summary>Count expressed as indivisible units.</summary>
    Unit
}

/// <summary>Qualitative availability states for lots without measured quantity.</summary>
public enum AvailabilityState
{
    /// <summary>The lot is available.</summary>
    Available,
    /// <summary>The lot is low.</summary>
    Low,
    /// <summary>The lot is unavailable.</summary>
    Unavailable
}

/// <summary>Supported physical storage locations.</summary>
public enum StorageLocation
{
    /// <summary>Room-temperature pantry storage.</summary>
    Pantry,
    /// <summary>Refrigerated storage.</summary>
    Refrigerator,
    /// <summary>Frozen storage.</summary>
    Freezer,
    /// <summary>User-specified storage requiring a custom label.</summary>
    Other
}

/// <summary>Optional package state supplied by the user.</summary>
public enum PackageState
{
    /// <summary>The package is sealed.</summary>
    Sealed,
    /// <summary>The package has been opened.</summary>
    Opened,
    /// <summary>The package state is unknown.</summary>
    Unknown
}

/// <summary>Source of shelf-life evidence retained by the first inventory slice.</summary>
public enum ExpirationProvenance
{
    /// <summary>The user entered the printed date.</summary>
    UserEntered
}

/// <summary>Immutable inventory lifecycle transaction types.</summary>
public enum InventoryTransactionType
{
    /// <summary>Initial lot creation.</summary>
    Initial,
    /// <summary>Consumption reduction.</summary>
    Consume,
    /// <summary>Discard reduction.</summary>
    Discard,
    /// <summary>Explicit resulting-quantity correction.</summary>
    Correct,
    /// <summary>Qualitative availability change.</summary>
    AvailabilityChanged,
    /// <summary>Soft deletion of an erroneously created lot.</summary>
    Deleted

    ,
    /// <summary>Consumes a measured parent quantity as an input to an authoritative preparation batch.</summary>
    PreparationInputConsumed,
    /// <summary>Creates an output lot produced by an authoritative preparation batch.</summary>
    PreparationOutputCreated
}

/// <summary>Validated product name and normalized search value.</summary>
public sealed record ProductName
{
    private ProductName(string value, string normalizedSearchValue)
    {
        Value = value;
        NormalizedSearchValue = normalizedSearchValue;
    }

    /// <summary>Gets the trimmed user-visible product name.</summary>
    public string Value { get; }

    /// <summary>Gets the deterministic uppercase search representation.</summary>
    public string NormalizedSearchValue { get; }

    /// <summary>Validates and normalizes a user-entered product name.</summary>
    /// <param name="value">Candidate user-entered name.</param>
    /// <param name="productName">Validated value object when the candidate is valid.</param>
    /// <returns><see langword="true"/> when the candidate is nonblank and at most 160 characters.</returns>
    public static bool TryCreate(string? value, out ProductName? productName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.EnumerateRunes().Count() > 160)
        {
            productName = null;
            return false;
        }

        productName = new ProductName(trimmed, trimmed.ToUpperInvariant());
        return true;
    }
}

/// <summary>Represents exactly one lot quantity mode.</summary>
public abstract record LotQuantity
{
    /// <summary>Largest value representable by the PostgreSQL <c>numeric(18,3)</c> contract.</summary>
    public const decimal MaximumMeasuredValue = 999_999_999_999_999.999m;

    private LotQuantity()
    {
    }

    /// <summary>Measured quantity in a canonical unit.</summary>
    public sealed record Measured : LotQuantity
    {
        /// <summary>Creates a persisted nonnegative measured quantity with canonical precision.</summary>
        /// <param name="value">Nonnegative quantity with at most three decimal places.</param>
        /// <param name="unit">Defined canonical unit.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when value, precision, or unit is invalid.</exception>
        public Measured(decimal value, CanonicalUnit unit)
        {
            if (value < 0m || value > MaximumMeasuredValue || decimal.Round(value, 3) != value)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Measured quantity must be nonnegative with at most three decimal places.");
            }

            if (!Enum.IsDefined(unit))
            {
                throw new ArgumentOutOfRangeException(nameof(unit), "Measured quantity requires a canonical unit.");
            }

            Value = value;
            Unit = unit;
        }

        /// <summary>Gets the canonical decimal quantity.</summary>
        public decimal Value { get; }

        /// <summary>Gets the canonical unit.</summary>
        public CanonicalUnit Unit { get; }
    }

    /// <summary>Qualitative availability quantity.</summary>
    public sealed record Availability : LotQuantity
    {
        /// <summary>Creates a qualitative quantity from a defined availability state.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="state"/> is undefined.</exception>
        public Availability(AvailabilityState state)
        {
            if (!Enum.IsDefined(state))
            {
                throw new ArgumentOutOfRangeException(nameof(state), "Availability quantity requires a defined state.");
            }

            State = state;
        }

        /// <summary>Gets the qualitative availability state.</summary>
        public AvailabilityState State { get; }
    }

    /// <summary>Creates a valid positive measured quantity for lot creation.</summary>
    /// <param name="value">Positive decimal value with at most three decimal places.</param>
    /// <param name="unit">Canonical measured unit.</param>
    /// <param name="quantity">Validated quantity when successful.</param>
    /// <returns><see langword="true"/> when the measured quantity is valid.</returns>
    public static bool TryCreateMeasured(decimal value, CanonicalUnit unit, out LotQuantity? quantity)
    {
        if (value <= 0m || value > MaximumMeasuredValue || decimal.Round(value, 3) != value)
        {
            quantity = null;
            return false;
        }

        quantity = new Measured(value, unit);
        return true;
    }

    /// <summary>Creates a qualitative availability quantity.</summary>
    /// <param name="state">Availability state to retain.</param>
    /// <returns>A qualitative quantity.</returns>
    public static LotQuantity FromAvailability(AvailabilityState state) => new Availability(state);
}

/// <summary>Validated storage location and optional custom label.</summary>
public sealed record LotStorage
{
    private LotStorage(StorageLocation location, string? customLocation)
    {
        Location = location;
        CustomLocation = customLocation;
    }

    /// <summary>Gets the controlled storage location.</summary>
    public StorageLocation Location { get; }

    /// <summary>Gets the trimmed custom label required only for <see cref="StorageLocation.Other"/>.</summary>
    public string? CustomLocation { get; }

    /// <summary>Validates and normalizes a storage location.</summary>
    /// <param name="location">Controlled storage location.</param>
    /// <param name="customLocation">Optional user-entered custom label.</param>
    /// <param name="storage">Validated storage when successful.</param>
    /// <returns><see langword="true"/> when the custom-label rule is satisfied.</returns>
    public static bool TryCreate(StorageLocation location, string? customLocation, out LotStorage? storage)
    {
        var trimmed = customLocation?.Trim();
        if (location == StorageLocation.Other && string.IsNullOrWhiteSpace(trimmed))
        {
            storage = null;
            return false;
        }

        if (location != StorageLocation.Other && !string.IsNullOrWhiteSpace(trimmed))
        {
            storage = null;
            return false;
        }

        if (trimmed?.EnumerateRunes().Count() > 80)
        {
            storage = null;
            return false;
        }

        storage = new LotStorage(location, trimmed);
        return true;
    }
}

/// <summary>Private trimmed note retained with a lot or immutable transaction.</summary>
public sealed record PrivateNotes
{
    private PrivateNotes(string value) => Value = value;

    /// <summary>Gets the trimmed private note value.</summary>
    public string Value { get; }

    /// <summary>Validates and normalizes an optional private note.</summary>
    /// <param name="value">Candidate note.</param>
    /// <param name="notes">Normalized note, or <see langword="null"/> for blank input.</param>
    /// <returns><see langword="true"/> when the note is absent or at most 1,000 characters.</returns>
    public static bool TryCreate(string? value, out PrivateNotes? notes)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            notes = null;
            return true;
        }

        if (trimmed.EnumerateRunes().Count() > 1000)
        {
            notes = null;
            return false;
        }

        notes = new PrivateNotes(trimmed);
        return true;
    }
}

/// <summary>Printed calendar expiration date and its user-entered provenance.</summary>
public sealed record PrintedExpiration
{
    /// <summary>Creates printed expiration evidence with a supported provenance.</summary>
    /// <param name="date">Calendar date printed on the package.</param>
    /// <param name="provenance">Defined evidence provenance.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when provenance is undefined.</exception>
    public PrintedExpiration(DateOnly date, ExpirationProvenance provenance)
    {
        if (!Enum.IsDefined(provenance))
        {
            throw new ArgumentOutOfRangeException(nameof(provenance), "Printed expiration requires a defined provenance.");
        }

        Date = date;
        Provenance = provenance;
    }

    /// <summary>Gets the printed calendar date.</summary>
    public DateOnly Date { get; }

    /// <summary>Gets the evidence provenance.</summary>
    public ExpirationProvenance Provenance { get; }
}
