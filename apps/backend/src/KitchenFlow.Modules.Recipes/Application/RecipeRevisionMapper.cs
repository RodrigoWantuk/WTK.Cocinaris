using System.Text.Json;
using KitchenFlow.Modules.Recipes.Ai;

namespace KitchenFlow.Modules.Recipes.Application;

/// <summary>
/// Deterministically normalizes an already-validated <see cref="ExpandRecipe"/> AI Gateway response
/// into the immutable JSON stored on a <see cref="Domain.RecipeRevision"/>. Serialization uses a
/// fixed property order (declaration order) and no extension data, so two validations of logically
/// identical content always produce byte-identical normalized JSON.
/// </summary>
public static class RecipeRevisionMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>Serializes the validated recipe artifact into its normalized immutable revision JSON.</summary>
    public static string NormalizeRecipe(ExpandRecipe recipe) => JsonSerializer.Serialize(recipe, SerializerOptions);

    /// <summary>Serializes the validated thumbnail visual descriptor into its normalized JSON.</summary>
    public static string NormalizeThumbnailVisual(ExpandThumbnailVisual visual) => JsonSerializer.Serialize(visual, SerializerOptions);
}
