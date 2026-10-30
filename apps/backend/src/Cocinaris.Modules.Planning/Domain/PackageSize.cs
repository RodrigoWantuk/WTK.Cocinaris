namespace Cocinaris.Modules.Planning.Domain;

/// <summary>
/// The package size the owner buys a shopping item in (PLAN-0029 Phase 4). Cocinaris never assumes
/// a package size: one exists only when the owner confirmed it, so the source is always the owner.
/// It applies to every menu's shopping list, keyed by the line's item key (catalog ingredient or
/// normalized name, and unit), and it is advisory: it changes what the list suggests buying, never
/// the pantry or a menu.
/// </summary>
public sealed class PackageSize
{
    /// <summary>Largest package accepted, in the item's unit (100 kg, 100 L, or 100,000 units).</summary>
    public const decimal MaxSize = 100_000m;

    /// <summary>Longest item key accepted; matches the shopping check key limit.</summary>
    public const int MaxItemKeyLength = 200;

    private PackageSize()
    {
    }

    /// <summary>Gets the owner.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the shopping item key the size applies to.</summary>
    public string ItemKey { get; private init; } = string.Empty;

    /// <summary>Gets the size of one package, in the item key's unit.</summary>
    public decimal Size { get; private init; }

    /// <summary>Gets the UTC instant the owner set the size.</summary>
    public DateTimeOffset UpdatedAt { get; private init; }

    /// <summary>Records the size the owner confirmed.</summary>
    /// <exception cref="ArgumentException">The key is empty or too long, or the size is not in (0, <see cref="MaxSize"/>].</exception>
    public static PackageSize Create(Guid ownerUserId, string itemKey, decimal size, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > MaxItemKeyLength)
        {
            throw new ArgumentException("The item key is required and must be at most 200 characters.", nameof(itemKey));
        }

        if (size is <= 0 or > MaxSize)
        {
            throw new ArgumentException("A package size must be greater than zero and at most 100,000.", nameof(size));
        }

        return new PackageSize { OwnerUserId = ownerUserId, ItemKey = itemKey, Size = size, UpdatedAt = now };
    }

    /// <summary>Restores a persisted size.</summary>
    public static PackageSize Restore(Guid ownerUserId, string itemKey, decimal size, DateTimeOffset updatedAt) =>
        new() { OwnerUserId = ownerUserId, ItemKey = itemKey, Size = size, UpdatedAt = updatedAt };

    /// <summary>
    /// Whole packages that cover <paramref name="toBuy"/> and what is left over after the menu,
    /// in the item's unit. Nothing to buy needs no package.
    /// </summary>
    public (int Count, decimal Remainder) Cover(decimal toBuy)
    {
        if (toBuy <= 0)
        {
            return (0, 0);
        }

        var count = (int)Math.Ceiling(toBuy / Size);
        return (count, count * Size - toBuy);
    }
}
