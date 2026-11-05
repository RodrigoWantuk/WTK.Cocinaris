using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0062 Slice 5: reusable bases, on the list and in storage proposals.</summary>
public sealed class PlanComponentTests
{
    private static string Recipe(params (string Name, decimal Quantity)[] items) =>
        $$"""{"ingredients":[{{string.Join(",", items.Select((item, index) => $$"""{"ingredientId":"i{{index}}","sourceType":"additional","displayName":"{{item.Name}}","requiredQuantity":{{item.Quantity}},"unit":"g","optional":false}"""))}}]}""";

    private static ShoppingMeal Meal(string name, MenuEntryProduction? production, params (string Name, decimal Quantity)[] items) =>
        new(Guid.NewGuid(), name, null, 2, 2, Recipe(items), production);

    [Fact]
    public void WhatABaseMakesIsNotBoughtAndItsOwnIngredientsAre()
    {
        var wrap = Meal("Wrap", null, ("Frango desfiado", 300));
        var pie = Meal("Escondidinho", null, ("Frango desfiado", 300));
        var @base = Meal("Base", new MenuEntryProduction("Frango desfiado", 700, "Gram"), ("Peito de frango", 800));

        // The base is last here on purpose: it still comes first.
        var lines = ShoppingProjector.Build([wrap, pie, @base], [], new HashSet<string>()).ToDictionary(item => item.DisplayName);

        Assert.Equal(("from_prep", 600m, 0m), (lines["Frango desfiado"].Category, lines["Frango desfiado"].RequiredQuantity, lines["Frango desfiado"].ToBuyQuantity));
        Assert.Equal(("required", 800m), (lines["Peito de frango"].Category, lines["Peito de frango"].ToBuyQuantity));
    }

    [Fact]
    public void ABaseThatFallsShortLeavesTheRestToBuy()
    {
        var wrap = Meal("Wrap", null, ("Frango desfiado", 500));
        var @base = Meal("Base", new MenuEntryProduction("frango  desfiado", 300, "Gram"), ("Peito de frango", 400));

        var line = ShoppingProjector.Build([wrap, @base], [], new HashSet<string>()).Single(item => item.DisplayName == "Frango desfiado");

        Assert.Equal(("required", 200m), (line.Category, line.ToBuyQuantity));
    }

    [Fact]
    public void WithoutTheBaseTheItemIsARequiredLine()
    {
        var line = Assert.Single(ShoppingProjector.Build([Meal("Wrap", null, ("Frango desfiado", 300))], [], new HashSet<string>()));

        Assert.Equal(("required", 300m), (line.Category, line.ToBuyQuantity));
    }

    [Fact]
    public void ABaseInAnotherUnitDoesNotCoverTheLine()
    {
        var @base = Meal("Base", new MenuEntryProduction("Frango desfiado", 2, "Unit"), ("Peito de frango", 400));

        var line = ShoppingProjector.Build([Meal("Wrap", null, ("Frango desfiado", 300)), @base], [], new HashSet<string>()).Single(item => item.DisplayName == "Frango desfiado");

        Assert.Equal(("required", 300m), (line.Category, line.ToBuyQuantity));
    }

    [Fact]
    public void TheCardEstimateCountsWhatABaseMakes()
    {
        var @base = new RankCard(Guid.NewGuid(), 1, "base", "boil", null, 10, 10, [new RankIngredient("peito|Gram", "Peito", 800, true, true)], 0, "desfiado|Gram", 700);
        var dish = new RankCard(Guid.NewGuid(), 2, "wrap", "fold", null, 10, 10, [new RankIngredient("desfiado|Gram", "Desfiado", 300, true, false)]);

        // The dish was accepted first, but the base is counted first, as on the real list.
        var projection = new ShoppingPlanProjection([new RankAccepted(dish, 1, false), new RankAccepted(@base, 1, true)], new Dictionary<string, decimal>());

        Assert.False(projection.IsOnList("desfiado|Gram"));
        Assert.True(projection.IsOnList("peito|Gram"));
        Assert.Equal(400m, projection.Remaining("desfiado|Gram"));
    }

    [Fact]
    public void BasesComeFirstInTheSuggestedOrder()
    {
        var now = DateTimeOffset.UnixEpoch;
        var fish = new SequenceEntry(Guid.NewGuid(), Guid.NewGuid(), 1, true, 10, now);
        var @base = new SequenceEntry(Guid.NewGuid(), Guid.NewGuid(), 9, false, 90, now.AddHours(1), IsComponent: true);

        Assert.Equal([@base.EntryId, fish.EntryId], ShoppingPlanSequencer.Order([fish, @base]));
    }

