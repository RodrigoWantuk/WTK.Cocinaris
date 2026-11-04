using System.Text.RegularExpressions;

namespace Cocinaris.Modules.Ingredients;

/// <summary>
/// Salt and water: what every home is assumed to have (owner request 2026-11-04, PLAN-0061). They are
/// never reported as unavailable or as something to buy, whatever the recipe declared. A pantry item
/// the person chose to track is still used normally.
/// </summary>
/// <remarks>
/// A name counts only when it is salt or water plus words from a closed list (temperature, grain,
/// measure, "to taste"). Any other word makes it a real ingredient: coconut water, fleur de sel, "salt
/// and pepper", and sparkling water stay on the shopping list.
/// </remarks>
public static partial class HouseholdBasics
{
    private static readonly HashSet<string> Core = new(StringComparer.Ordinal) { "sal", "salt", "agua", "water" };

    private static readonly HashSet<string> Qualifiers = new(StringComparer.Ordinal)
    {
        // Connecting words.
        "a", "o", "e", "de", "do", "da", "of", "the", "to", "al", "la", "el", "para", "for", "por", "um", "uma", "un", "una", "some", "opcional", "optional",
        // "To taste" and small amounts.
        "gosto", "taste", "gusto", "pitada", "pitadas", "pitadinha", "pinch", "punado", "qb", "q", "b",
        // Kinds of table salt.
        "fino", "fina", "refinado", "refinada", "grosso", "grossa", "marinho", "marinha", "mesa", "table", "sea", "fine", "coarse", "kosher", "iodado", "iodized", "marina",
        // Tap water and its temperature.
        "morna", "quente", "fria", "gelada", "fervente", "filtrada", "potavel", "torneira", "warm", "hot", "cold", "ice", "iced", "lukewarm", "tepid", "filtered", "tap", "tibia", "caliente", "helada", "hirviendo", "natural",
        // Household measures.
        "colher", "colheres", "cha", "sopa", "xicara", "xicaras", "copo", "copos", "cup", "cups", "glass", "tablespoon", "tablespoons", "teaspoon", "teaspoons", "taza", "tazas", "cucharada", "cucharadas", "cucharadita", "cucharaditas", "litro", "litros", "liter", "liters",
        // What it is for.
        "cozinhar", "cozimento", "temperar", "cooking", "boiling", "seasoning", "cocinar", "hervir", "sazonar"
    };

    /// <summary>Whether an ingredient name is plain salt or plain water.</summary>
    /// <param name="name">Name as written, in any supported language.</param>
    public static bool Is(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var words = Word().Matches(IngredientText.Normalize(name)).Select(match => match.Value).ToList();
        return words.Count(Core.Contains) == 1 && words.All(word => Core.Contains(word) || Qualifiers.Contains(word));
    }

    [GeneratedRegex(@"\p{L}+", RegexOptions.None, 100)]
    private static partial Regex Word();
}
