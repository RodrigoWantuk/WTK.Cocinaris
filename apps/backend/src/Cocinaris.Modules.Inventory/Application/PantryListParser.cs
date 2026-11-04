using System.Globalization;
using System.Text.RegularExpressions;
using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Inventory.Application;

/// <summary>
/// One line of a typed or pasted pantry list, read into a proposed pantry item (PLAN-0046 Phase 1).
/// Nothing is created from it: the person reviews and edits every line before saving.
/// </summary>
/// <param name="LineNumber">1-based line in the text.</param>
/// <param name="Original">The line as typed (trimmed).</param>
/// <param name="Name">Product name, without the amount.</param>
/// <param name="Quantity">Amount in <paramref name="Unit"/>; null when the line has none.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>; null with no amount.</param>
/// <param name="StorageLocation">Suggested place: <c>Freezer</c> when the line says frozen, <c>Refrigerator</c> for meat, fish, dairy, and eggs, otherwise <c>Pantry</c>.</param>
/// <param name="Recognized">Whether the name is a curated catalog ingredient.</param>
/// <param name="Uncertain">Whether an AI reading was unsure of this item (PLAN-0046), so the person should check it. Always false for the deterministic reader.</param>
public sealed record PantryListLine(int LineNumber, string Original, string Name, decimal? Quantity, string? Unit, string StorageLocation, bool Recognized, bool Uncertain = false);

/// <summary>
/// Reads a pantry list deterministically, with no AI call (PLAN-0046 Phase 1): one item per line,
/// with the amount before or after the name ("2 kg de arroz", "arroz 2kg", "6 ovos", "leite 1 L",
/// "meia dúzia de ovos", "manteiga"). Kilograms and liters become grams and milliliters; a dozen
/// becomes 12 units; a bare number counts units. Lists in Portuguese, English, and Spanish share
/// the same rules.
/// </summary>
/// <remarks>
/// Hostile input is bounded: at most <see cref="MaxLines"/> lines and <see cref="MaxLineLength"/>
/// characters per line (longer lines are cut), and only the regular expressions below run on it,
/// with a timeout. A line that cannot be read keeps its text as the name and no amount, so nothing
/// the person typed is lost.
/// </remarks>
public static partial class PantryListParser
{
    /// <summary>Most lines read from one list.</summary>
    public const int MaxLines = 60;

    /// <summary>Longest line read; the rest of a longer line is ignored.</summary>
    public const int MaxLineLength = 120;

    /// <summary>Longest product name kept.</summary>
    private const int MaxNameLength = 80;

    private static readonly Dictionary<string, decimal> WordNumbers = new(StringComparer.Ordinal)
    {
        ["meio"] = 0.5m,
        ["meia"] = 0.5m,
        ["half"] = 0.5m,
        ["medio"] = 0.5m,
        ["media"] = 0.5m,
        ["um"] = 1,
        ["uma"] = 1,
        ["one"] = 1,
        ["a"] = 1,
        ["an"] = 1,
        ["un"] = 1,
        ["una"] = 1,
        ["uno"] = 1,
        ["dois"] = 2,
        ["duas"] = 2,
        ["two"] = 2,
        ["dos"] = 2,
        ["tres"] = 3,
        ["three"] = 3,
    };

    // Unit word → (canonical unit, factor). Matched on the accent-free lower-case text.
    private static readonly Dictionary<string, (string Unit, decimal Factor)> Units = new(StringComparer.Ordinal)
    {
        ["kg"] = ("Gram", 1000),
        ["kgs"] = ("Gram", 1000),
        ["kilo"] = ("Gram", 1000),
        ["kilos"] = ("Gram", 1000),
        ["quilo"] = ("Gram", 1000),
        ["quilos"] = ("Gram", 1000),
        ["g"] = ("Gram", 1),
        ["gr"] = ("Gram", 1),
        ["grs"] = ("Gram", 1),
        ["grama"] = ("Gram", 1),
        ["gramas"] = ("Gram", 1),
        ["gramos"] = ("Gram", 1),
        ["grams"] = ("Gram", 1),
        ["l"] = ("Milliliter", 1000),
        ["lt"] = ("Milliliter", 1000),
        ["lts"] = ("Milliliter", 1000),
        ["litro"] = ("Milliliter", 1000),
        ["litros"] = ("Milliliter", 1000),
        ["liter"] = ("Milliliter", 1000),
        ["liters"] = ("Milliliter", 1000),
        ["ml"] = ("Milliliter", 1),
        ["mililitros"] = ("Milliliter", 1),
        ["un"] = ("Unit", 1),
        ["und"] = ("Unit", 1),
        ["unid"] = ("Unit", 1),
        ["unidade"] = ("Unit", 1),
        ["unidades"] = ("Unit", 1),
        ["unidad"] = ("Unit", 1),
        ["unidades."] = ("Unit", 1),
        ["x"] = ("Unit", 1),
        ["pcs"] = ("Unit", 1),
        ["duzia"] = ("Unit", 12),
        ["duzias"] = ("Unit", 12),
        ["dozen"] = ("Unit", 12),
        ["docena"] = ("Unit", 12),
    };

    // Measures the pantry does not store (imperial weights, kitchen cups and spoons): a line with
    // one keeps its whole text as the name and no amount, for the person to fix.
    private static readonly HashSet<string> UnsupportedUnits = new(StringComparer.Ordinal)
    {
        "lb", "lbs", "oz", "cup", "cups", "xicara", "xicaras", "taza", "tazas", "colher", "colheres", "cuchara", "cucharas", "tbsp", "tsp",
    };

