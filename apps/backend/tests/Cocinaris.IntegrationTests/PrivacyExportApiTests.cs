using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence.AccountData;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0049 Slice 1: the export holds every table of the person who asks, and nothing of anyone else.</summary>
public sealed partial class RecipeExecutionApiTests
{
    private const string Export = "/api/v1/privacy/export";

    private static async Task<(JsonElement Json, string Html)> ExportAsync(HttpClient client, string language = "pt-BR")
    {
        using var response = await client.GetAsync($"{Export}?language={language}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.StartsWith("cocinaris-dados-", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        Assert.Equal(["cocinaris-dados.html", "cocinaris-dados.json"], zip.Entries.Select(entry => entry.FullName).Order());
        using var json = JsonDocument.Parse(zip.GetEntry("cocinaris-dados.json")!.Open());
        using var html = new StreamReader(zip.GetEntry("cocinaris-dados.html")!.Open());
        return (json.RootElement.Clone(), await html.ReadToEndAsync());
    }

    private static IEnumerable<JsonElement> Rows(JsonElement export, string module, string table) =>
        export.GetProperty("modules").GetProperty(module).GetProperty(table).EnumerateArray();

    [Fact]
    public async Task TheExportHoldsEveryTableOfThePersonAndNothingOfAnyoneElse()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using var client = PlanClient(host, "privacy-export");
        using var other = PlanClient(host, "privacy-export-other");
        var csrf = await CsrfAsync(client);
        var otherCsrf = await CsrfAsync(other);
        const string Hostile = "<script>alert(1)</script> Bolo";
        await CreateMealRecipeAsync(client, csrf, Hostile, "snack", "Farinha");
        await CreateLotAsync(client, csrf, "Farinha", 500m);
        await PostAsync(client, csrf, Plans, CountsPlan(snack: 2, days: 7), Guid.NewGuid());
        await CreateMealRecipeAsync(other, otherCsrf, "Torta Secreta do Outro", "snack", "Queijo Secreto");
        await CreateLotAsync(other, otherCsrf, "Queijo Secreto", 300m);

        var (export, html) = await ExportAsync(client);
        Assert.Equal(("cocinaris-export", 1), (export.GetProperty("format").GetString(), export.GetProperty("version").GetInt32()));
        var accountId = export.GetProperty("accountId").GetGuid();

        // Every table of the catalog is there, even when empty, so a reader can rely on the shape.
        foreach (var table in AccountDataCatalog.Tables)
        {
            Assert.Equal(JsonValueKind.Array, export.GetProperty("modules").GetProperty(table.Module).GetProperty(table.Name).ValueKind);
        }

        Assert.Equal(accountId, Rows(export, "identity", "users").Single().GetProperty("Id").GetGuid());
        Assert.Contains(Rows(export, "recipes", "recipe_revisions"), row => row.GetProperty("Name").GetString() == Hostile);
        // A column stored as JSON text is written as JSON.
        Assert.Equal(JsonValueKind.Object, Rows(export, "recipes", "recipe_revisions").First().GetProperty("NormalizedRecipeJson").ValueKind);
        Assert.Contains(Rows(export, "inventory", "products"), row => row.GetProperty("DisplayName").GetString() == "Farinha");
        Assert.NotEmpty(Rows(export, "planning", "shopping_plan_sessions"));

        // Only the person's own rows, in every table.
        foreach (var table in AccountDataCatalog.Tables.Where(table => table.OwnerColumn != "Id"))
        {
            Assert.All(Rows(export, table.Module, table.Name), row => Assert.Equal(accountId, row.GetProperty(table.OwnerColumn).GetGuid()));
        }

        var text = export.GetRawText();
        Assert.DoesNotContain("Torta Secreta do Outro", text);
        Assert.DoesNotContain("Queijo Secreto", text);
        Assert.DoesNotContain("Queijo Secreto", html);

        // The readable page shows the recipe and the pantry, escaped, and runs nothing.
        Assert.Contains("Seus dados no Cocinaris", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; Bolo", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Security-Policy", html);
        Assert.Contains("Farinha", html);

        var (_, english) = await ExportAsync(client, "en");
        Assert.Contains("Your data in Cocinaris", english);
    }

    [Fact]
    public async Task TheExportIsLimitedPerPerson()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        // The test host signs everyone in; the session requirement is the group's RequireAuthorization, as for every route.
        using var client = PlanClient(host, "privacy-limit");
        await CsrfAsync(client);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var ok = await client.GetAsync(Export);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var limited = await client.GetAsync(Export);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        // Another person is not limited by the first.
        using var other = PlanClient(host, "privacy-limit-other");
        await CsrfAsync(other);
        using var allowed = await other.GetAsync(Export);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