    [Fact]
    public void WithoutGuidanceADishIsProposedForTheRefrigeratorOnly()
    {
        var parts = StoredOutputAdvisor.ForDish(6, 3, [2, 5], freezeCompatible: true, cookedFridgeDays: null);

        Assert.Equal(2, parts.Count);
        Assert.All(parts, part => Assert.Equal((2m, "Unit", "Refrigerator"), (part.Quantity, part.Unit, part.StorageLocation)));
    }

    [Fact]
    public void WithGuidanceOnlyAFreezableDishPastItsDaysGoesToTheFreezer()
    {
        Assert.Equal(["Refrigerator", "Freezer"], StoredOutputAdvisor.ForDish(6, 3, [2, 5], true, 3).Select(part => part.StorageLocation));
        Assert.Equal(["Refrigerator", "Refrigerator"], StoredOutputAdvisor.ForDish(6, 3, [2, 5], false, 3).Select(part => part.StorageLocation));
    }

    [Fact]
    public void ABaseIsSplitByItsUsesWithTheRestInTheLast()
    {
        var parts = StoredOutputAdvisor.ForComponent(700, "Gram", [new OutputUse(300, 1), new OutputUse(300, 2)], null);

        Assert.Equal([300m, 300m, 100m], parts.Select(part => part.Quantity));
        Assert.All(parts, part => Assert.Equal("Refrigerator", part.StorageLocation));
    }

    [Fact]
    public void ABaseNeverProposesMoreThanFourPortionsOrMoreThanItMade()
    {
        var uses = Enumerable.Range(0, 6).Select(day => new OutputUse(100, day)).ToList();

        Assert.Equal([100m, 100m, 100m, 300m], StoredOutputAdvisor.ForComponent(600, "Gram", uses, null).Select(part => part.Quantity));
        Assert.Equal([150m], StoredOutputAdvisor.ForComponent(150, "Gram", [new OutputUse(300, 1)], null).Select(part => part.Quantity));
        Assert.Equal([500m], StoredOutputAdvisor.ForComponent(500, "Gram", [], null).Select(part => part.Quantity));
    }

    [Fact]
    public void AnEarlyUseStaysColdAndALateOneFreezesWhenTheTableSaysSo()
    {
        var parts = StoredOutputAdvisor.ForComponent(600, "Gram", [new OutputUse(300, 1), new OutputUse(300, 5)], new StorageRule("Refrigerator", 3, true, 60));

        Assert.Equal(["Refrigerator", "Freezer"], parts.Select(part => part.StorageLocation));
    }

    [Fact]
    public void APreparedLotInTheFreezerAgesFromWhenItWasMade()
    {
        var today = new DateOnly(2026, 11, 5);
        InventoryAttentionFacts Facts(string state, DateTimeOffset created) => new(null, null, "Freezer", null, state, null, null, null, CreatedAt: created);
        var old = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(InventoryAttentionState.CheckCondition, InventoryAttentionAdvisor.Evaluate(Facts("Prepared", old), today, TimeZoneInfo.Utc).State);
        Assert.Equal(InventoryAttentionState.Normal, InventoryAttentionAdvisor.Evaluate(Facts("Prepared", DateTimeOffset.Parse("2026-11-01T00:00:00Z")), today, TimeZoneInfo.Utc).State);
    }

    private const string CandidateTemplate = """
        {"candidateId":"@ID@","candidateStrategy":"exploratory","name":"Prato @ID@","targetMealType":"dinner","dishFormat":"format@ID@","primaryTechnique":"saute","primaryIngredientRefs":["i1"],"summary":"Um prato simples.","servings":2,"time":{"activeMinutes":10,"passiveMinutes":0,"totalMinutes":10},"difficulty":"easy","requiredEquipmentIds":[],"requiredCapabilities":[],"inventoryUses":[],"additionalIngredients":[{"name":"@ITEM@","requiredQuantity":100,"unit":"g","optional":false}],"preparationProfile":{"requiresAdvancePreparation":false,"minimumLeadMinutes":0,"blockingPreparationCodes":[],"mayProduceReusableComponents":false},"assumptionsUsed":[]}
        """;

    private static string Candidate(string id, string item) => CandidateTemplate.Replace("@ID@", id).Replace("@ITEM@", item);

    private static string DeckV2(string components)
    {
        var candidates = Enumerable.Range(0, 6).Select(index => Candidate($"c{index}", index < 2 ? "Frango desfiado" : "Arroz"));
        return "{\"operation\":\"recipe.suggest_plan_deck.v2\",\"schemaVersion\":\"0.4\",\"candidates\":[" + string.Join(",", candidates) + "],\"components\":" + components + "}";
    }

