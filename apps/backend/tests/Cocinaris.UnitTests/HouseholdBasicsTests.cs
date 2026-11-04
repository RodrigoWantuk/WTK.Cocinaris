using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0061: salt and water are never unavailable and never to buy.</summary>
public sealed class HouseholdBasicsTests
{
    [Theory]
    [InlineData("Sal")]
    [InlineData("sal a gosto")]
    [InlineData("Sal (a gosto)")]
    [InlineData("sal grosso")]
    [InlineData("1 pitada de sal")]
    [InlineData("Salt")]
    [InlineData("salt to taste")]
    [InlineData("Água")]
    [InlineData("agua")]
    [InlineData("água morna")]
    [InlineData("Água filtrada")]
    [InlineData("2 xícaras de água")]
    [InlineData("cold water")]
    [InlineData("Water")]
    [InlineData("agua caliente")]
    public void PlainSaltAndWaterAreBasics(string name) => Assert.True(HouseholdBasics.Is(name));

    [Theory]
    [InlineData("Água de coco")]
    [InlineData("Água com gás")]
    [InlineData("água mineral")]
    [InlineData("coconut water")]
    [InlineData("sparkling water")]
    [InlineData("Flor de sal")]
    [InlineData("Sal e pimenta")]
    [InlineData("salt and pepper")]
    [InlineData("Sal de parrilla")]
    [InlineData("Manteiga com sal")]
    [InlineData("Salsa")]
    [InlineData("Salmão")]
    [InlineData("Bacalhau salgado")]
    [InlineData("a gosto")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsAnIngredient(string? name) => Assert.False(HouseholdBasics.Is(name));

    private static string Recipe(params (string Id, string Source, string Name, decimal Quantity, string Unit)[] items) =>
        "{\"ingredients\":[" + string.Join(',', items.Select(item =>
            $"{{\"ingredientId\":\"{item.Id}\",\"sourceType\":\"{item.Source}\",\"displayName\":\"{item.Name}\",\"requiredQuantity\":{item.Quantity},\"unit\":\"{item.Unit}\",\"optional\":false}}")) + "]}";

    [Fact]
    public void TheReaderTreatsBasicsAsAssumedWhateverTheRecipeDeclared()
    {
        var ingredients = RecipeIngredientReader.Read(Recipe(("i1", "additional", "Sal", 5, "g"), ("i2", "inventory", "Água", 500, "ml"), ("i3", "additional", "Arroz", 200, "g"))).ToList();
        Assert.Equal(["assumption", "assumption", "additional"], ingredients.Select(item => item.SourceType));
    }

    [Fact]
    public void TheShoppingListLeavesOutMissingBasicsAndKeepsRealIngredients()
    {
        var meal = new ShoppingMeal(Guid.NewGuid(), "Arroz", null, 2, 2, Recipe(("i1", "additional", "Sal", 5, "g"), ("i2", "additional", "Água", 500, "ml"), ("i3", "additional", "Arroz", 200, "g"), ("i4", "additional", "Água de coco", 200, "ml")));
        var items = ShoppingProjector.Build([meal], [], new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal(["Água de coco", "Arroz"], items.Select(item => item.DisplayName));
    }

    [Fact]
    public void BasicsDoNotCountAsPurchasesWhenAdaptingToThePantry()
    {
        var candidate = new SuggestCandidate
        {
            CandidateId = "c1",
            CandidateStrategy = "on_hand_first",
            Name = "Arroz",
            TargetMealType = "dinner",
            DishFormat = "bowl",
            PrimaryTechnique = "simmer",
            PrimaryIngredientRefs = ["rice"],
            Summary = "Arroz soltinho.",
            Servings = 2,
            Time = new SuggestTimeProfile { ActiveMinutes = 5, PassiveMinutes = 5, TotalMinutes = 10 },
            Difficulty = "easy",
            RequiredEquipmentIds = [],
            RequiredCapabilities = [],
            InventoryUses = [],
            AdditionalIngredients =
            [
                new SuggestAdditionalIngredient { Name = "Sal", RequiredQuantity = 5, Unit = "g", Optional = false },
                new SuggestAdditionalIngredient { Name = "Água", RequiredQuantity = 500, Unit = "ml", Optional = false },
                new SuggestAdditionalIngredient { Name = "Alho", RequiredQuantity = 1, Unit = "unit", Optional = false }
            ],
            PreparationProfile = new SuggestPreparationProfile { RequiresAdvancePreparation = false, MinimumLeadMinutes = 0, BlockingPreparationCodes = [], MayProduceReusableComponents = false },
            AssumptionsUsed = []
        };
        var response = new SuggestCandidatesResponse { Operation = "recipe.suggest_candidates.v1", SchemaVersion = "0.3", Candidates = [candidate], Clarifications = [] };
        Assert.Empty(RecipeOutputQuality.CheckSuggest(response, "pt-BR", "adapt_to_pantry"));
    }
}
