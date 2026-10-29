using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>One ingredient of a stored revision, as the cooking proposal and the shopping projection read it.</summary>
/// <param name="Id">Ingredient identifier within the revision.</param>
/// <param name="Name">Display name.</param>
/// <param name="SourceType"><c>inventory</c>, <c>additional</c>, or <c>assumption</c>.</param>
/// <param name="LotId">Pantry lot the ingredient was taken from, for <c>inventory</c> ingredients.</param>
/// <param name="Quantity">Quantity for the revision's servings, in <paramref name="Unit"/>.</param>
/// <param name="Unit">Recipe unit: <c>g</c>, <c>ml</c>, or <c>unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
public sealed record RecipeIngredient(string Id, string Name, string SourceType, Guid? LotId, decimal Quantity, string Unit, bool Optional);

/// <summary>
/// Reads ingredients from stored revision content and scales them. Shared by the cooking proposal
/// (PLAN-0042) and the menu shopping projection (PLAN-0029) so both compute the same quantities.
/// </summary>
public static class RecipeIngredientReader
{
    /// <summary>Reads the ingredients of revision content; malformed content yields none.</summary>
    public static IEnumerable<RecipeIngredient> Read(string json)
    {
        // The revision was validated before it was saved; this read stays defensive so a malformed
        // row degrades to no ingredients instead of failing the page that reads it.
        JsonDocument document;
        try { document = JsonDocument.Parse(json); } catch (JsonException) { yield break; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("recipe", out var recipe)) { root = recipe; }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ingredients", out var items) || items.ValueKind != JsonValueKind.Array) { yield break; }
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("requiredQuantity", out var quantity) || quantity.ValueKind != JsonValueKind.Number) { continue; }
                Guid? lotId = item.TryGetProperty("inventoryItemId", out var reference) && reference.ValueKind == JsonValueKind.String && Guid.TryParse(reference.GetString(), out var parsed) ? parsed : null;
                yield return new RecipeIngredient(
                    Text(item, "ingredientId") ?? string.Empty,
                    Text(item, "displayName") ?? string.Empty,
                    Text(item, "sourceType") ?? "assumption",
                    lotId,
                    quantity.GetDecimal(),
                    Text(item, "unit") ?? "unit",
                    item.TryGetProperty("optional", out var optional) && optional.ValueKind == JsonValueKind.True);
            }
        }
    }

    /// <summary>Maps a recipe unit to the pantry lot's canonical unit (<c>Gram</c>, <c>Milliliter</c>, <c>Unit</c>).</summary>
    public static string ToLotUnit(string recipeUnit) => recipeUnit switch { "g" => "Gram", "ml" => "Milliliter", _ => "Unit" };

    /// <summary>
    /// Scales a quantity by <paramref name="scale"/> (portions wanted over the revision's portions).
    /// Whole units round up, since half an egg is still an egg used; grams and milliliters round to
    /// whole numbers. The result is never zero.
    /// </summary>
    /// <param name="quantity">Quantity for the revision's servings.</param>
    /// <param name="lotUnit">Canonical unit from <see cref="ToLotUnit"/>.</param>
    /// <param name="scale">Portions wanted divided by the revision's portions.</param>
    public static decimal Scale(decimal quantity, string lotUnit, decimal scale)
    {
        var scaled = lotUnit == "Unit" ? Math.Ceiling(quantity * scale) : Math.Round(quantity * scale, 0, MidpointRounding.AwayFromZero);
        return Math.Max(scaled, lotUnit == "Unit" ? 1 : 0.001m);
    }

    /// <summary>
    /// Normalizes a product name for matching an ingredient to pantry lots: trimmed, lower case,
    /// accents removed, single spaces. Shared by the cooking proposal, the shopping list, and menu
    /// reservations so all three match the same way.
    /// </summary>
    public static string NormalizeName(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
