using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0029 Phase 4: package sizes and remainders, per-meal shortfalls, and pre-meal reminders.</summary>
public sealed class PurchaseAndPreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);

    [Fact]
    public void AConfirmedPackageSizeGivesWholePackagesAndTheRemainderAndNothingIsAssumedOtherwise()
    {
        var owner = Guid.NewGuid();
        var chicken = Lot("Peito de frango", 200m, "Gram");
        var stew = Shopping("Ensopado", 3, Ingredient("i1", "Peito de frango", 600, "g"), Ingredient("i2", "Cebola", 2, "unit"));
        var key = ShoppingProjector.Key("Peito de frango", "Gram");
        var sizes = new Dictionary<string, PackageSize> { [key] = PackageSize.Create(owner, key, 1000m, Now) };

        var items = ShoppingProjector.Build([stew], [chicken], new HashSet<string>(), packageSizes: sizes);

        // The domain example: need 600 g, have 200 g, buy 400 g; one 1 kg package leaves 600 g after the menu.
        var line = items.Single(item => item.ItemKey == key);
        Assert.Equal((400m, 1000m, 1, 600m), (line.ToBuyQuantity, line.PackageSize, line.PackageCount, line.ExpectedRemainder));
        var onion = items.Single(item => item.DisplayName == "Cebola");
        Assert.Equal(((decimal?)null, (int?)null, (decimal?)null), (onion.PackageSize, onion.PackageCount, onion.ExpectedRemainder));

        Assert.Equal((3, 50m), PackageSize.Create(owner, key, 250m, Now).Cover(700m));
        Assert.Equal((0, 0m), PackageSize.Create(owner, key, 250m, Now).Cover(0m));
        Assert.Throws<ArgumentException>(() => PackageSize.Create(owner, key, 0m, Now));
        Assert.Throws<ArgumentException>(() => PackageSize.Create(owner, key, 100_001m, Now));
    }

    [Fact]
    public void EachUseSaysWhatThatMealStillLacksInPlanOrder()
    {
        var rice = Lot("Arroz", 300m, "Gram");
        var first = Shopping("Risoto", 3, Ingredient("i1", "Arroz", 200, "g"));
        var second = Shopping("Arroz doce", 4, Ingredient("i1", "Arroz", 250, "g"));

        var line = ShoppingProjector.Build([first, second], [rice], new HashSet<string>()).Single();

        // The first meal is covered; the second gets the 100 g left and lacks 150 g.
        Assert.Equal(150m, line.ToBuyQuantity);
        Assert.Equal([("Risoto", 0m), ("Arroz doce", 150m)], line.Uses.Select(use => (use.RecipeName, use.MissingQuantity)));
    }

    [Fact]
    public void RemindersSayWhatToBuyThawAndUseBeforeEachDatedMeal()
    {
        var frozen = Lot("Peito de frango", 500m, "Gram", storage: "Freezer");
        var thawing = Lot("Carne moída", 400m, "Gram", storage: "Refrigerator", state: "Thawing");
        var cream = Lot("Creme de leite", 200m, "Gram", printed: Today.AddDays(1));
        var tomorrow = Reserving("Frango cremoso", Today.AddDays(1), Ingredient("i1", "Peito de frango", 400, "g", frozen.LotId), Ingredient("i2", "Cebola", 1, "unit"));
        var later = Reserving("Escondidinho", Today.AddDays(3), Ingredient("i1", "Carne moída", 400, "g", thawing.LotId), Ingredient("i2", "Creme de leite", 200, "g", cream.LotId));
        var lots = new[] { frozen, thawing, cream };
        var projection = ReservationProjector.Build([tomorrow, later], lots, []);
        var plan = Guid.NewGuid();

        var reminders = PreparationReminders.Build([ToReminder(plan, tomorrow), ToReminder(plan, later)], projection, lots, Today);

        Assert.Equal(
            [
                ("Frango cremoso", "thaw", "Peito de frango", (decimal?)400m, Today),
                ("Frango cremoso", "buy", "Cebola", 1m, Today),
                ("Escondidinho", "use_by", "Creme de leite", 200m, Today.AddDays(1)),
            ],
            reminders.Select(item => (item.RecipeName, item.Kind, item.ItemName, item.Quantity, item.DueDate)));
        // The cream's own date is reported, and the lot already thawing needs no reminder.
        Assert.Equal(Today.AddDays(1), reminders.Single(item => item.Kind == "use_by").UseByDate);
        Assert.DoesNotContain(reminders, item => item.ItemName == "Carne moída");
    }

    private static ShoppingMeal Shopping(string name, int day, params string[] ingredients) =>
        new(Guid.NewGuid(), name, new DateOnly(2026, 11, day), 2, 2, "{\"ingredients\":[" + string.Join(",", ingredients) + "]}");

    private static ReservingMeal Reserving(string name, DateOnly day, params string[] ingredients) =>
        new(Guid.NewGuid(), name, day, 2, 2, "{\"ingredients\":[" + string.Join(",", ingredients) + "]}");

    private static ReminderMeal ToReminder(Guid plan, ReservingMeal meal) => new(plan, meal.EntryId, meal.RecipeName, meal.PlannedDate!.Value, "dinner");

    private static string Ingredient(string id, string name, decimal quantity, string unit, Guid? lotId = null) =>
        $"{{\"ingredientId\":\"{id}\",\"sourceType\":\"{(lotId is null ? "additional" : "inventory")}\",\"displayName\":\"{name}\",{(lotId is { } lot ? $"\"inventoryItemId\":\"{lot:n}\"," : string.Empty)}\"requiredQuantity\":{quantity},\"unit\":\"{unit}\",\"optional\":false}}";

    private static InventoryLotReadModel Lot(string name, decimal quantity, string unit = "Gram", DateOnly? printed = null, string storage = "Pantry", string? state = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, quantity, unit, null, storage, null, null, printed, null, Guid.NewGuid(), Now, Now, null,
            state is null ? null : new InventoryLifecycleView(state, null, null, null, null, null));
}
