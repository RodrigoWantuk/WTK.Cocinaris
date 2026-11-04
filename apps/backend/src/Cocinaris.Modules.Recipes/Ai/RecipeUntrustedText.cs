using System.Globalization;
using System.Text;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// Cleans short free text a person writes for the AI (the suggestion hint and the revision request,
/// PLAN-0060) before it reaches a request envelope. This is one layer of the injection defense, not
/// all of it: the text always travels as a JSON string in a field the policy names as untrusted, it
/// can only influence output that still passes every deterministic validator, and recipes shaped by
/// it never enter the shared idea library.
/// </summary>
public static class RecipeUntrustedText
{
    /// <summary>
    /// Characters removed because they build the look of a prompt, a data structure, or markup and
    /// have no use in a sentence about food.
    /// </summary>
    private const string Structural = "<>{}[]`\\|";

    /// <summary>
    /// Returns the text compatibility-normalized (full-width and other look-alike forms become
    /// ordinary letters), without control, invisible, or structural characters, with whitespace
    /// collapsed to single spaces and trimmed; null when nothing remains. Accented letters and
    /// ordinary punctuation are kept. The result is never longer than the Unicode-normalized input,
    /// and it is capped at <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="text">Raw text from the request.</param>
    /// <param name="maxLength">Longest result, in UTF-16 code units.</param>
    public static string? Normalize(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        // Lone surrogates make string.Normalize throw, so they are dropped before normalizing.
        foreach (var rune in text.EnumerateRunes())
        {
            builder.Append(rune.Value == Rune.ReplacementChar.Value ? " " : rune.ToString());
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormKC);
        builder.Clear();
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (Structural.Contains(character) || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        if (builder.Length == 0)
        {
            return null;
        }

        var result = builder.ToString();
        if (result.Length <= maxLength)
        {
            return result;
        }

        // Never leaves half of a surrogate pair at the cut.
        var cut = char.IsHighSurrogate(result[maxLength - 1]) ? maxLength - 1 : maxLength;
        return result[..cut].TrimEnd();
    }
}
