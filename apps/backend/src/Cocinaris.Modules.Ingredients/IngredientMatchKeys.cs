namespace Cocinaris.Modules.Ingredients;

/// <summary>
/// Matching keys for one set of names (PLAN-0055): a name resolved to a catalog ingredient gets
/// that ingredient's key, so "peito de frango" in the pantry matches "chicken breast" in a recipe.
/// Any other name keeps its normalized text, which is exactly how matching worked before the catalog.
/// </summary>
/// <remarks>
/// Keys never cross units: callers still compare the unit separately. A resolution only adds
/// matches between names that mean the same ingredient; it never removes a match two identical
/// names already had, because identical names normalize to one alias.
/// </remarks>
public sealed class IngredientMatchKeys
{
    private const string IngredientPrefix = "ingredient:";
    private readonly IReadOnlyDictionary<string, string> _slugByNormalizedName;

    /// <summary>Creates keys from resolved names.</summary>
    /// <param name="slugByNormalizedName">Catalog slug by normalized name, for the names that resolved.</param>
    public IngredientMatchKeys(IReadOnlyDictionary<string, string> slugByNormalizedName)
    {
        _slugByNormalizedName = slugByNormalizedName;
    }

    /// <summary>Keys with no resolved names: every name matches by normalized text only.</summary>
    public static IngredientMatchKeys NamesOnly { get; } = new(new Dictionary<string, string>());

    /// <summary>Number of names that resolved to a catalog ingredient.</summary>
    public int ResolvedCount => _slugByNormalizedName.Count;

    /// <summary>Returns the matching key of a name.</summary>
    public string Key(string name)
    {
        var normalized = IngredientText.Normalize(name);
        return _slugByNormalizedName.TryGetValue(normalized, out var slug) ? IngredientPrefix + slug : normalized;
    }
}
