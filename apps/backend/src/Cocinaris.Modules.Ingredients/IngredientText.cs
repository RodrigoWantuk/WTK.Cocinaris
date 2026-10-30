using System.Globalization;
using System.Text;

namespace Cocinaris.Modules.Ingredients;

/// <summary>Text rules shared by the catalog, the alias table, and recipe-to-pantry matching.</summary>
public static class IngredientText
{
    /// <summary>Longest normalized name kept; longer names are cut, which never merges two real ingredient names.</summary>
    public const int MaxNormalizedLength = 120;

    /// <summary>Longest exact text sent to the embedding provider or kept for resolution.</summary>
    public const int MaxExactLength = 120;

    /// <summary>
    /// Normalizes a name for matching: trimmed, lower case, accents removed, single spaces. Shared by
    /// the cooking proposal, the shopping list, menu reservations, and the alias table, so all of
    /// them compare names the same way.
    /// </summary>
    public static string Normalize(string name)
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

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);
        return normalized.Length > MaxNormalizedLength ? normalized[..MaxNormalizedLength] : normalized;
    }
}
