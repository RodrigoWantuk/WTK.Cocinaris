using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocinaris.Modules.Ingredients;

/// <summary>One canonical ingredient of the curated catalog.</summary>
/// <param name="Id">Permanent identifier; never reused for another ingredient.</param>
/// <param name="Slug">Stable readable key, for example <c>chicken-breast</c>.</param>
/// <param name="Category">Coarse kitchen category, for example <c>poultry</c>.</param>
/// <param name="Names">Names by language code (<c>en</c>, <c>pt</c>, <c>es</c>), first name preferred.</param>
public sealed record IngredientCatalogItem(int Id, string Slug, string Category, IReadOnlyDictionary<string, IReadOnlyList<string>> Names)
{
    /// <summary>
    /// The text embedded for this ingredient: every name in every language, so a name typed in any
    /// of them lands near it. Bounded because the catalog is curated.
    /// </summary>
    public string DocumentText => string.Join("; ", Names.OrderBy(entry => entry.Key, StringComparer.Ordinal).SelectMany(entry => entry.Value));
}

/// <summary>
/// The curated canonical ingredient catalog (PLAN-0055), shipped with the code as an embedded
/// resource. It is project data, not user data. Changing it changes <see cref="Version"/>, which
/// makes the resolution job sync the database copy and re-check names it could not resolve.
/// </summary>
/// <remarks>
/// Loading fails fast when two ingredients share a normalized name. An alias that points at two
/// ingredients would make matching depend on load order, so the catalog must stay unambiguous.
/// </remarks>
public sealed class IngredientCatalog
{
    private static readonly Lazy<IngredientCatalog> Embedded = new(LoadEmbedded);

    private IngredientCatalog(IReadOnlyList<IngredientCatalogItem> items, string version)
    {
        Items = items;
        Version = version;
        ById = items.ToDictionary(item => item.Id);
        var aliases = new Dictionary<string, IngredientCatalogItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            foreach (var name in item.Names.Values.SelectMany(names => names))
            {
                var normalized = IngredientText.Normalize(name);
                if (normalized.Length == 0)
                {
                    continue;
                }

                if (aliases.TryGetValue(normalized, out var existing) && existing.Id != item.Id)
                {
                    throw new InvalidOperationException($"Ingredient catalog alias '{normalized}' belongs to both '{existing.Slug}' and '{item.Slug}'.");
                }

                aliases[normalized] = item;
            }
        }

        Aliases = aliases;
    }

    /// <summary>Gets the catalog shipped with this build.</summary>
    public static IngredientCatalog Default => Embedded.Value;

    /// <summary>Gets every ingredient.</summary>
    public IReadOnlyList<IngredientCatalogItem> Items { get; }

    /// <summary>Gets ingredients by id.</summary>
    public IReadOnlyDictionary<int, IngredientCatalogItem> ById { get; }

    /// <summary>Gets every normalized catalog name and its ingredient.</summary>
    public IReadOnlyDictionary<string, IngredientCatalogItem> Aliases { get; }

    /// <summary>Gets the content version: the schema version and a SHA-256 prefix of the catalog content.</summary>
    public string Version { get; }

    /// <summary>Parses a catalog document; used for the embedded catalog and by tests.</summary>
    public static IngredientCatalog Parse(string json)
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Ingredient catalog is empty.");
        var items = (document.Items ?? [])
            .Select(item => new IngredientCatalogItem(
                item.Id,
                item.Slug ?? throw new InvalidOperationException($"Ingredient {item.Id} has no slug."),
                item.Category ?? "other",
                (item.Names ?? []).ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)entry.Value.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList(), StringComparer.Ordinal)))
            .ToList();
        if (items.Count == 0 || items.Select(item => item.Id).Distinct().Count() != items.Count || items.Select(item => item.Slug).Distinct(StringComparer.Ordinal).Count() != items.Count)
        {
            throw new InvalidOperationException("Ingredient catalog ids and slugs must be present and unique.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
        return new IngredientCatalog(items, $"{document.Version}-{hash}");
    }

    private static IngredientCatalog LoadEmbedded()
    {
        using var stream = typeof(IngredientCatalog).Assembly.GetManifestResourceStream("Cocinaris.Modules.Ingredients.ingredient-catalog.json")
            ?? throw new InvalidOperationException("The ingredient catalog resource is missing from the build.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    private sealed record CatalogDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("items")] List<CatalogDocumentItem>? Items);

    private sealed record CatalogDocumentItem(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("slug")] string? Slug,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("names")] Dictionary<string, List<string>>? Names);
}
