using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0062: the restriction guard, ranking, sequencing, and setup rules of shopping plans.</summary>
public sealed class ShoppingPlanTests
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly IngredientMatchKeys Names = IngredientMatchKeys.NamesOnly;
    private static readonly RestrictionGuard Guard = RestrictionGuard.Default;

    [Theory]
    [InlineData("lactose_intolerance", "Intolerance", "Molho de queijo")]
    [InlineData("lactose_intolerance", "Intolerance", "CREME DE LEITE")]
    [InlineData("milk_allergy", "Allergy", "Manteiga")]
    [InlineData("lactose_intolerance", "Intolerance", "cheese sauce")]
    [InlineData("lactose_intolerance", "Intolerance", "Crema agria")]
    [InlineData("vegetarian", "DietaryPattern", "Peito de frango")]
    [InlineData("vegetarian", "DietaryPattern", "pollo asado")]
    [InlineData("vegan", "DietaryPattern", "Ovos")]
    [InlineData("vegan", "DietaryPattern", "mel")]
    [InlineData("peanut_allergy", "Allergy", "amendoim torrado")]
    [InlineData("gluten_intolerance", "Intolerance", "Pães de forma")]
    [InlineData("celiac_safe_diet", "MedicalRestriction", "macarrão")]
    [InlineData("shellfish_allergy", "Allergy", "Camarões")]
    [InlineData("tree_nut_allergy", "Allergy", "nozes")]
    public void ABrokenRuleBlocksInAnyLanguageAndWithAnyAccent(string code, string category, string ingredient)
    {
        var result = Guard.Check(["Arroz", ingredient], Names, [new RestrictionCode(code, category)]);

        Assert.Equal(RestrictionGuardStatus.Blocked, result.Status);
        Assert.Equal(code, result.Code);
        Assert.Equal(ingredient, result.Ingredient);
    }

    [Theory]
    [InlineData("lactose_intolerance", "Leite de coco")]
    [InlineData("lactose_intolerance", "manteiga de cacau")]
    [InlineData("gluten_intolerance", "Massa de tomate")]
    [InlineData("gluten_intolerance", "Pão de queijo")]
    [InlineData("vegetarian", "Carne de soja")]
    public void AnExceptionIsNotABlock(string code, string ingredient)
    {
        var result = Guard.Check([ingredient], Names, [new RestrictionCode(code, "Intolerance")]);

        Assert.NotEqual(RestrictionGuardStatus.Blocked, result.Status);
    }

    [Fact]
    public void AWordInsideAnotherWordDoesNotMatch()
    {
        // "melão" holds "mel", and "panela" holds "pan".
        Assert.NotEqual(RestrictionGuardStatus.Blocked, Guard.Check(["Melão"], Names, [new RestrictionCode("vegan", "DietaryPattern")]).Status);
        Assert.NotEqual(RestrictionGuardStatus.Blocked, Guard.Check(["Panela"], Names, [new RestrictionCode("gluten_intolerance", "Intolerance")]).Status);
    }

    [Fact]
    public void AnUnknownNameIsUnverifiedOnlyUnderAStrictRestriction()
    {
        Assert.Equal(RestrictionGuardStatus.Unverified, Guard.Check(["Tempero da casa"], Names, [new RestrictionCode("peanut_allergy", "Allergy")]).Status);
        Assert.Equal(RestrictionGuardStatus.Unverified, Guard.Check(["Tempero da casa"], Names, [new RestrictionCode("low_sodium_diet", "MedicalRestriction")]).Status);
        Assert.Equal(RestrictionGuardStatus.Allowed, Guard.Check(["Tempero da casa"], Names, [new RestrictionCode("vegetarian", "DietaryPattern")]).Status);
    }

    [Fact]
    public void ACatalogIngredientWithNoConflictIsAllowedEvenUnderAnAllergy()
    {
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["arroz"] = "rice" });

        Assert.Equal(RestrictionGuardStatus.Allowed, Guard.Check(["Arroz"], keys, [new RestrictionCode("peanut_allergy", "Allergy")]).Status);
    }

    [Fact]
    public void ACategoryRuleCatchesACatalogNameTheKeywordsMiss()
    {
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["contrafile"] = "beef-steak" });

        Assert.Equal(RestrictionGuardStatus.Blocked, Guard.Check(["Contrafilé"], keys, [new RestrictionCode("vegetarian", "DietaryPattern")]).Status);
    }

    [Fact]
    public void ABlockWinsOverAnUnverifiedRestriction()
    {
        var result = Guard.Check(["Frango"], Names, [new RestrictionCode("custom_code", "Allergy"), new RestrictionCode("vegetarian", "DietaryPattern")]);

        Assert.Equal(RestrictionGuardStatus.Blocked, result.Status);
    }

    [Fact]
    public void ACustomCodeCannotBeChecked()
    {
        var restrictions = new[] { new RestrictionCode("no_pepper_please", "Allergy"), new RestrictionCode("cilantro", "Dislike"), new RestrictionCode("vegetarian", "DietaryPattern") };

        Assert.Equal(["no_pepper_please"], Guard.UnverifiableCodes(restrictions));
        Assert.Equal(RestrictionGuardStatus.Unverified, Guard.Check(["Arroz"], Names, [restrictions[0]]).Status);
    }

    [Fact]
    public void ADislikeNeverBlocksButIsCounted()
    {
        var dislike = new[] { new RestrictionCode("cilantro", "Dislike") };

        Assert.Equal(RestrictionGuardStatus.Allowed, Guard.Check(["Coentro"], Names, dislike).Status);
        Assert.Equal(1, Guard.DislikeMatches(["Coentro"], Names, dislike));
        Assert.Equal(0, Guard.DislikeMatches(["Arroz"], Names, dislike));
    }

    [Fact]
    public void ARuleNamingAnUnknownSlugFailsFast() =>
        Assert.Throws<InvalidOperationException>(() => RestrictionGuard.Parse("""{"rules":[{"code":"x","slugs":["no-such-slug"]}]}""", IngredientCatalog.Default));

    [Theory]
    [InlineData("white-fish", 1)]
    [InlineData("ground-beef", 1)]
    [InlineData("chicken", 2)]
    [InlineData("basil", 3)]
    [InlineData("tomato", 4)]
    [InlineData("milk", 4)]
    [InlineData("egg", 5)]
    [InlineData("rice", 9)]
    public void PerishabilityFollowsTheRankTable(string slug, int rank) =>
        Assert.Equal(rank, IngredientPerishability.Rank(IngredientCatalog.Default.BySlug[slug]));

    [Fact]
    public void AListFromAcceptedCardsAllocatesStockInOrder()
    {
        var chicken = Ingredient("frango|Gram", 300);
        var first = Card(1, "a", "x", [chicken]);
        var second = Card(2, "b", "y", [chicken]);
        var projection = new ShoppingPlanProjection([new RankAccepted(first, 1, false), new RankAccepted(second, 2, false)], new Dictionary<string, decimal> { ["frango|Gram"] = 400 });

        Assert.Equal(1, projection.ListItemCount);
        Assert.True(projection.IsOnList("frango|Gram"));
        Assert.Equal(0, projection.Remaining("frango|Gram"));
        Assert.Equal(2, projection.Uses("frango|Gram"));
    }

    [Fact]
    public void TheSameNameInAnotherUnitIsAnotherLine()
    {
        var card = Card(1, "a", "x", [Ingredient("frango|Gram", 300), Ingredient("frango|Unit", 1)]);

        Assert.Equal(2, new ShoppingPlanProjection([new RankAccepted(card, 1, false)], new Dictionary<string, decimal>()).ListItemCount);
    }

    [Fact]
    public void AShortListPrefersFewerNewItemsAndCountsThem()
    {
        var accepted = Card(1, "a", "x", [Ingredient("tomate|Gram", 100)]);
        var fewer = Card(2, "b", "y", [Ingredient("tomate|Gram", 100), Ingredient("alho|Gram", 10)]);
        var more = Card(3, "c", "z", [Ingredient("arroz|Gram", 100), Ingredient("feijao|Gram", 100), Ingredient("couve|Gram", 100)]);

        var ranked = ShoppingPlanRanker.Rank([more, fewer], Context("short", [new RankAccepted(accepted, 1, false)]));

        Assert.Equal(fewer.Id, ranked[0].Card.Id);
        Assert.Single(ranked[0].NewItems);
        Assert.Equal(["tomate|Gram"], ranked[0].Reused.Select(item => item.Key));
        Assert.Equal(3, ranked[1].NewItems.Count);
    }

    [Fact]
    public void LessWasteRescuesAPerishableUsedByOneDish()
    {
        var accepted = Card(1, "a", "x", [Ingredient("coentro|Gram", 10, perishable: true)]);
        var rescues = Card(2, "b", "y", [Ingredient("coentro|Gram", 10, perishable: true), Ingredient("arroz|Gram", 100), Ingredient("feijao|Gram", 100)]);
        var orphan = Card(3, "c", "z", [Ingredient("alface|Gram", 50, perishable: true)]);

        var ranked = ShoppingPlanRanker.Rank([orphan, rescues], Context("waste", [new RankAccepted(accepted, 1, false)]));

        Assert.Equal(rescues.Id, ranked[0].Card.Id);
    }

    [Fact]
    public void SaveAvoidsCostlyNewItems()
    {
        var costly = Card(1, "a", "x", [Ingredient("carne|Gram", 300, costly: true)]);
        var cheap = Card(2, "b", "y", [Ingredient("lentilha|Gram", 300)]);

        Assert.Equal(cheap.Id, ShoppingPlanRanker.Rank([costly, cheap], Context("save")).First().Card.Id);
    }

    [Fact]
    public void LessTimePrefersLessHandsOnWork()
    {
        var slow = Card(1, "a", "x", [], active: 60, total: 60);
        var quick = Card(2, "b", "y", [], active: 10, total: 60);

        Assert.Equal(quick.Id, ShoppingPlanRanker.Rank([slow, quick], Context("time")).First().Card.Id);
    }

    [Fact]
    public void TheSameInputAlwaysGivesTheSameOrderAndTiesKeepDeckOrder()
    {
        var cards = Enumerable.Range(1, 6).Select(index => Card(index, $"f{index}", $"t{index}", [])).Reverse().ToList();
        var context = Context("short");

        var first = ShoppingPlanRanker.Rank(cards, context).Select(item => item.Card.Id).ToList();
        var second = ShoppingPlanRanker.Rank(Enumerable.Reverse(cards), context).Select(item => item.Card.Id).ToList();

        Assert.Equal(first, second);
        Assert.Equal(cards.OrderBy(card => card.Order).Select(card => card.Id), first);
    }

    [Fact]
    public void FoodNeedingAttentionComesFirstWhateverTheGoal()
    {
        var plain = Card(1, "a", "x", [Ingredient("arroz|Gram", 100)]);
        var uses = Card(2, "b", "y", [Ingredient("iogurte|Gram", 100), Ingredient("mel|Gram", 10), Ingredient("aveia|Gram", 10)]);
        var context = Context("short") with { AttentionKeys = new HashSet<string> { "iogurte|Gram" } };

        var ranked = ShoppingPlanRanker.Rank([plain, uses], context);

        Assert.Equal(uses.Id, ranked[0].Card.Id);
        Assert.Equal(1, ranked[0].AttentionHits);
    }

    [Fact]
    public void RepeatedFormatsTechniquesAndLeadIngredientsArePenalized()
    {
        var chicken = Ingredient("frango|Gram", 300);
        var one = Card(1, "assado", "forno", [chicken], lead: "frango|Gram");
        var two = Card(2, "wrap", "montar", [chicken], lead: "frango|Gram");
        var context = Context("short", [new RankAccepted(one, 1, false), new RankAccepted(two, 1, false)]);
        var different = Card(3, "sopa", "ferver", [chicken], lead: "frango|Gram");
        var sameFormat = Card(4, "assado", "ferver", [], lead: null);
        var plain = Card(5, "salada", "misturar", [], lead: null);

        var scores = ShoppingPlanRanker.Rank([different, sameFormat, plain], context).ToDictionary(item => item.Card.Id, item => item.Score);

        Assert.Equal(scores[plain.Id] - 40, scores[sameFormat.Id]);
        Assert.Equal(5 - 60, scores[different.Id]);
    }

    [Fact]
    public void AReasonForRefusingShapesTheNextCards()
    {
        var chicken = Ingredient("frango|Gram", 300);
        var refused = Card(1, "a", "x", [chicken], active: 40, lead: "frango|Gram");
        var sameLead = Card(2, "b", "y", [chicken], active: 10, lead: "frango|Gram");
        var hard = Card(3, "c", "z", [], active: 50);
        var easy = Card(4, "d", "w", [], active: 5);
        int ScoreWith(string? reason, string? key, RankCard card) =>
            ShoppingPlanRanker.Rank([card], Context("short") with { Rejected = [new RankRejected(refused, reason, key)] })[0].Score;
        int Base(RankCard card) => ShoppingPlanRanker.Rank([card], Context("short"))[0].Score;

        Assert.Equal(Base(sameLead) - 80, ScoreWith("dislike", null, sameLead));
        Assert.Equal(Base(hard) - 60, ScoreWith("too_much_work", null, hard));
        Assert.Equal(Base(easy), ScoreWith("too_much_work", null, easy));
        Assert.Equal(Base(sameLead) - 150, ScoreWith("tired_of", "frango|Gram", sameLead));
        Assert.Equal(Base(Card(5, "a", "q", [])) - 10, ScoreWith(null, null, Card(5, "a", "q", [])));
    }

    [Fact]
    public void AProfileDislikeSinksACardButKeepsIt()
    {
        var disliked = Card(1, "a", "x", []) with { DislikeMatches = 1 };

        Assert.Equal(-200, ShoppingPlanRanker.Rank([disliked], Context("short"))[0].Score);
    }

    [Fact]
    public void ADishPastTheSessionTimeStaysButSinks()
    {
        var long_ = Card(1, "a", "x", [], active: 100);
        var short_ = Card(2, "b", "y", [], active: 15);
        var used = Card(3, "c", "z", [], active: 30);
        var context = Context("short", [new RankAccepted(used, 1, true)]) with { SessionMinutes = 60 };

        var ranked = ShoppingPlanRanker.Rank([long_, short_], context);

        Assert.Equal(short_.Id, ranked[0].Card.Id);
        Assert.True(ranked[1].OverBudget);
        Assert.False(ranked[0].OverBudget);
        Assert.Equal(30, ShoppingPlanRanker.SessionMinutesUsed(context.Accepted));
    }

    [Fact]
    public void WhatSpoilsFirstIsCookedFirst()
    {
        var soup = Sequence(4);
        var fish = Sequence(1);
        var rice = Sequence(9);

        Assert.Equal([fish.EntryId, soup.EntryId, rice.EntryId], ShoppingPlanSequencer.Order([soup, rice, fish]));
    }

    [Fact]
    public void WithinARankAttentionThenShortDishesComeFirst()
    {
        var slow = Sequence(4, minutes: 60);
        var quick = Sequence(4, minutes: 10);
        var attention = Sequence(4, minutes: 90, attention: true);

        Assert.Equal([attention.EntryId, quick.EntryId, slow.EntryId], ShoppingPlanSequencer.Order([slow, quick, attention]));
    }

    [Fact]
    public void ARepeatedDishMovesAwayFromItsCopy()
    {
        var recipe = Guid.NewGuid();
        var one = Sequence(1, recipe: recipe, minutes: 1);
        var two = Sequence(1, recipe: recipe, minutes: 2);
        var other = Sequence(4);

        Assert.Equal([one.EntryId, other.EntryId, two.EntryId], ShoppingPlanSequencer.Order([one, two, other]));
    }

    [Fact]
    public void MealsOutComeFromLunchAndDinnerFirst()
    {
        var setup = Setup(days: 5, meals: ["lunch", "dinner", "breakfast"], mealsOut: 2);

        Assert.Equal(13, setup.TotalMeals);
        var needs = setup.MealNeeds();
        Assert.Equal(8, needs["main"]);
        Assert.Equal(5, needs["breakfast"]);
    }

    [Fact]
    public void TheMenuStartsOnTheSessionDay()
    {
        var setup = Setup(days: 5, meals: ["dinner"], prep: Today.AddDays(2));

        Assert.Equal(Today.AddDays(2), setup.StartDate);
        Assert.Equal(Today.AddDays(6), setup.EndDate);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(5, 10)]
    public void InvalidDaysOrMealsOutAreRefused(int days, int mealsOut) =>
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(Command(days: days, mealsOut: mealsOut), 2));

    [Fact]
    public void TooManyMealsAreRefused() =>
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(Command(days: 14, meals: ["breakfast", "lunch", "dinner"]), 2));

    [Fact]
    public void ASessionTooFarAheadIsRefusedAndADailyPlanHasNone()
    {
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(Command(prep: Today.AddDays(8)), 2));
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(Command(prep: Today.AddDays(-1)), 2));
        var daily = ShoppingPlanApplicationService.ValidateSetup(Command(style: "daily", prep: Today.AddDays(1)), 2)!;
        Assert.Null(daily.PrepDate);
        Assert.Null(daily.PrepMinutes);
    }

    [Fact]
    public void ADeckAnswerNeedsExactlySixDifferentDishesForTheRequestedMeals()
    {
        var valid = RecipeProtocolValidator.ValidatePlanDeck(DeckJson(6));
        Assert.True(valid.IsValid, string.Join(";", valid.Errors));
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckJson(5)).IsValid);
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckJson(7)).IsValid);
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckJson(6, sameDish: true)).IsValid);
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckJson(6, strategy: "on_hand_flexible")).IsValid);
        using var request = System.Text.Json.JsonDocument.Parse("""{"planDeck":{"mealTypes":["lunch"]}}""");
        Assert.False(RecipeProtocolValidator.ValidatePlanDeck(DeckJson(6), request.RootElement).IsValid);
    }

    private static CreateShoppingPlanCommand CountsCommand(int days, Dictionary<string, int> counts, DateOnly? start = null) =>
        new(null, "test", null, Today, days, null, 0, null, null, null, null, null, 1, null, counts, start);

    [Fact]
    public void APlanFromBeforeTheCountsReadsAsItsDaysLessTheMealsOut()
    {
        var setup = Setup(5, ["lunch", "dinner", "snack"], mealsOut: 7);

        Assert.Equal(setup.Days * 3 - 7, setup.TotalMeals);
        Assert.Equal(setup.TotalMeals, setup.Counts().Values.Sum());
        // Meals out come off lunch and dinner first, the ones people eat out.
        Assert.Equal((0, 3, 5), (setup.Counts()["lunch"], setup.Counts()["dinner"], setup.Counts()["snack"]));
        Assert.Equal(3, setup.MealNeeds()["main"]);
    }

    [Fact]
    public void CountsDecideTheMealsAndThePeriodStandsOnItsOwn()
    {
        var setup = ShoppingPlanApplicationService.ValidateSetup(CountsCommand(14, new() { ["lunch"] = 3, ["dinner"] = 2, ["snack"] = 7, ["breakfast"] = 0 }, Today.AddDays(1)), 2)!;

        Assert.Equal(12, setup.TotalMeals);
        Assert.Equal((5, 7), (setup.MealNeeds()["main"], setup.MealNeeds()["snack"]));
        Assert.False(setup.MealNeeds().ContainsKey("breakfast"));
        Assert.Equal(["lunch", "dinner", "snack"], setup.MealTypes);
        Assert.Equal((Today.AddDays(1), Today.AddDays(14)), (setup.StartDate, setup.EndDate));
        // Nothing about cooking is asked up front.
        Assert.Equal(("mix", "none", null, null, 0), (setup.CookingStyle, setup.Priority, setup.PrepDate, setup.PrepMinutes, setup.MealsOut));
    }

    [Fact]
    public void CountsOutsideTheLimitsAreRefused()
    {
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(7, new() { ["snack"] = 0 }), 2));
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(7, new() { ["snack"] = 41 }), 2));
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(7, new() { ["supper"] = 2 }), 2));
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(15, new() { ["snack"] = 2 }), 2));
        Assert.Null(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(7, new() { ["snack"] = 2 }, Today.AddDays(-1)), 2));
        Assert.NotNull(ShoppingPlanApplicationService.ValidateSetup(CountsCommand(14, new() { ["snack"] = 40 }), 2));
    }

    [Fact]
    public void AGoalChosenAsAFilterCountsEvenWithThePantrySwitchedOff()
    {
        var accepted = Card(1, "a", "x", [Ingredient("tomate|Gram", 100)]);
        var fewer = Card(2, "b", "y", [Ingredient("tomate|Gram", 100)]);
        var more = Card(3, "c", "z", [Ingredient("arroz|Gram", 100), Ingredient("feijao|Gram", 100), Ingredient("couve|Gram", 100)]);
        var off = Context("short", [new RankAccepted(accepted, 1, false)]) with { PantryWeight = 0 };

        var plain = ShoppingPlanRanker.Rank([more, fewer], off with { GoalIsExplicit = false });
        var filtered = ShoppingPlanRanker.Rank([more, fewer], off with { GoalIsExplicit = true });

        Assert.Equal(fewer.Id, filtered[0].Card.Id);
        Assert.True(filtered[0].Score - filtered[1].Score > plain.Max(item => item.Score) - plain.Min(item => item.Score));
    }

    [Fact]
    public void WithThePantrySwitchedOffAttentionNoLongerDecidesEvenWithAGoal()
    {
        var plain = Card(1, "a", "x", [Ingredient("arroz|Gram", 100)]);
        var uses = Card(2, "b", "y", [Ingredient("iogurte|Gram", 100), Ingredient("mel|Gram", 10), Ingredient("aveia|Gram", 10)]);
        var context = Context("short") with { AttentionKeys = new HashSet<string> { "iogurte|Gram" }, PantryWeight = 0, GoalIsExplicit = true };

        Assert.Equal(plain.Id, ShoppingPlanRanker.Rank([uses, plain], context)[0].Card.Id);
    }

    private static string DeckJson(int count, bool sameDish = false, string strategy = "exploratory")
    {
        var candidates = Enumerable.Range(0, count).Select(index => $$"""
            {"candidateId":"c{{index}}","candidateStrategy":"{{strategy}}","name":"Prato {{index}}","targetMealType":"dinner","dishFormat":"{{(sameDish ? "bowl" : "format" + index)}}","primaryTechnique":"saute","primaryIngredientRefs":["i1"],"summary":"Um prato simples.","servings":2,"time":{"activeMinutes":10,"passiveMinutes":0,"totalMinutes":10},"difficulty":"easy","requiredEquipmentIds":[],"requiredCapabilities":[],"inventoryUses":[],"additionalIngredients":[{"name":"Arroz","requiredQuantity":100,"unit":"g","optional":false}],"preparationProfile":{"requiresAdvancePreparation":false,"minimumLeadMinutes":0,"blockingPreparationCodes":[],"mayProduceReusableComponents":false},"assumptionsUsed":[]}
            """);
        return $$"""{"operation":"recipe.suggest_plan_deck.v1","schemaVersion":"0.3","candidates":[{{string.Join(",", candidates)}}]}""";
    }

    private static CreateShoppingPlanCommand Command(int days = 5, int mealsOut = 0, string[]? meals = null, string style = "mix", DateOnly? prep = null) =>
        new(Guid.NewGuid(), "c", null, Today, days, meals ?? ["dinner"], mealsOut, style, "waste", prep, 120, null, 2);

    private static ShoppingPlanSetup Setup(int days, string[] meals, int mealsOut = 0, DateOnly? prep = null) =>
        new(Today, days, meals, mealsOut, "mix", "waste", prep, 120, null, 2);

    private static SequenceEntry Sequence(int rank, Guid? recipe = null, int minutes = 30, bool attention = false) =>
        new(Guid.NewGuid(), recipe ?? Guid.NewGuid(), rank, attention, minutes, DateTimeOffset.UnixEpoch);

    private static RankIngredient Ingredient(string key, decimal quantity, bool perishable = false, bool costly = false) =>
        new(key, key, quantity, perishable, costly);

    private static RankCard Card(int order, string format, string technique, IReadOnlyList<RankIngredient> ingredients, int active = 20, int total = 30, string? lead = null) =>
        new(Guid.NewGuid(), order, format, technique, lead, active, total, ingredients);

    private static RankContext Context(string priority, IReadOnlyList<RankAccepted>? accepted = null) =>
        new(priority, accepted ?? [], [], new Dictionary<string, decimal>(), new HashSet<string>(), null);
}
