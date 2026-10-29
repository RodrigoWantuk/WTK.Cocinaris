using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.Modules.Recipes.Thumbnails;

/// <summary>
/// A recipe revision's validated visual descriptor in canonical form, with its cache keys and the
/// provider prompt (docs/ai/recipe-thumbnail-generation.md). The descriptor describes only the
/// finished dish, and the expansion validator already rejected private context. Identical
/// appearance therefore shares one image across recipes and owners.
/// </summary>
/// <param name="CanonicalJson">Normalized descriptor: trimmed, lower-case, sorted lists, fixed property order.</param>
/// <param name="VisualIdentityKey">SHA-256 of the descriptor schema version and the canonical descriptor.</param>
/// <param name="RenderKey">SHA-256 of the visual identity and the full rendering policy; the image cache key.</param>
/// <param name="Prompt">Provider prompt built from the canonical descriptor.</param>
public sealed record RecipeThumbnailDescriptor(string CanonicalJson, string VisualIdentityKey, string RenderKey, string Prompt)
{
    /// <summary>Style and prompt template version; a change gives every dish a new render key.</summary>
    public const string StyleVersion = "recipe-thumbnail-style-v2";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Parses and canonicalizes a stored <c>thumbnailVisual</c>. Returns null for an empty descriptor
    /// (recipes written by hand) or one that fails the protocol bounds, which keeps the placeholder.
    /// </summary>
    /// <param name="thumbnailVisualJson">Stored descriptor JSON.</param>
    /// <param name="renderPolicy">Model, size tier, and aspect ratio; part of the render key.</param>
    public static RecipeThumbnailDescriptor? TryCreate(string? thumbnailVisualJson, string renderPolicy)
    {
        if (string.IsNullOrWhiteSpace(thumbnailVisualJson) || thumbnailVisualJson.Trim() is "{}" or "null")
        {
            return null;
        }

        ExpandThumbnailVisual? visual;
        try
        {
            visual = JsonSerializer.Deserialize<ExpandThumbnailVisual>(thumbnailVisualJson, ReadOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (visual is null)
        {
            return null;
        }

        var errors = new List<string>();
        visual.Validate(errors, "thumbnailVisual");
        if (errors.Count > 0)
        {
            return null;
        }

        static string Clean(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        static JsonArray List(IEnumerable<string> values) => new(values.Select(Clean).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(item => (JsonNode)item).ToArray());
        var canonical = new JsonObject
        {
            ["appearanceDescription"] = Clean(visual.AppearanceDescription),
            ["dishFormat"] = Clean(visual.DishFormat),
            ["dominantColors"] = List(visual.DominantColors),
            ["excludedElements"] = List(visual.ExcludedElements),
            ["garnish"] = List(visual.Garnish),
            ["plating"] = Clean(visual.Plating),
            ["sauceAppearance"] = Clean(visual.SauceAppearance),
            ["schemaVersion"] = visual.SchemaVersion,
            ["textureAndDoneness"] = List(visual.TextureAndDoneness),
            ["visibleComponents"] = List(visual.VisibleComponents)
        }.ToJsonString();
        var visualKey = Sha256($"{visual.SchemaVersion}\n{canonical}");
        var renderKey = Sha256($"{visualKey}\n{StyleVersion}\n{renderPolicy}");
        return new RecipeThumbnailDescriptor(canonical, visualKey, renderKey, BuildPrompt(visual));
    }

    /// <summary>The canonical prompt template <c>recipe-thumbnail-style-v2</c> filled from the descriptor.</summary>
    private static string BuildPrompt(ExpandThumbnailVisual visual)
    {
        static string Join(IEnumerable<string> values) => values.Any() ? string.Join(", ", values.Select(item => item.Trim())) : "none";
        return new StringBuilder()
            .AppendLine("Generate a natural, photorealistic 16:9 photograph of one served plate of the following finished dish:")
            .AppendLine()
            .AppendLine(visual.AppearanceDescription.Trim())
            .AppendLine()
            .AppendLine($"Visible components: {Join(visual.VisibleComponents)}")
            .AppendLine($"Dish format: {visual.DishFormat.Trim()}")
            .AppendLine($"Plating: {visual.Plating.Trim()}")
            .AppendLine($"Sauce appearance: {visual.SauceAppearance.Trim()}")
            .AppendLine($"Texture and cooked-state cues: {Join(visual.TextureAndDoneness)}")
            .AppendLine($"Garnish: {Join(visual.Garnish)}")
            .AppendLine($"Dominant colors: {Join(visual.DominantColors)}")
            .AppendLine()
            .Append("Use a modern wooden table and a light, predominantly white, softly blurred background. Keep the plate and food as the clear subject, with only a small margin outside the plate. ")
            .Append("Make it look like a real photograph of home-cooked food: soft natural daylight, gentle shadows, true-to-life colors, real food textures with small natural imperfections, and a shallow depth of field. Avoid an illustrated, painted, cartoon, 3D-rendered, CGI, over-saturated, over-smoothed, glossy, or artificially perfect look. ")
            .Append("Show the visible components and cooked states exactly as described. Do not add ingredients, garnish, side dishes, utensils, cutlery, napkins, hands, people, packaging, logos, labels, watermarks, or text. ")
            .Append("Do not imply nutrition, dietary suitability, restaurant quality, authenticity, freshness, doneness safety, or food-safety guarantees.")
            .ToString();
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
