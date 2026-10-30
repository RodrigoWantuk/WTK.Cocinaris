using System.Text.Json;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Recipes.Thumbnails;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0055 Phase 3: deterministic similarity of dish descriptors for image reuse.</summary>
public sealed class ThumbnailSimilarityTests
{
    private static string Descriptor(string format, string[] components, string[] colors, string sauce = "", string[]? texture = null) =>
        JsonSerializer.Serialize(new
        {
            appearanceDescription = "ignored by the score",
            dishFormat = format,
            dominantColors = colors,
            excludedElements = Array.Empty<string>(),
            garnish = Array.Empty<string>(),
            plating = "single plate",
            sauceAppearance = sauce,
            schemaVersion = "1",
            textureAndDoneness = texture ?? [],
            visibleComponents = components
        });

    private static readonly string ChickenRice = Descriptor("plated_main_with_sides", ["chicken breast in tomato sauce", "white rice", "sauteed carrot slices"], ["red", "white", "orange"], "smooth red tomato sauce", ["fully cooked chicken"]);

    [Fact]
    public void TheSameDishWordedDifferentlyIsReused()
    {
        var reworded = Descriptor("plated_main_with_sides", ["cooked chicken breast with tomato", "rice", "carrot"], ["white", "red", "orange"], "red tomato sauce", ["cooked chicken"]);

        Assert.True(ThumbnailSimilarity.Score(ChickenRice, reworded, IngredientCatalog.Default) >= 0.8);
    }

    [Fact]
    public void ADifferentFormatOrMainIngredientIsNotReused()
    {
        var bowl = Descriptor("bowl", ["chicken breast in tomato sauce", "white rice", "sauteed carrot slices"], ["red", "white", "orange"], "smooth red tomato sauce", ["fully cooked chicken"]);
        var fish = Descriptor("plated_main_with_sides", ["white fish fillet in tomato sauce", "white rice", "sauteed carrot slices"], ["red", "white", "orange"], "smooth red tomato sauce", ["flaky fish"]);

        Assert.Equal(0, ThumbnailSimilarity.Score(ChickenRice, bowl, IngredientCatalog.Default));
        Assert.True(ThumbnailSimilarity.Score(ChickenRice, fish, IngredientCatalog.Default) < 0.8);
    }

    [Fact]
    public void ComponentsBecomeCatalogIngredientsAndUnknownOnesKeepTheirText()
    {
        var tokens = ThumbnailSimilarity.ComponentTokens(["chicken breast in tomato sauce", "Arroz branco", "golden crispy croutons"], IngredientCatalog.Default);

        Assert.Contains("ingredient:chicken-breast", tokens);
        Assert.Contains("ingredient:tomato", tokens);
        Assert.Contains("ingredient:rice", tokens);
        Assert.Contains("golden crispy croutons", tokens);
        Assert.Equal(1.0, ThumbnailSimilarity.Score(ChickenRice, ChickenRice, IngredientCatalog.Default), 6);
    }
}