    private static string Component(string name = "Frango desfiado", decimal quantity = 700, string unit = "g", string usedBy = """["c0","c1"]""") =>
        "{\"candidate\":" + Candidate("base", "Peito de frango") + ",\"output\":{\"name\":\"" + name + "\",\"quantity\":" + quantity + ",\"unit\":\"" + unit + "\"},\"usedBy\":" + usedBy + "}";

    [Fact]
    public void AValidComponentIsKept()
    {
        var result = RecipeProtocolValidator.ValidatePlanDeck(DeckV2($"[{Component(name: "frango  desfiado")}]"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Single(result.Response!.Components!);
    }

    [Theory]
    [InlineData("""["c0","nobody"]""", "Frango desfiado", 700, "g")]
    [InlineData("""["c0","c2"]""", "Frango desfiado", 700, "g")]
    [InlineData("""["c0","c1"]""", "Carne moida", 700, "g")]
    [InlineData("""["c0","c1"]""", "Frango desfiado", 150, "g")]
    [InlineData("""["c0","c1"]""", "Frango desfiado", 700, "ml")]
    public void ABadComponentIsDroppedAndTheDishesStay(string usedBy, string name, int quantity, string unit)
    {
        var result = RecipeProtocolValidator.ValidatePlanDeck(DeckV2($"[{Component(name, quantity, unit, usedBy)}]"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Empty(result.Response!.Components!);
        Assert.Equal(6, result.Response.Candidates.Count);
    }

    [Fact]
    public void AComponentWithAnUnknownFieldIsDroppedToo()
    {
        var extra = Component().Replace("\"usedBy\"", "\"secret\":1,\"usedBy\"");

        Assert.Empty(RecipeProtocolValidator.ValidatePlanDeck(DeckV2($"[{extra}]")).Response!.Components!);
    }

    [Fact]
    public void TooManyComponentsOrTheWrongVersionFailsTheBatch()
    {
        var three = string.Join(",", Enumerable.Repeat(Component(), 3));
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckV2($"[{three}]")).IsValid);
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckV2("[]").Replace("\"0.4\"", "\"0.3\"")).IsValid);
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckV2("[]").Replace("suggest_plan_deck.v2", "suggest_plan_deck.v1")).IsValid);
    }
}

/// <summary>PLAN-0064: how much the pantry counts follows what the person said and refused.</summary>
public sealed class PlanPantryWeightTests
{
    private static RankCard Card(int order, string key, int active = 10) =>
        new(Guid.NewGuid(), order, "format" + order, "technique" + order, null, active, active, [new RankIngredient(key, key, 100, true, false)]);

    private static readonly RankCard Fresh = Card(0, "abobora|Gram");
    private static readonly RankCard FromPantry = Card(1, "tomate|Gram");

    private static IReadOnlyList<RankedCard> Rank(string priority, int weight, params RankCard[] cards) =>
        ShoppingPlanRanker.Rank(cards, new RankContext(priority, [], [], new Dictionary<string, decimal> { ["tomate|Gram"] = 500 }, new HashSet<string> { "tomate|Gram" }, null, weight));

    [Theory]
    [InlineData(null, 0, 3)]
    [InlineData(null, 1, 2)]
    [InlineData(null, 2, 1)]
    [InlineData(null, 3, 0)]
    [InlineData(null, 9, 0)]
    [InlineData("free", 0, 0)]
    [InlineData("use", 9, 3)]
    public void AClearAnswerWinsAndEachNoWeakensThePantry(string? pantry, int rejections, int expected) =>
        Assert.Equal(expected, ShoppingPlanRanker.PantryWeight(pantry, rejections));

    [Theory]
    [InlineData("waste")]
    [InlineData("short")]
    [InlineData("save")]
    [InlineData("time")]
    public void WithFullWeightThePantryDishLeads(string priority) =>
        Assert.Same(FromPantry, Rank(priority, ShoppingPlanRanker.FullPantryWeight, Fresh, FromPantry)[0].Card);

    [Theory]
    [InlineData("waste")]
    [InlineData("short")]
    [InlineData("save")]
    [InlineData("time")]
    public void WithoutWeightThePantryGivesNoAdvantage(string priority)
    {
        var ranked = Rank(priority, 0, Fresh, FromPantry);
        Assert.Equal(ranked[0].Score, ranked[1].Score);
        Assert.Same(Fresh, ranked[0].Card);
    }

    [Fact]
    public void WithoutWeightTheTimeGoalStillCounts()
    {
        var slow = Card(0, "abobora|Gram", active: 60);
        var quick = Card(1, "tomate|Gram", active: 5);
        Assert.Same(quick, Rank("time", 0, slow, quick)[0].Card);
    }

    [Fact]
    public void FullWeightKeepsTheOriginalScore() =>
        Assert.Equal(100 - 0 + 5, Rank("short", ShoppingPlanRanker.FullPantryWeight, FromPantry)[0].Score);
}
