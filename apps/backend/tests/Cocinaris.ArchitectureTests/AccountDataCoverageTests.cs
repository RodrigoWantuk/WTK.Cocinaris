using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Infrastructure.Persistence.AccountData;
using Microsoft.EntityFrameworkCore;

namespace Cocinaris.ArchitectureTests;

/// <summary>
/// PLAN-0049: every table that holds a person's id is exported or deliberately left out, so a new
/// table cannot be forgotten by the privacy center. Reads the EF model; no database is needed.
/// </summary>
public sealed class AccountDataCoverageTests
{
    private static readonly string[] PersonColumns = ["OwnerUserId", "ActorUserId", "RequestedByUserId"];

    private static List<(string Table, HashSet<string> Columns)> ModelTables()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options);
        return context.Model.GetEntityTypes()
            .Where(type => type.GetTableName() is not null)
            .Select(type => ($"{type.GetSchema()}.{type.GetTableName()}", type.GetProperties().Select(property => property.GetColumnName()).ToHashSet(StringComparer.Ordinal)))
            .ToList();
    }

    [Fact]
    public void EveryTableWithAPersonsIdIsExportedOrExempted()
    {
        var covered = AccountDataCatalog.Tables.Select(table => table.QualifiedName)
            .Concat(AccountDataCatalog.Exemptions.Select(exemption => $"{exemption.Schema}.{exemption.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        var missing = ModelTables()
            .Where(table => table.Columns.Overlaps(PersonColumns) && !covered.Contains(table.Table))
            .Select(table => table.Table)
            .ToList();

        Assert.True(missing.Count == 0, "Add these tables to AccountDataCatalog.Tables or, with a reason, to Exemptions: " + string.Join(", ", missing));
        Assert.Contains("identity.users", AccountDataCatalog.Tables.Select(table => table.QualifiedName));
    }

    [Fact]
    public void CatalogNamesRealTablesOwnerColumnsAndOmittedColumns()
    {
        var model = ModelTables().ToDictionary(table => table.Table, table => table.Columns, StringComparer.Ordinal);
        foreach (var table in AccountDataCatalog.Tables)
        {
            Assert.True(model.TryGetValue(table.QualifiedName, out var columns), $"{table.QualifiedName} is not in the model.");
            Assert.Contains(table.OwnerColumn, columns!);
            Assert.All(table.OmittedColumns, column => Assert.Contains(column, columns!));
        }

        foreach (var exemption in AccountDataCatalog.Exemptions)
        {
            Assert.True(model.ContainsKey($"{exemption.Schema}.{exemption.Name}"), $"{exemption.Schema}.{exemption.Name} is not in the model.");
            Assert.False(string.IsNullOrWhiteSpace(exemption.Reason));
        }

        Assert.Equal(AccountDataCatalog.Tables.Count, AccountDataCatalog.Tables.Select(table => table.QualifiedName).Distinct().Count());
        // Push keys are secrets of the browser and never leave in an export.
        Assert.Equal(["Endpoint", "P256dh", "Auth"], AccountDataCatalog.Tables.Single(table => table.Name == "push_subscriptions").OmittedColumns);
    }

    [Fact]
    public void TheReadablePageHasTheSameCopyInEveryLanguage()
    {
        var keys = Cocinaris.Api.Privacy.ExportText.Keys;
        Assert.Equal(keys["pt-BR"].Order(), keys["en"].Order());
        Assert.Equal(keys["pt-BR"].Order(), keys["es"].Order());
    }
}
