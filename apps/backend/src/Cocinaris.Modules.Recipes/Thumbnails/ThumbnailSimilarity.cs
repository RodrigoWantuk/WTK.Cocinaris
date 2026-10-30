using System.Text.Json;
using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Modules.Recipes.Thumbnails;

/// <summary>
/// Visual similarity of two canonical dish descriptors (PLAN-0055 Phase 3, owner decision
/// 2026-10-30: score the stored images and reuse the best one at or above a threshold; generate
/// only below it).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Different dish formats never match: a bowl never borrows a plated photo.</item>
/// <item>Visible components become catalog ingredients where the catalog names them ("cooked
/// chicken breast in tomato sauce" gives chicken breast and tomato), so wording does not matter.
/// A component the catalog cannot name keeps its normalized text.</item>
/// <item>Score: 0.70 × components + 0.15 × dominant colors + 0.15 × sauce, texture, and garnish
/// words, each a Jaccard index (two empty sets count as equal).</item>
/// </list>
/// The rule is deterministic and explainable: the score of every reuse is stored with the asset.
/// </remarks>
public static class ThumbnailSimilarity
{
    /// <summary>Longest catalog name, in words, looked for inside a component.</summary>
    private const int MaxAliasWords = 4;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal) { "a", "an", "and", "the", "of", "in", "on", "with", "de", "do", "da", "com", "e", "en", "con", "y", "el", "la" };

    /// <summary>Similarity from 0 to 1 of two canonical descriptor JSON documents.</summary>
    public static double Score(string leftJson, string rightJson, IngredientCatalog catalog)
    {
        var left = Facts.Parse(leftJson, catalog);
        var right = Facts.Parse(rightJson, catalog);
        if (left is null || right is null || left.DishFormat != right.DishFormat)
        {
            return 0;
        }

        return 0.70 * Jaccard(left.Components, right.Components)
            + 0.15 * Jaccard(left.Colors, right.Colors)
            + 0.15 * Jaccard(left.Finish, right.Finish);
    }

    /// <summary>Dish format of a canonical descriptor, or null when it cannot be read.</summary>
    public static string? DishFormat(string json) => Facts.Parse(json, IngredientCatalog.Default)?.DishFormat;

    /// <summary>
    /// The catalog ingredients and leftover component texts of visible components: at each word the
    /// longest catalog name wins and its words are consumed.
    /// </summary>
    public static IReadOnlySet<string> ComponentTokens(IEnumerable<string> components, IngredientCatalog catalog)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            var words = IngredientText.Normalize(component).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var found = false;
            for (var index = 0; index < words.Length;)
            {
                var matched = 0;
                for (var length = Math.Min(MaxAliasWords, words.Length - index); length > 0; length--)
                {
                    if (catalog.Aliases.TryGetValue(string.Join(' ', words, index, length), out var item))
                    {
                        tokens.Add("ingredient:" + item.Slug);
                        matched = length;
                        found = true;
                        break;
                    }
                }

                index += Math.Max(1, matched);
            }

            if (!found && words.Length > 0)
            {
                tokens.Add(string.Join(' ', words));
            }
        }

        return tokens;
    }

    private static double Jaccard(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 1;
        }

        var union = left.Union(right, StringComparer.Ordinal).Count();
        return union == 0 ? 0 : (double)left.Intersect(right, StringComparer.Ordinal).Count() / union;
    }

    private static IReadOnlySet<string> Words(IEnumerable<string> values) =>
        values.SelectMany(value => IngredientText.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(word => word.Length > 2 && !StopWords.Contains(word))
            .ToHashSet(StringComparer.Ordinal);

    private sealed record Facts(string DishFormat, IReadOnlySet<string> Components, IReadOnlySet<string> Colors, IReadOnlySet<string> Finish)
    {
        public static Facts? Parse(string json, IngredientCatalog catalog)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                string Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
                IEnumerable<string> List(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList()
                    : [];
                var format = IngredientText.Normalize(Text("dishFormat"));
                return format.Length == 0
                    ? null
                    : new Facts(
                        format,
                        ComponentTokens(List("visibleComponents"), catalog),
                        Words(List("dominantColors")),
                        Words(List("textureAndDoneness").Concat(List("garnish")).Append(Text("sauceAppearance"))));
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
