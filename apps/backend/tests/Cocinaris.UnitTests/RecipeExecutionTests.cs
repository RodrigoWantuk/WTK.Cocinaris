using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0042 Phase 1: recipe execution state and the deterministic completion proposal.</summary>
public sealed class RecipeExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProposalScalesToServingsRoundsUnitsUpAndNeverExceedsTheLot()
    {
        var eggs = Lot("Eggs", 2m, "Unit");
        var flour = Lot("Flour", 1000m, "Gram");
        var milk = Lot("Milk", 500m, "Milliliter");
        var json = Recipe(
            Inventory("ing-eggs", eggs.LotId, 3, "unit"),
            Inventory("ing-flour", flour.LotId, 250, "g"),
            Inventory("ing-milk", milk.LotId, 333, "ml"),
            """{"ingredientId":"ing-sugar","sourceType":"additional","displayName":"Sugar","requiredQuantity":40,"unit":"g","optional":true}""");

        var proposal = RecipeExecutionApplicationService.BuildProposal(json, recipeServings: 4, servings: 2, [eggs, flour, milk]);
        var lines = proposal.Lines.ToDictionary(line => line.IngredientId);

        Assert.Equal((2m, "ready", 2m), (lines["ing-eggs"].RequiredQuantity, lines["ing-eggs"].Status, lines["ing-eggs"].ProposedQuantity!.Value));
        Assert.Equal((125m, "ready"), (lines["ing-flour"].RequiredQuantity, lines["ing-flour"].Status));
        Assert.Equal(167m, lines["ing-milk"].RequiredQuantity);
        Assert.Equal(("not_tracked", true), (lines["ing-sugar"].Status, lines["ing-sugar"].Optional));
        Assert.Equal(["Eggs", "Flour", "Milk"], proposal.PantryLots.Select(lot => lot.ProductName));

        var doubled = RecipeExecutionApplicationService.BuildProposal(json, recipeServings: 4, servings: 8, [eggs, flour, milk]);
        var eggsLine = doubled.Lines.Single(line => line.IngredientId == "ing-eggs");
        Assert.Equal((6m, "insufficient", 2m), (eggsLine.RequiredQuantity, eggsLine.Status, eggsLine.ProposedQuantity!.Value));
    }

    [Fact]
    public void ALotThatIsGoneOrInAnotherUnitIsUnavailableAndMalformedContentGivesNoLines()
    {
        var milk = Lot("Milk", 500m, "Milliliter");
        var json = Recipe(Inventory("ing-gone", Guid.NewGuid(), 100, "g"), Inventory("ing-milk", milk.LotId, 100, "g"));

        var proposal = RecipeExecutionApplicationService.BuildProposal(json, 2, 2, [milk]);

        Assert.All(proposal.Lines, line => Assert.Equal("lot_unavailable", line.Status));
        Assert.All(proposal.Lines, line => Assert.Null(line.LotId));
        Assert.Empty(RecipeExecutionApplicationService.BuildProposal("not json", 2, 2, [milk]).Lines);
    }

    [Fact]
    public void AnExecutionFinishesOnceAndValidatesRatingAndNotes()
    {
        var execution = RecipeExecution.Start(Guid.NewGuid(), Guid.NewGuid(), 1, 2, Now);
        var version = execution.Version;

        Assert.Throws<ArgumentException>(() => execution.Complete(Guid.NewGuid(), null, 6, null, Now));
        Assert.Throws<ArgumentException>(() => execution.Complete(Guid.NewGuid(), null, null, new string('x', 1001), Now));
        execution.Complete(Guid.NewGuid(), Guid.NewGuid(), 4, "  good  ", Now.AddHours(1));

        Assert.Equal((RecipeExecutionStatus.Completed, "good", Now.AddHours(1)), (execution.Status, execution.Notes, execution.FinishedAt!.Value));
        Assert.NotEqual(version, execution.Version);
        Assert.Throws<InvalidOperationException>(() => execution.Abandon(Now));
        Assert.Throws<ArgumentException>(() => RecipeExecution.Start(Guid.NewGuid(), Guid.NewGuid(), 1, 25, Now));
    }

    private static InventoryLotReadModel Lot(string name, decimal value, string unit) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, value, unit, null, "Refrigerator", null, null, null, null, Guid.NewGuid(), Now, Now, null);

    private static string Inventory(string id, Guid lotId, decimal quantity, string unit) =>
        $$"""{"ingredientId":"{{id}}","sourceType":"inventory","displayName":"{{id}}","inventoryItemId":"{{lotId:n}}","requiredQuantity":{{quantity}},"unit":"{{unit}}","optional":false}""";

    private static string Recipe(params string[] ingredients) => "{\"recipe\":{\"ingredients\":[" + string.Join(",", ingredients) + "]}}";
}
