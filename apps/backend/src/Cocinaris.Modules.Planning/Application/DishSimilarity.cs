using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>
/// Whether two suggested dishes are really the same dish (PLAN-0065). Exact names and the model's
/// free-text format let three versions of pão de queijo through. Pure and deterministic.
/// </summary>
public static class DishSimilarity
{
    private const double SameDishOverlap = 0.6;

    // Words that describe a way of cooking or a flavor rather than which dish it is.
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "a", "as", "o", "os", "e", "de", "da", "das", "do", "dos", "com", "sem", "em", "no", "na", "nos", "nas", "ao", "aos", "the", "and", "with", "con", "del", "y", "el", "la",
        "air", "fryer", "frigideira", "forno", "assado", "assada", "assados", "assadas", "frito", "frita", "fritos", "fritas", "grelhado", "grelhada", "cremoso", "cremosa", "crocante", "crocantes",
        "picante", "picantes", "apimentado", "apimentada", "apimentados", "apimentadas", "agridoce", "molho", "calda", "pimenta", "pimentas", "individual", "caseiro", "caseira", "rapido", "rapida",
    };

    /// <summary>True when the names share most of what identifies the dish, or one is contained in the other.</summary>
    /// <param name="first">A dish name.</param>
    /// <param name="second">Another dish name.</param>
    public static bool IsSameDish(string first, string second)
    {
        var a = Tokens(first);
        var b = Tokens(second);
        if (a.Count == 0 || b.Count == 0)
        {
            return false;
        }

        var shared = a.Count(b.Contains);
        // "Pão de queijo" against "Pão de queijo com goiabada": a two-word dish inside a longer name.
        if (shared >= 2 && shared == Math.Min(a.Count, b.Count))
        {
            return true;
        }

        return (double)shared / (a.Count + b.Count - shared) >= SameDishOverlap;
    }

    private static HashSet<string> Tokens(string name) =>
        IngredientText.Normalize(name)
            .Split([' ', '-', ',', '.', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !Ignored.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
}
