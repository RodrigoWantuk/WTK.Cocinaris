using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0046 Phase 1: reading a typed pantry list without AI.</summary>
public sealed class PantryListParserTests
{
    [Theory]
    [InlineData("2 kg de arroz", "Arroz", "2000", "Gram")]
    [InlineData("arroz 2kg", "Arroz", "2000", "Gram")]
    [InlineData("6 ovos", "Ovos", "6", "Unit")]
    [InlineData("Leite integral 1 L", "Leite integral", "1000", "Milliliter")]
    [InlineData("leite - 1,5 litros", "Leite", "1500", "Milliliter")]
    [InlineData("meia dúzia de ovos", "Ovos", "6", "Unit")]
    [InlineData("500g carne moída", "Carne moída", "500", "Gram")]
    [InlineData("2 ovos caipira", "Ovos caipira", "2", "Unit")]
    [InlineData("- 300 ml creme de leite", "Creme de leite", "300", "Milliliter")]
    [InlineData("un kilo de harina", "Harina", "1000", "Gram")]
    [InlineData("2 lb flour", "2 lb flour", null, null)]
    [InlineData("1 xícara de açúcar", "1 xícara de açúcar", null, null)]
    public void ReadsTheAmountBeforeOrAfterTheName(string line, string name, string? quantity, string? unit)
    {
        var parsed = Assert.Single(PantryListParser.Parse(line, IngredientCatalog.Default));
        Assert.Equal(name, parsed.Name);
        Assert.Equal(quantity is null ? null : decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), parsed.Quantity);
        Assert.Equal(unit, parsed.Unit);
    }

    [Fact]
    public void KeepsUnreadableLinesSkipsBlanksAndSuggestsThePlace()
    {
        var lines = PantryListParser.Parse("pão\n\n• 1 frango congelado\n3 tomates\nmanteiga 200g\n", IngredientCatalog.Default);

        Assert.Equal([1, 3, 4, 5], lines.Select(line => line.LineNumber));
        Assert.Equal(("Pão", (decimal?)null), (lines[0].Name, lines[0].Quantity));
        Assert.Equal("Freezer", lines[1].StorageLocation);
        Assert.True(lines[2].Recognized);
        Assert.Equal("Pantry", lines[2].StorageLocation);
        Assert.Equal("Refrigerator", lines[3].StorageLocation);
    }

    [Fact]
    public void BoundsHostileInput()
    {
        var text = string.Join('\n', Enumerable.Range(1, 200).Select(index => $"item {index}"));
        Assert.Equal(PantryListParser.MaxLines, PantryListParser.Parse(text, IngredientCatalog.Default).Count);
        var longLine = Assert.Single(PantryListParser.Parse(new string('a', 5000), IngredientCatalog.Default));
        Assert.True(longLine.Name.Length <= 80);
        Assert.Empty(PantryListParser.Parse("  \n \n", IngredientCatalog.Default));
    }
}
