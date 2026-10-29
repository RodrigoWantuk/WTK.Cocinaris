using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0029 Phase 3: derived menu reservations, protections, readiness, and the cooking proposal's view of them.</summary>
public sealed class ReservationProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MealsReserveInOrderFromFreeStockAndProtectionComesFirst()
    {
        var owner = Guid.NewGuid();
        var older = Lot("Feijão", 300m, "Gram", printed: new DateOnly(2026, 11, 1));
        var newer = Lot("feijao", 500m, "Gram", printed: new DateOnly(2026, 12, 1));
        var first = Meal("Feijoada", 1, Ingredient("i1", "additional", "Feijão", 400, "g"));
        var second = Meal("Tutu", 2, Ingredient("i1", "additional", "Feijão", 300, "g"), Ingredient("i2", "assumption", "Sal", 5, "g"), Ingredient("i3", "additional", "Couve", 1, "unit", optional: true));

        var projection = ReservationProjector.Build([first, second], [newer, older], [LotProtection.Create(owner, newer.LotId, false, 200m, Now)]);

        // Feijoada takes the earliest-dated lot (300 g) and 100 g of the newer one; 200 g of the newer one is protected.
        Assert.Equal([(first.EntryId, 300m)], projection.Lots[older.LotId].Reservations.Select(item => (item.MenuEntryId, item.Quantity)));
        var newerCommitment = projection.Lots[newer.LotId];
        Assert.Equal(200m, newerCommitment.ProtectedQuantity);
        Assert.Equal([(first.EntryId, 100m), (second.EntryId, 200m)], newerCommitment.Reservations.Select(item => (item.MenuEntryId, item.Quantity)));
        Assert.Equal("ready", projection.Meals[first.EntryId].Readiness);
        // Tutu lacks 100 g of beans; salt (assumed) and kale (optional) are never reported missing.
        var tutu = projection.Meals[second.EntryId];
        Assert.Equal(("missing", "Feijão", 100m), (tutu.Readiness, tutu.Missing.Single().Name, tutu.Missing.Single().Quantity));
    }

    [Fact]
    public void TheLinkedLotIsReservedBeforeSimilarLotsAndWholeProtectionBlocksIt()
    {
        var owner = Guid.NewGuid();
        var linked = Lot("Frango caipira", 500m, "Gram");
        var other = Lot("Chicken", 1000m, "Gram");
        var meal = Meal("Assado", 1, Ingredient("i1", "inventory", "Chicken", 400, "g", lotId: linked.LotId));

        var projection = ReservationProjector.Build([meal], [other, linked], []);
        Assert.Equal(400m, projection.Lots[linked.LotId].Reservations.Single().Quantity);
        Assert.False(projection.Lots.ContainsKey(other.LotId));

        var blocked = ReservationProjector.Build([meal], [other, linked], [LotProtection.Create(owner, linked.LotId, true, null, Now)]);
        Assert.Equal(500m, blocked.Lots[linked.LotId].ProtectedQuantity);
        Assert.Empty(blocked.Lots[linked.LotId].Reservations);
        Assert.Equal(("missing", "Frango caipira"), (blocked.Meals[meal.EntryId].Readiness, blocked.Meals[meal.EntryId].Missing.Single().Name));
        Assert.Throws<ArgumentException>(() => LotProtection.Create(owner, linked.LotId, true, 10m, Now));
    }

    [Fact]
    public void TheProposalNeverProposesProtectedQuantityAndWarnsAboutOtherMeals()
    {
        var lot = Lot("Arroz", 500m, "Gram");
        var own = Guid.NewGuid();
        var json = "{\"ingredients\":[" + Ingredient("i1", "inventory", "Arroz", 400, "g", lotId: lot.LotId) + "]}";
        var commitments = new Dictionary<Guid, LotCommitment>
        {
            [lot.LotId] = new(lot.LotId, 200m, [new LotReservationView(own, "Este", null, 100m), new LotReservationView(Guid.NewGuid(), "Risoto", new DateOnly(2026, 11, 2), 150m)])
        };

        var proposal = RecipeExecutionApplicationService.BuildProposal(json, 2, 2, [lot], commitments, own);

        var line = proposal.Lines.Single();
        Assert.Equal(("insufficient", 300m, 200m), (line.Status, line.ProposedQuantity, line.ProtectedQuantity));
        Assert.Equal(["Risoto"], line.ReservedElsewhere!.Select(item => item.RecipeName));
        Assert.Equal((200m, 150m), (proposal.PantryLots.Single().ProtectedQuantity, proposal.PantryLots.Single().ReservedQuantity));

        var locked = new Dictionary<Guid, LotCommitment> { [lot.LotId] = new(lot.LotId, 500m, []) };
        var blocked = RecipeExecutionApplicationService.BuildProposal(json, 2, 2, [lot], locked);
        Assert.Equal(("protected", (decimal?)null), (blocked.Lines.Single().Status, blocked.Lines.Single().ProposedQuantity));
    }

    [Fact]
    public void TheProposalMatchesAnIngredientWithoutItsOwnLotByNameAndUnit()
    {
        var eggs = Lot("Ovos", 6m, "Unit");
        var milk = Lot("Leite", 1000m, "Gram");
        var json = "{\"ingredients\":[" + Ingredient("i1", "additional", " OVOS ", 2, "unit") + "," + Ingredient("i2", "additional", "Leite", 200, "ml") + "," + Ingredient("i3", "assumption", "Sal", 5, "g") + "]}";

        var lines = RecipeExecutionApplicationService.BuildProposal(json, 1, 1, [eggs, milk]).Lines;

        Assert.Equal(("ready", eggs.LotId, 2m), (lines[0].Status, lines[0].LotId, lines[0].ProposedQuantity));
        // Milk is stocked in grams; a recipe in millilitres is not matched to it.
        Assert.Equal(("not_tracked", (Guid?)null), (lines[1].Status, lines[1].LotId));
        Assert.Equal("not_tracked", lines[2].Status);
    }

    private static ReservingMeal Meal(string name, int day, params string[] ingredients) =>
        new(Guid.NewGuid(), name, new DateOnly(2026, 11, day), 2, 2, "{\"ingredients\":[" + string.Join(",", ingredients) + "]}");

    private static string Ingredient(string id, string source, string name, decimal quantity, string unit, bool optional = false, Guid? lotId = null) =>
        $"{{\"ingredientId\":\"{id}\",\"sourceType\":\"{source}\",\"displayName\":\"{name}\",{(lotId is { } lot ? $"\"inventoryItemId\":\"{lot:n}\"," : string.Empty)}\"requiredQuantity\":{quantity},\"unit\":\"{unit}\",\"optional\":{(optional ? "true" : "false")}}}";

    private static InventoryLotReadModel Lot(string name, decimal quantity, string unit, DateOnly? printed = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, quantity, unit, null, "Pantry", null, null, printed, null, Guid.NewGuid(), Now, Now, null);
}
