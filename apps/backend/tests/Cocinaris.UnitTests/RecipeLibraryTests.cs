using System.Text.Json;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0038: deterministic recipe content composition and library domain rules.</summary>
public sealed class RecipeLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private const string Base = """
        {"recipe":{"recipeId":"r-1","revision":1,"name":"Old","mealTypes":["lunch"],"servings":2,"yield":"",
         "ingredients":[
           {"ingredientId":"ing-chicken","sourceType":"inventory","displayName":"Chicken","inventoryItemId":"0f8fad5bd9cb469fa16570867728950e","requiredQuantity":300,"unit":"g","optional":false},
           {"ingredientId":"ing-salt","sourceType":"assumption","displayName":"Salt","requiredQuantity":5,"unit":"g","optional":false}],
         "equipment":[{"equipmentId":"eq-pan","name":"Pan","capabilities":["saute"]}],
         "preparations":[],
         "stages":[
           {"stageId":"s-a","name":"Prep","instructions":"Cut.","activeMinutes":5,"passiveMinutes":0,"dependsOn":[],"sensoryCues":[],"usesIngredientIds":["ing-chicken","ing-salt"]},
           {"stageId":"s-b","name":"Cook","instructions":"Fry.","activeMinutes":10,"passiveMinutes":0,"dependsOn":["s-a"],"sensoryCues":[],"usesIngredientIds":["ing-chicken"]}],
         "dependencies":[{"fromId":"s-a","toId":"s-b","relationship":"before"}],
         "storage":{"refrigerateHours":48,"freezeCompatible":true,"reheatNotes":"Pan"},
         "assumptions":["Salt to taste"]}}
        """;

    [Fact]
    public void EditingKeepsPantryLinksAndHiddenFieldsAndDropsReferencesToRemovedRows()
    {
        var draft = new RecipeContentDraft(
            "New",
            ["lunch", "lunch", "dinner"],
            3,
            "3 plates",
            [new("ing-chicken", "Chicken thighs", 450m, "g", false), new(null, "Lime", 1m, "unit", true)],
            ["pan", "Oven tray"],
            [new("s-b", "Cook", "Fry.\nRest.", 12, 3, ["Golden"]), new(null, "Serve", "Plate it.", 2, 0, null)]);

        var composition = RecipeContentComposer.Compose(Base, draft, "ignored", 2);

        Assert.True(composition.IsValid);
        using var document = JsonDocument.Parse(composition.NormalizedRecipeJson!);
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("recipe", out _));
        Assert.Equal((2, "New", 3), (root.GetProperty("revision").GetInt32(), root.GetProperty("name").GetString(), root.GetProperty("servings").GetInt32()));
        Assert.Equal(["lunch", "dinner"], root.GetProperty("mealTypes").EnumerateArray().Select(item => item.GetString()));

        var chicken = root.GetProperty("ingredients")[0];
        Assert.Equal(("inventory", "0f8fad5bd9cb469fa16570867728950e", 450m), (chicken.GetProperty("sourceType").GetString(), chicken.GetProperty("inventoryItemId").GetString(), chicken.GetProperty("requiredQuantity").GetDecimal()));
        var lime = root.GetProperty("ingredients")[1];
        Assert.Equal(("ing-1", "additional"), (lime.GetProperty("ingredientId").GetString(), lime.GetProperty("sourceType").GetString()));
        Assert.False(lime.TryGetProperty("inventoryItemId", out _));

        var cook = root.GetProperty("stages")[0];
        Assert.Empty(cook.GetProperty("dependsOn").EnumerateArray());
        Assert.Equal(["ing-chicken"], cook.GetProperty("usesIngredientIds").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["s-b"], root.GetProperty("stages")[1].GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Empty(root.GetProperty("dependencies").EnumerateArray());

        Assert.Equal([("eq-pan", "pan"), ("eq-1", "Oven tray")], root.GetProperty("equipment").EnumerateArray().Select(item => (item.GetProperty("equipmentId").GetString(), item.GetProperty("name").GetString())));
        Assert.Equal("saute", root.GetProperty("equipment")[0].GetProperty("capabilities")[0].GetString());
        Assert.Equal(48, root.GetProperty("storage").GetProperty("refrigerateHours").GetInt32());
        Assert.Equal("Salt to taste", root.GetProperty("assumptions")[0].GetString());
    }

    [Fact]
    public void InvalidDraftsReportEveryFieldAndProduceNoContent()
    {
        var draft = new RecipeContentDraft(
            " ",
            ["brunch"],
            0,
            null,
            [new(null, "Rice", 0m, "kg", false)],
            null,
            [new(null, "Cook", new string('x', RecipeContentComposer.MaxInstructionsLength + 1), -1, 0, null)]);

        var composition = RecipeContentComposer.Compose(null, draft, "r", 1);

        Assert.False(composition.IsValid);
        Assert.Equal(
            ["ingredients[0].quantity", "ingredients[0].unit", "mealTypes", "name", "servings", "stages[0].instructions", "stages[0].minutes"],
            composition.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ManualRecipesStartFromAnEmptySkeletonAndDuplicatedRowsBecomeNewRows()
    {
        var draft = new RecipeContentDraft("Beans", ["dinner"], 4, null, [new("x", "Beans", 500m, "g", false), new("x", "Water", 1000m, "ml", false)], null, [new(null, "Soak", "Soak overnight.", 5, 600, null)]);

        var composition = RecipeContentComposer.Compose(null, draft, "abc", 1);

        using var document = JsonDocument.Parse(composition.NormalizedRecipeJson!);
        var root = document.RootElement;
        Assert.Equal("abc", root.GetProperty("recipeId").GetString());
        Assert.Equal(["ing-1", "ing-2"], root.GetProperty("ingredients").EnumerateArray().Select(item => item.GetProperty("ingredientId").GetString()));
        Assert.All(root.GetProperty("ingredients").EnumerateArray(), item => Assert.Equal("additional", item.GetProperty("sourceType").GetString()));
    }

    [Fact]
    public void DerivedRecipesNeedAReasonAndAuthoredRevisionsFollowTheirOrigin()
    {
        var parent = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Recipe.CreateDerived(Guid.NewGuid(), Guid.NewGuid(), parent, 1, " ", Now));
        var derived = Recipe.CreateDerived(Guid.NewGuid(), Guid.NewGuid(), parent, 2, " Air-fryer ", Now);
        Assert.Equal((RecipeOrigin.Derived, parent, 2, "Air-fryer"), (derived.Origin, derived.ParentRecipeId, derived.ParentRevisionNumber, derived.DerivationReason));

        Assert.Throws<ArgumentException>(() => RecipeRevision.CreateAuthored(derived.Id, derived.OwnerUserId, 2, RecipeRevisionOrigin.Derived, "A", ["lunch"], 2, "{}", "{}", null, null, Now));
        Assert.Throws<ArgumentException>(() => RecipeRevision.CreateAuthored(derived.Id, derived.OwnerUserId, 1, RecipeRevisionOrigin.Edited, "A", ["lunch"], 2, "{}", "{}", null, null, Now));
        Assert.Null(RecipeRevision.CreateAuthored(derived.Id, derived.OwnerUserId, 2, RecipeRevisionOrigin.Edited, "A", ["lunch"], 2, "{}", "{}", " ", null, Now).ChangeReason);

        derived.SetArchived(true, Now);
        derived.SetArchived(true, Now.AddDays(1));
        Assert.Equal(Now, derived.ArchivedAt);
        derived.SetArchived(false, Now);
        Assert.Null(derived.ArchivedAt);
    }
}
