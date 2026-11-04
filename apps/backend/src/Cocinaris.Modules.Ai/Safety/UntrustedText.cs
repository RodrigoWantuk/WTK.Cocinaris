using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cocinaris.Modules.Ai.Safety;

/// <summary>
/// Cleans short free text a person writes for the AI (the suggestion hint, the revision request, and
/// pantry lists; PLAN-0060, PLAN-0046) before it reaches a request envelope. This is one layer of the injection defense, not
/// all of it: the text always travels as a JSON string in a field the policy names as untrusted, it
/// can only influence output that still passes every deterministic validator, and recipes shaped by
/// it never enter the shared idea library.
/// </summary>
public static partial class UntrustedText
{
    /// <summary>
    /// Characters removed because they build the look of a prompt, a data structure, or markup and
    /// have no use in a sentence about food.
    /// </summary>
    private const string Structural = "<>{}[]`\\|";

    /// <summary>
    /// Whether text has a link or markup. Food text has no use for either, so their presence means
    /// the model followed something it should have treated as data.
    /// </summary>
    public static bool ContainsLinkOrMarkup(string text) => LinkOrMarkup().IsMatch(text);

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

    [GeneratedRegex(@"https?://|\bwww\.|<\s*/?\s*[a-z!]", RegexOptions.IgnoreCase)]
    private static partial Regex LinkOrMarkup();
}
