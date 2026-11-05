using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Ingredients;

/// <summary>Configuration under <c>Planning:StorageGuidance</c>.</summary>
public sealed class StorageGuidanceOptions
{
    /// <summary>
    /// Gets or sets whether curated storage times may be shown. Off by default: it is turned on only
    /// after the owner approves <c>docs/domain/storage-guidance-review.md</c> (PLAN-0062 gate G2-A).
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>How one kind of raw food is usually stored.</summary>
/// <param name="Location"><c>Pantry</c>, <c>Refrigerator</c>, or <c>Freezer</c>.</param>
/// <param name="FridgeDays">Days it keeps refrigerated from purchase; null when it is not kept by a refrigerator clock.</param>
/// <param name="Freezable">Whether it is worth freezing as bought.</param>
/// <param name="FreezerDays">Days it keeps its quality frozen, if known.</param>
public sealed record StorageRule(string Location, int? FridgeDays, bool Freezable, int? FreezerDays);

/// <summary>
/// Curated storage guidance by catalog ingredient (PLAN-0062). All output is advisory and comes only
/// from this reviewed table, never from model text. Without a rule, or while the flag is off, nothing
/// is claimed: callers show no date and no freezing advice.
/// </summary>
public sealed class StorageGuidance
{
    private static readonly HashSet<string> Locations = new(StringComparer.Ordinal) { "Pantry", "Refrigerator", "Freezer" };
    private readonly IngredientCatalog _catalog;
    private readonly IReadOnlyDictionary<string, StorageRule> _categories;
    private readonly IReadOnlyDictionary<string, StorageRule> _slugs;

    private StorageGuidance(bool enabled, IngredientCatalog catalog, IReadOnlyDictionary<string, StorageRule> categories, IReadOnlyDictionary<string, StorageRule> slugs)
    {
        Enabled = enabled;
        _catalog = catalog;
        _categories = categories;
        _slugs = slugs;
    }

    /// <summary>Gets whether the owner enabled this guidance.</summary>
    public bool Enabled { get; }

    /// <summary>Creates the guidance shipped with this build.</summary>
    /// <param name="options">Configuration.</param>
    public static StorageGuidance Create(StorageGuidanceOptions options)
    {
        const string resource = "Cocinaris.Modules.Ingredients.storage-guidance.json";
        using var stream = typeof(StorageGuidance).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' was not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), options.Enabled, IngredientCatalog.Default);
    }

    /// <summary>Parses the table; fails fast on an unknown slug, category, or location, so a typo cannot change advice silently.</summary>
    /// <param name="json">Table.</param>
    /// <param name="enabled">Whether guidance is active.</param>
    /// <param name="catalog">Catalog the table refers to.</param>
    /// <exception cref="InvalidOperationException">The table is invalid.</exception>
    public static StorageGuidance Parse(string json, bool enabled, IngredientCatalog catalog)
    {
        var document = JsonSerializer.Deserialize<Document>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Storage guidance is empty.");
        var categories = catalog.Items.Select(item => item.Category).ToHashSet(StringComparer.Ordinal);
        foreach (var key in (document.Categories ?? []).Keys.Where(key => !categories.Contains(key)))
        {
            throw new InvalidOperationException($"Storage guidance names unknown category '{key}'.");
        }

        foreach (var key in (document.Slugs ?? []).Keys.Where(key => !catalog.BySlug.ContainsKey(key)))
        {
            throw new InvalidOperationException($"Storage guidance names unknown slug '{key}'.");
        }

        foreach (var rule in (document.Categories ?? []).Values.Concat((document.Slugs ?? []).Values).Where(rule => !Locations.Contains(rule.Location)))
        {
            throw new InvalidOperationException($"Storage guidance names unknown location '{rule.Location}'.");
        }

        return new StorageGuidance(enabled, catalog, document.Categories ?? [], document.Slugs ?? []);
    }

    /// <summary>The rule for a catalog item, or null when the guidance is off or has no rule.</summary>
    /// <param name="item">Catalog item, or null for a name outside the catalog.</param>
    public StorageRule? RuleFor(IngredientCatalogItem? item)
    {
        if (!Enabled || item is null)
        {
            return null;
        }

        return _slugs.TryGetValue(item.Slug, out var slug) ? slug : _categories.GetValueOrDefault(item.Category);
    }

    /// <summary>The rule for a product name that exactly matches a curated catalog name, or null.</summary>
    /// <param name="productName">Product name.</param>
    public StorageRule? RuleForName(string productName) =>
        RuleFor(_catalog.Aliases.GetValueOrDefault(IngredientText.Normalize(productName)));

    private sealed record Document(
        [property: JsonPropertyName("categories")] Dictionary<string, StorageRule>? Categories,
        [property: JsonPropertyName("slugs")] Dictionary<string, StorageRule>? Slugs);
}
