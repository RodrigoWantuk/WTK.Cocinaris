using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0039 R1: informational text is fitted before validation; nothing that is cooked, bought, or checked is.</summary>
public sealed class RecipeOutputNormalizerTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "ContractFixtures", "fixtures", "responses");

    private static JsonObject ValidExpand() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesRoot, "positive", "expand-selected.valid.json")))!.AsObject();

    [Fact]
    public void ALongStorageNoteIsFittedAndTheRecipeIsAccepted()
    {
        var answer = ValidExpand();
        answer["recipe"]!["storage"]!["reheatNotes"] = string.Join(' ', Enumerable.Repeat("Aqueça em fogo baixo mexendo sempre.", 20));
        Assert.False(RecipeProtocolValidator.ValidateExpand(answer.ToJsonString().Replace("reheatNotes", "reheatNotesX")).IsValid);

        var result = RecipeProtocolValidator.ValidateExpand(answer.ToJsonString());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(["storage.reheatNotes"], result.Normalized);
        Assert.True(result.Response!.Recipe.Storage.ReheatNotes.Length <= 240);
    }

    [Fact]
    public void ListsKeepTheirFirstItemsAndTheVisualDescriptionItsWordLimit()
    {
        var answer = ValidExpand();
        var stage = answer["recipe"]!["stages"]![0]!.AsObject();
        stage["sensoryCues"] = new JsonArray(Enumerable.Range(1, 8).Select(index => JsonValue.Create($"Sinal {index}")).ToArray<JsonNode?>());
        answer["recipe"]!["thumbnailVisual"]!["appearanceDescription"] = string.Join(' ', Enumerable.Repeat("prato", 50));

        var (json, applied) = RecipeOutputNormalizer.NormalizeRecipe(answer.ToJsonString());
        var fitted = JsonNode.Parse(json)!;

        Assert.Contains("stages[].sensoryCues", applied);
        Assert.Contains("thumbnailVisual.appearanceDescription", applied);
        Assert.Equal(["Sinal 1", "Sinal 2", "Sinal 3", "Sinal 4", "Sinal 5", "Sinal 6"], fitted["recipe"]!["stages"]![0]!["sensoryCues"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.True(fitted["recipe"]!["thumbnailVisual"]!["appearanceDescription"]!.GetValue<string>().Split(' ').Length <= RecipeExpandSemantics.MaxAppearanceWords);
    }

    [Fact]
    public void WhatIsCookedOrBoughtIsNeverChanged()
    {
        var answer = ValidExpand();
        var ingredient = answer["recipe"]!["ingredients"]![0]!.AsObject();
        ingredient["displayName"] = new string('a', 120);
        answer["recipe"]!["stages"]![0]!["instructions"] = new string('b', 900);
        var raw = answer.ToJsonString();

        var (json, applied) = RecipeOutputNormalizer.NormalizeRecipe(raw);
        Assert.Empty(applied);
        Assert.Equal(raw, json);
        // The slips are left for repair.
        var result = RecipeProtocolValidator.ValidateExpand(raw);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("displayName", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCatalogNamesOnlyInformationalFields()
    {
        string[] forbidden = ["ingredient", "quantity", "unit", "instruction", "equipment", "name", "meal", "assumption", "restriction", "allergen", "dependsOn", "uses"];
        Assert.All(RecipeOutputNormalizer.Fields, field => Assert.DoesNotContain(forbidden, word => field.Path.Contains(word, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(("not json", 0), (RecipeOutputNormalizer.NormalizeRecipe("not json").Json, RecipeOutputNormalizer.NormalizeRecipe("not json").Applied.Count));
    }

    [Fact]
    public void FitTextCutsAtASentenceOrAWord()
    {
        Assert.Equal("Curto.", RecipeOutputNormalizer.FitText("  Curto.  ", 40));
        Assert.Equal("Primeira frase longa aqui.", RecipeOutputNormalizer.FitText("Primeira frase longa aqui. Segunda frase que passa do limite", 40));
        Assert.Equal("um dois tres…", RecipeOutputNormalizer.FitText("um dois tres quatro", 40, maxWords: 3));
        Assert.True(RecipeOutputNormalizer.FitText(new string('x', 100), 40).Length <= 40);
    }
}