    private static readonly string[] FrozenWords = ["congelad", "frozen"];

    /// <summary>Reads the list.</summary>
    /// <param name="text">Typed or pasted text, one item per line.</param>
    /// <param name="catalog">Curated ingredients, for recognition and the default place.</param>
    public static IReadOnlyList<PantryListLine> Parse(string? text, IngredientCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<PantryListLine>();
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = Bullet().Replace(raw.Trim(), string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Length > MaxLineLength)
            {
                line = line[..MaxLineLength];
            }

            result.Add(ParseLine(lineNumber, line, catalog));
            if (result.Count == MaxLines)
            {
                break;
            }
        }

        return result;
    }

    private static PantryListLine ParseLine(int lineNumber, string line, IngredientCatalog catalog)
    {
        string name = line;
        decimal? quantity = null;
        string? unit = null;

        var leading = Leading().Match(line);
        var trailing = Trailing().Match(line);
        if (leading.Success && ReadAmount(leading.Groups["q"].Value, leading.Groups["u"].Value) is { } before)
        {
            (quantity, unit, var unitWasName) = before;
            // "2 ovos caipira": the word after the number is the name, not a unit.
            name = unitWasName ? $"{leading.Groups["u"].Value} {leading.Groups["name"].Value}" : leading.Groups["name"].Value;
        }
        else if (trailing.Success && ReadAmount(trailing.Groups["q"].Value, trailing.Groups["u"].Value) is { } after && !after.UnitWasName)
        {
            (quantity, unit, _) = after;
            name = trailing.Groups["name"].Value;
        }

        name = Cleanup(name);
        if (name.Length == 0)
        {
            name = Cleanup(line);
            quantity = null;
            unit = null;
        }

        var item = Recognize(name, catalog);
        var folded = IngredientText.Normalize(line);
        var storage = FrozenWords.Any(word => folded.Contains(word, StringComparison.Ordinal))
            ? "Freezer"
            : item is not null && StoreSections.OfCategory(item.Category) is StoreSections.ButcherFish or StoreSections.DairyEggs
                ? "Refrigerator"
                : "Pantry";
        return new PantryListLine(lineNumber, line, name, quantity, unit, storage, item is not null);
    }

    // The amount (a number or a number word) and its unit, converted to the canonical unit. An
    // unknown unit word means a count of the named thing ("2 ovos"): units, and the word is the name.
    private static (decimal Quantity, string Unit, bool UnitWasName)? ReadAmount(string number, string unitWord)
    {
        var folded = IngredientText.Normalize(number);
        decimal value;
        if (WordNumbers.TryGetValue(folded, out var word))
        {
            value = word;
        }
        else if (!decimal.TryParse(number.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return null;
        }

        if (value <= 0 || value > 100_000)
        {
            return null;
        }

        var unitKey = IngredientText.Normalize(unitWord);
        if (unitKey.Length == 0)
        {
            return (value, "Unit", false);
        }

        if (UnsupportedUnits.Contains(unitKey))
        {
            return null;
        }

        return Units.TryGetValue(unitKey, out var known)
            ? (Math.Round(value * known.Factor, 3), known.Unit, false)
            : WordNumbers.ContainsKey(folded) ? null : (value, "Unit", true);
    }

    // Drops connecting words and punctuation around the name and capitalizes it.
    private static string Cleanup(string name)
    {
        var cleaned = Connector().Replace(name.Trim(), string.Empty).Trim(' ', '-', ':', ',', ';', '.', '(', ')');
        if (cleaned.Length > MaxNameLength)
        {
            cleaned = cleaned[..MaxNameLength].TrimEnd();
        }

        return cleaned.Length == 0 ? cleaned : char.ToUpper(cleaned[0], CultureInfo.InvariantCulture) + cleaned[1..];
    }

    /// <summary>Finds the curated ingredient by exact name, then by the singular (eggs → egg, ovos → ovo, cebollas → cebolla).</summary>
    /// <param name="name">Product name.</param>
    /// <param name="catalog">Curated ingredients.</param>
    /// <returns>The ingredient, or null.</returns>
    public static IngredientCatalogItem? Recognize(string name, IngredientCatalog catalog)
    {
        var key = IngredientText.Normalize(name);
        if (catalog.Aliases.TryGetValue(key, out var item))
        {
            return item;
        }

        foreach (var suffix in new[] { "es", "s" })
        {
            if (key.EndsWith(suffix, StringComparison.Ordinal) && catalog.Aliases.TryGetValue(key[..^suffix.Length], out item))
            {
                return item;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^(?:[-*•·▪]+|\d+[.)](?=\s))\s*", RegexOptions.None, 100)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(?<q>\d+(?:[.,]\d+)?|[A-Za-zÀ-ÿ]+)\s*(?<u>[A-Za-zÀ-ÿ]{1,10}\.?)?\s+(?<name>.+)$", RegexOptions.None, 100)]
    private static partial Regex Leading();

    [GeneratedRegex(@"^(?<name>.+?)[\s:,-]+(?<q>\d+(?:[.,]\d+)?)\s*(?<u>[A-Za-zÀ-ÿ]{1,10}\.?)?$", RegexOptions.None, 100)]
    private static partial Regex Trailing();

    [GeneratedRegex(@"^(?:de|do|da|of|del)\s+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Connector();
}
