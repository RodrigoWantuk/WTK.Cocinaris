using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0062 Slice 2: storage guidance, the arrival advisor, line marks, and the curated attention rule.</summary>
public sealed class ArrivalAdvisorTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly StorageRule Chicken = new("Refrigerator", 1, true, 270);
    private static readonly StorageRule Yogurt = new("Refrigerator", 7, false, null);
    private static readonly StorageRule Rice = new("Pantry", null, false, null);

    private static IReadOnlyList<ArrivalPortion> Advise(decimal bought, StorageRule? rule, bool freezer = true, string unit = "Gram", params ArrivalUse[] uses) =>
        ArrivalAdvisor.Advise(bought, unit, StoreSections.ButcherFish, rule, true, freezer, Today, uses);

    [Fact]
    public void AMealSoonStaysInTheFridgeAndALaterOneGoesToTheFreezer()
    {
        var portions = Advise(600, Chicken, uses: [new("Wrap", 300, Today.AddDays(1)), new("Sopa", 300, Today.AddDays(4))]);

        Assert.Equal(2, portions.Count);
        Assert.Equal(new ArrivalPortion("Refrigerator", 300, 1, "Wrap", false), portions[0]);
        Assert.Equal(new ArrivalPortion("Freezer", 300, 1, "Sopa", false), portions[1]);
    }

    [Fact]
    public void MealsOnTheSameDayShareAPortion()
    {
        var portion = Assert.Single(Advise(600, Chicken, uses: [new("A", 200, Today), new("B", 400, Today)]));

        Assert.Equal(600, portion.Quantity);
        Assert.Null(portion.Dish);
    }

    [Fact]
    public void ASurplusIsFrozenInEvenPortionsAboutTheSizeOfTheLargestUseAndAddsUpExactly()
    {
        var portions = Advise(1000, Chicken, uses: [new("Wrap", 300, Today)]);

        Assert.Equal(new ArrivalPortion("Freezer", 233, 2, null, false), portions[1]);
        Assert.Equal(new ArrivalPortion("Freezer", 234, 1, null, false), portions[2]);
        Assert.Equal(1000, portions.Sum(portion => portion.Quantity * portion.Count));
    }

    [Fact]
    public void ASurplusNeverMakesMoreThanSixPortions()
    {
        var surplus = Advise(1000, Chicken, uses: [new("Wrap", 10, Today)])[1];

        Assert.Equal(new ArrivalPortion("Freezer", 165, 6, null, false), surplus);
    }

    [Fact]
    public void WholeUnitsAreNotCutIntoFractions()
    {
        var surplus = Advise(7, Chicken, unit: "Unit", uses: [new("Wrap", 2, Today)])[1];

        Assert.Equal(new ArrivalPortion("Freezer", 5, 1, null, false), surplus);
    }

    [Fact]
    public void WithoutAFreezerNothingGoesThereAndAMealThatWillNotKeepIsMarkedUseFirst()
    {
        var portions = Advise(600, Chicken, freezer: false, uses: [new("Sopa", 300, Today.AddDays(4))]);

        Assert.All(portions, portion => Assert.NotEqual("Freezer", portion.Location));
        Assert.True(portions[0].UseFirst);
    }

    [Fact]
    public void WhatCannotBeFrozenIsMarkedUseFirst()
    {
        var portion = Assert.Single(Advise(200, Yogurt, uses: [new("Bolo", 200, Today.AddDays(10))]));

        Assert.Equal("Refrigerator", portion.Location);
        Assert.True(portion.UseFirst);
    }

    [Fact]
    public void AShelfStableFoodIsOneLot()
    {
        var portion = Assert.Single(Advise(1000, Rice, uses: [new("A", 200, Today), new("B", 200, Today.AddDays(5))]));

        Assert.Equal(new ArrivalPortion("Pantry", 1000, 1, null, false), portion);
    }

    [Fact]
    public void WithoutARuleItStaysWholeWithNoFreezingAdvice()
    {
        var portion = Assert.Single(Advise(1000, null, uses: [new("Sopa", 300, Today.AddDays(9))]));

        Assert.Equal(new ArrivalPortion("Refrigerator", 1000, 1, "Sopa", false), portion);
    }

    [Fact]
    public void BuyingLessThanNeededAllocatesInPlanOrder()
    {
        var portions = Advise(300, Chicken, uses: [new("Wrap", 300, Today), new("Sopa", 300, Today.AddDays(5))]);

        Assert.Equal("Wrap", Assert.Single(portions).Dish);
    }

    [Fact]
    public void TheGuidanceIsSilentUntilEnabled()
    {
        var chicken = IngredientCatalog.Default.BySlug["chicken"];

        Assert.Null(StorageGuidance.Create(new StorageGuidanceOptions()).RuleFor(chicken));
        var enabled = StorageGuidance.Create(new StorageGuidanceOptions { Enabled = true });
        Assert.Equal(1, enabled.RuleFor(chicken)!.FridgeDays);
        Assert.Equal("Pantry", enabled.RuleFor(IngredientCatalog.Default.BySlug["onion"])!.Location);
        Assert.Null(enabled.RuleFor(null));
    }

    [Fact]
    public void ATableNamingAnUnknownSlugFailsFast() =>
        Assert.Throws<InvalidOperationException>(() => StorageGuidance.Parse("""{"slugs":{"no-such":{"location":"Pantry","freezable":false}}}""", true, IngredientCatalog.Default));

    [Fact]
    public void TheCuratedRuleSetsADateOnlyForARefrigeratedLotWithNoDate()
    {
        var created = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        InventoryAttentionFacts Facts(DateOnly? printed = null, string storage = "Refrigerator", int? days = 1) => new(printed, null, storage, null, "AsAcquired", null, null, null, false, created, days);

        var curated = InventoryAttentionAdvisor.Evaluate(Facts(), Today, TimeZoneInfo.Utc);
        Assert.Equal((InventoryAttentionState.UsePriority, Today.AddDays(1), InventoryAttentionEvidence.CuratedRule), (curated.State, curated.EstimatedUseBy, curated.Evidence));

        // A printed date is stronger, and the flag being off (no days) changes nothing.
        Assert.Equal(InventoryAttentionEvidence.PrintedDate, InventoryAttentionAdvisor.Evaluate(Facts(Today.AddDays(30)), Today, TimeZoneInfo.Utc).Evidence);
        Assert.Equal(InventoryAttentionEvidence.Unknown, InventoryAttentionAdvisor.Evaluate(Facts(days: null), Today, TimeZoneInfo.Utc).Evidence);
        Assert.Equal(InventoryAttentionEvidence.Unknown, InventoryAttentionAdvisor.Evaluate(Facts(storage: "Pantry"), Today, TimeZoneInfo.Utc).Evidence);
    }

    [Fact]
    public void ASubstituteTakesOverTheLineSoItsCheckAndPackageFollow()
    {
        var meal = new ShoppingMeal(Guid.NewGuid(), "Sopa", null, 2, 2, """{"ingredients":[{"ingredientId":"i1","displayName":"Coentro","sourceType":"additional","requiredQuantity":50,"unit":"g","optional":false}]}""");
        var original = ShoppingProjector.Key("Coentro", "Gram");
        var marks = new Dictionary<string, ShoppingLineMark> { [original] = new(original, null, "Salsa", null) };
        var replaced = ShoppingProjector.Key("Salsa", "Gram");

        var item = Assert.Single(ShoppingProjector.Build([meal], [], new HashSet<string> { replaced }, null, null, marks));

        Assert.Equal(("Salsa", replaced, original, "Coentro", 50m, true), (item.DisplayName, item.ItemKey, item.MarkKey, item.SubstituteFor, item.ToBuyQuantity, item.Checked));
    }

    [Fact]
    public void ALineCarriesItsLaterStateAndPlace()
    {
        var meal = new ShoppingMeal(Guid.NewGuid(), "Sopa", null, 2, 2, """{"ingredients":[{"ingredientId":"i1","displayName":"Coentro","sourceType":"additional","requiredQuantity":50,"unit":"g","optional":false}]}""");
        var key = ShoppingProjector.Key("Coentro", "Gram");

        var plain = Assert.Single(ShoppingProjector.Build([meal], [], new HashSet<string>()));
        Assert.Equal((false, ShoppingPlaces.Market), (plain.Later, plain.Place));
        var marked = Assert.Single(ShoppingProjector.Build([meal], [], new HashSet<string>(), null, null, new Dictionary<string, ShoppingLineMark> { [key] = new(key, "later", null, ShoppingPlaces.Butcher) }));
        Assert.Equal((true, ShoppingPlaces.Butcher), (marked.Later, marked.Place));
        Assert.Equal(ShoppingPlaces.StreetMarket, ShoppingPlaces.OfSection(StoreSections.Produce));
    }
}
