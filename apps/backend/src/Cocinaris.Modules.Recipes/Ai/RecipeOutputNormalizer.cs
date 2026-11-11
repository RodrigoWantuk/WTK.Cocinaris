using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cocinaris.Modules.Recipes.Ai;

/// <summary>
/// Fits informational text of a model recipe to its limits before validation (PLAN-0039 decision R1),
/// so a note one word too long no longer fails a whole recipe. Only the fields in
/// <see cref="Fields"/> are touched: the storage note, sensory cues, and the dish's visual description,
/// which people read or the image model uses. Ingredients, quantities, units, steps, equipment, names,
/// and anything a restriction is checked against are never changed; their slips still go to repair.
/// </summary>
/// <remarks>Deterministic: the same answer always gives the same result. Validation runs afterwards on the result.</remarks>
public static class RecipeOutputNormalizer
{
    /// <summary>One normalizable field: a path under <c>recipe</c>, its character limit, and for a list its item limit.</summary>
    /// <param name="Path">Dot path under <c>recipe</c>; <c>stages[].sensoryCues</c> means every stage.</param>
    /// <param name="MaxCharacters">Longest text, the schema's <c>maxLength</c>.</param>
    /// <param name="MaxItems">For a list, the schema's <c>maxItems</c>; null for a single text.</param>
    /// <param name="MaxWords">A word limit the semantic rules add, if any.</param>
    public sealed record Field(string Path, int MaxCharacters, int? MaxItems = null, int? MaxWords = null);

    /// <summary>Gets the reviewed catalog of fields that may be fitted; nothing else is.</summary>
    public static IReadOnlyList<Field> Fields { get; } =
    [
        new("storage.reheatNotes", 240),
        new("stages[].sensoryCues", 120, MaxItems: 6),
        new("thumbnailVisual.appearanceDescription", 280, MaxWords: RecipeExpandSemantics.MaxAppearanceWords),
        new("thumbnailVisual.plating", 80),
        new("thumbnailVisual.sauceAppearance", 120),
        new("thumbnailVisual.visibleComponents", 64, MaxItems: 12),
        new("thumbnailVisual.textureAndDoneness", 80, MaxItems: 8),
        new("thumbnailVisual.garnish", 64, MaxItems: 6),
        new("thumbnailVisual.dominantColors", 32, MaxItems: 8),
        new("thumbnailVisual.excludedElements", 48, MaxItems: 12),
    ];

    /// <summary>
    /// Returns the answer with every catalog field fitted, and the paths that changed, for the attempt
    /// record. Text that is not JSON, or has no <c>recipe</c> object, is returned unchanged.
    /// </summary>
    /// <param name="raw">The model's answer.</param>
    public static (string Json, IReadOnlyList<string> Applied) NormalizeRecipe(string raw)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return (raw, []);
        }

        if (root?["recipe"] is not JsonObject recipe)
        {
            return (raw, []);
        }

        var applied = new List<string>();
        foreach (var field in Fields)
        {
            foreach (var (parent, name) in Targets(recipe, field.Path))
            {
                if (Fit(parent, name, field))
                {
                    applied.Add(field.Path);
                }
            }
        }

        return applied.Count == 0 ? (raw, []) : (root.ToJsonString(), applied.Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Cuts text to <paramref name="maxCharacters"/> and <paramref name="maxWords"/>: at the last sentence
    /// end in the second half, else at the last space, with an ellipsis. Text within both limits is
    /// returned trimmed.
    /// </summary>
    public static string FitText(string text, int maxCharacters, int? maxWords = null)
    {
        var fitted = text.Trim();
        if (maxWords is { } words)
        {
            var parts = fitted.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > words)
            {
                fitted = string.Join(' ', parts.Take(words)).TrimEnd('.', ',', ';', ':') + "…";
            }
        }

        if (fitted.Length <= maxCharacters)
        {
            return fitted;
        }

        var window = fitted[..maxCharacters];
        var sentenceEnd = window.LastIndexOfAny(['.', '!', '?']);
        if (sentenceEnd >= maxCharacters / 2)
        {
            return window[..(sentenceEnd + 1)];
        }

        var lastSpace = window[..(maxCharacters - 1)].LastIndexOf(' ');
        return (lastSpace > 0 ? window[..lastSpace] : window[..(maxCharacters - 1)]).TrimEnd() + "…";
    }

    // Each (object, property) the path names; "[]" walks every item of a list.
    private static IEnumerable<(JsonObject Parent, string Name)> Targets(JsonObject recipe, string path)
    {
        var segments = path.Split('.');
        IEnumerable<JsonObject> current = [recipe];
        foreach (var segment in segments[..^1])
        {
            current = segment.EndsWith("[]", StringComparison.Ordinal)
                ? current.SelectMany(node => node[segment[..^2]] is JsonArray list ? list.OfType<JsonObject>() : [])
                : current.Select(node => node[segment]).OfType<JsonObject>();
        }

        return current.Select(node => (node, segments[^1]));
    }

    private static bool Fit(JsonObject parent, string name, Field field)
    {
        switch (parent[name])
        {
            case JsonValue value when field.MaxItems is null && value.TryGetValue<string>(out var text):
                {
                    var fitted = FitText(text, field.MaxCharacters, field.MaxWords);
                    if (fitted == text || fitted.Length == 0)
                    {
                        return false;
                    }

                    parent[name] = fitted;
                    return true;
                }

            case JsonArray list when field.MaxItems is { } maxItems:
                {
                    var changed = false;
                    for (var index = 0; index < list.Count; index++)
                    {
                        if (list[index] is JsonValue item && item.TryGetValue<string>(out var text))
                        {
                            var fitted = FitText(text, field.MaxCharacters);
                            if (fitted != text && fitted.Length > 0)
                            {
                                list[index] = fitted;
                                changed = true;
                            }
                        }
                    }

                    // The first items are the ones the model meant most; the rest are dropped.
                    while (list.Count > maxItems)
                    {
                        list.RemoveAt(list.Count - 1);
                        changed = true;
                    }

                    return changed;
                }

            default:
                return false;
        }
    }
}
