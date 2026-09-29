using System.Text.Json;
using KitchenFlow.Modules.Recipes.Ai;

namespace KitchenFlow.UnitTests;

/// <summary>
/// Fixture-parity tests asserting the C# protocol 0.3 validators reach the exact same accept/reject
/// verdicts as <c>packages/contracts/ai/recipe/lib/validate-core.mjs</c> for every fixture consumed by
/// <c>packages/contracts/ai/recipe/test.mjs</c>.
/// </summary>
public sealed class RecipeProtocolValidatorFixtureTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "ContractFixtures", "fixtures", "responses");
    private static readonly string ExamplesRoot = Path.Combine(AppContext.BaseDirectory, "ContractFixtures", "examples");

    [Fact]
    public void PositiveSuggestFixtureIsAccepted()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "positive", "suggest-candidates.valid.json"));
        var result = RecipeProtocolValidator.ValidateSuggest(raw);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void PositiveExpandFixtureIsAccepted()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "positive", "expand-selected.valid.json"));
        var result = RecipeProtocolValidator.ValidateExpand(raw);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData("suggest-wrong-count.json")]
    [InlineData("suggest-extra-property.json")]
    [InlineData("suggest-noncanonical-unit.json")]
    [InlineData("suggest-injection-authority-field.json")]
    public void NegativeSuggestFixtureFailsStructurally(string fileName)
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "negative", fileName));
        var result = RecipeProtocolValidator.ValidateSuggest(raw);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("expand-thumbnail-cache-hash.json")]
    [InlineData("expand-too-many-stages.json")]
    public void NegativeExpandFixtureFailsStructurally(string fileName)
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "negative", fileName));
        var result = RecipeProtocolValidator.ValidateExpand(raw);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void InventedEquipmentAndExcessiveLeadFixturesFailSemanticallyAgainstRequest()
    {
        var request = LoadExample("08-suggest-cook-now-local-meal.request.json");

        foreach (var fileName in new[] { "suggest-invented-equipment-id-format-ok-but-semantic.json", "suggest-excessive-lead-minutes.json" })
        {
            var raw = File.ReadAllText(Path.Combine(FixturesRoot, "negative", fileName));
            var structural = RecipeProtocolValidator.ValidateSuggest(raw);
            Assert.True(structural.IsValid, $"{fileName} unexpectedly failed structurally: {string.Join("; ", structural.Errors)}");

            var semantic = RecipeProtocolValidator.ValidateSuggest(raw, request);
            Assert.False(semantic.IsValid, $"{fileName} was expected to fail semantics");
        }
    }

    [Fact]
    public void AssumptionRepeatedAsAdditionalIngredientFailsSemantics()
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "negative", "suggest-assumption-as-additional.json"));
        var result = RecipeProtocolValidator.ValidateSuggest(raw);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("expand-private-thumbnail.json")]
    [InlineData("expand-invented-visible-component.json")]
    public void ExpandFixturesFailSemanticsWithoutRequest(string fileName)
    {
        var raw = File.ReadAllText(Path.Combine(FixturesRoot, "negative", fileName));
        var result = RecipeProtocolValidator.ValidateExpand(raw);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("01-suggest-weeknight.request.json")]
    [InlineData("08-suggest-cook-now-local-meal.request.json")]
    public void SuggestRequestFixturesLoadAsJson(string fileName)
    {
        var element = LoadExample(fileName);
        Assert.Equal(JsonValueKind.Object, element!.Value.ValueKind);
    }

    private static JsonElement? LoadExample(string fileName)
    {
        var raw = File.ReadAllText(Path.Combine(ExamplesRoot, fileName));
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }
}
