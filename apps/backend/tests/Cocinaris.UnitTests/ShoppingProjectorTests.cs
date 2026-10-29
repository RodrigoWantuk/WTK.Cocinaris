using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0029: the shopping projection and menu domain rules.</summary>
public sealed class ShoppingProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThePantryIsAllocatedMealByMealAndOnlyTheRestIsBought()
    {
        var beans = Lot("Feijão preto", 300m, "Gram");
        var meals = new[]
        {
            // Two portions of a four-portion recipe: 250 g of the 500 g it lists.
            Meal("Feijoada", new DateOnly(2026, 10, 30), 2, 4, Ingredient("i1", "inventory", "Beans", 500, "g", lotId: beans.LotId), Ingredient("i2", "additional", "Laranja", 1, "unit")),
            Meal("Tutu", new DateOnly(2026, 10, 31), 2, 2, Ingredient("i1", "additional", " feijao   PRETO ", 200, "g"), Ingredient("i2", "assumption", "Sal", 5, "g"))
        };

        var items = ShoppingProjector.Build(meals, [beans, Lot("Sal", 1000m, "Gram")], new HashSet<string>(["laranja|Unit"]));

        var bean = items.Single(item => item.ItemKey == "feijao preto|Gram");
        Assert.Equal(("required", 450m, 300m, 150m), (bean.Category, bean.RequiredQuantity, bean.AvailableQuantity, bean.ToBuyQuantity));
        Assert.Equal(["Feijoada", "Tutu"], bean.Uses.Select(use => use.RecipeName));
        Assert.Equal("Feijão preto", bean.DisplayName);
        var orange = items.Single(item => item.ItemKey == "laranja|Unit");
        Assert.Equal(("required", 1m, true), (orange.Category, orange.ToBuyQuantity, orange.Checked));
        Assert.Equal(("available", 0m), (items.Single(item => item.ItemKey == "sal|Gram").Category, items.Single(item => item.ItemKey == "sal|Gram").ToBuyQuantity));
        Assert.Equal("required", items[0].Category);
    }

    [Fact]
    public void CategoriesSeparateOptionalAssumedAndStockThePlanUsesUp()
    {
        var meals = new[]
        {
            Meal("A", null, 1, 1,
                Ingredient("i1", "additional", "Coentro", 10, "g", optional: true),
                Ingredient("i2", "assumption", "Azeite", 15, "ml"),
                Ingredient("i3", "additional", "Ovos", 2, "unit"),
                Ingredient("i4", "additional", "Leite", 200, "ml"))
        };

        var items = ShoppingProjector.Build(meals, [Lot("Ovos", 2m, "Unit"), Lot("Leite", 200m, "Gram")], new HashSet<string>());

        Assert.Equal("optional", items.Single(item => item.DisplayName == "Coentro").Category);
        Assert.Equal("assumed", items.Single(item => item.DisplayName == "Azeite").Category);
        Assert.Equal("runs_out", items.Single(item => item.DisplayName == "Ovos").Category);
        // Milk is stocked in grams; a recipe in millilitres is not matched against it.
        Assert.Equal((200m, 0m), (items.Single(item => item.DisplayName == "Leite").ToBuyQuantity, items.Single(item => item.DisplayName == "Leite").AvailableQuantity));
    }

    [Fact]
    public void EntriesStayInsideThePlanAndArchivedPlansAreReadOnly()
    {
        Assert.Throws<ArgumentException>(() => MenuPlan.Create(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 2), Now));
        Assert.Equal(new DateOnly(2026, 12, 1), MenuPlan.Create(Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), Now).EndDate);
        var plan = MenuPlan.Create(Guid.NewGuid(), Guid.NewGuid(), " Semana ", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), Now);
        Assert.Equal("Semana", plan.Name);
        Assert.Throws<ArgumentException>(() => MenuEntry.Create(plan, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 11, 9), null, 2, Now));
        Assert.Throws<ArgumentException>(() => MenuEntry.Create(plan, Guid.NewGuid(), Guid.NewGuid(), null, null, "brunch", 2, Now));
        var entry = MenuEntry.Create(plan, Guid.NewGuid(), Guid.NewGuid(), null, null, "dinner", 2, Now);

        plan.Accept(Now);
        Assert.Equal((MenuPlanStatus.Accepted, Now), (plan.Status, plan.AcceptedAt));
        plan.Archive(Now);
        Assert.Throws<InvalidOperationException>(() => entry.Change(plan, null, null, 3, MenuEntryStatus.Skipped, Now));
        Assert.Throws<InvalidOperationException>(() => plan.Accept(Now));
    }

    private static ShoppingMeal Meal(string name, DateOnly? date, int servings, int recipeServings, params string[] ingredients) =>
        new(Guid.NewGuid(), name, date, servings, recipeServings, "{\"ingredients\":[" + string.Join(",", ingredients) + "]}");

    private static string Ingredient(string id, string source, string name, decimal quantity, string unit, bool optional = false, Guid? lotId = null) =>
        $"{{\"ingredientId\":\"{id}\",\"sourceType\":\"{source}\",\"displayName\":\"{name}\",{(lotId is { } lot ? $"\"inventoryItemId\":\"{lot:n}\"," : string.Empty)}\"requiredQuantity\":{quantity},\"unit\":\"{unit}\",\"optional\":{(optional ? "true" : "false")}}}";

    private static InventoryLotReadModel Lot(string name, decimal quantity, string unit) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, quantity, unit, null, "Pantry", null, null, null, null, Guid.NewGuid(), Now, Now, null);
}
