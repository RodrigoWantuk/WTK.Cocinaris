using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ingredients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Cocinaris.IntegrationTests;

/// <summary>
/// PLAN-0055 Phase 4: live evaluation of ingredient resolution with the real OpenRouter model,
/// against a throwaway database. It runs only when <c>COCINARIS_EVAL_OPENROUTER_API_KEY</c> is set
/// (it returns immediately otherwise), so normal test runs never call a provider. Cost: one
/// catalog embedding (about 5 calls) and one call for the fixture names, well under US$ 0.001.
/// </summary>
/// <remarks>
/// It fails only on a <b>wrong link</b> (a name linked to an ingredient other than the expected
/// one), because that is what could merge two foods. A name left unresolved is a missed match,
/// reported for threshold tuning but not a failure.
/// </remarks>
public sealed class IngredientLiveEvaluationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string KeyVariable = "COCINARIS_EVAL_OPENROUTER_API_KEY";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(TestImages.PostgreSql)
        .WithDatabase("cocinaris_ingredient_eval")
        .WithUsername("cocinaris_test")
        .WithPassword("development-only-test-password")
        .Build();

    private static string? ApiKey => Environment.GetEnvironmentVariable(KeyVariable);

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            await _postgres.StartAsync();
        }
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task IngredientLiveEvaluationLinksNoNameToAWrongIngredient()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            output.WriteLine($"Skipped: set {KeyVariable} to run the live evaluation.");
            return;
        }

        await using var factory = new CocinarisFactory(_postgres.GetConnectionString());
        await factory.EnsureDatabaseAsync();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Ai:Embeddings:Enabled", "true");
            builder.UseSetting("Ai:Providers:OpenRouter:ApiKey", ApiKey);
        });
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", "ingredient-evaluator");
        await client.GetFromJsonAsync<JsonElement>("/api/v1/session");

        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "ingredient-identity-eval.json")));
        var cases = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .Select(item => (Text: item.GetProperty("text").GetString()!, Expected: item.GetProperty("expected").GetString()))
            .ToList();

        using var scope = host.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await database.Users.Where(user => user.Subject == "ingredient-evaluator").Select(user => user.Id).SingleAsync();
        var matching = scope.ServiceProvider.GetRequiredService<IIngredientMatching>();
        await matching.KeysAsync(owner, cases.Select(item => item.Text), CancellationToken.None);
        var resolution = scope.ServiceProvider.GetRequiredService<IngredientResolutionService>();
        for (var step = 0; step < 10 && await resolution.ProcessNextBatchAsync(CancellationToken.None); step++)
        {
        }

        var keys = await matching.KeysAsync(owner, cases.Select(item => item.Text), CancellationToken.None);
        var aliases = await database.IngredientAliases.AsNoTracking().ToDictionaryAsync(item => item.NormalizedText);
        var unresolved = await database.IngredientResolutions.AsNoTracking().ToDictionaryAsync(item => item.NormalizedText);
        var catalog = IngredientCatalog.Default;
        int correct = 0, missed = 0, wrong = 0, correctlyUnresolved = 0;
        foreach (var (text, expected) in cases)
        {
            var normalized = IngredientText.Normalize(text);
            var key = keys.Key(text);
            var linked = key.StartsWith("ingredient:", StringComparison.Ordinal) ? key["ingredient:".Length..] : null;
            var how = aliases.TryGetValue(normalized, out var alias) ? $"{alias.Method} {alias.Score:0.000}"
                : unresolved.TryGetValue(normalized, out var row) ? $"unresolved ({row.Reason}; nearest {(row.BestIngredientId is { } id ? catalog.ById[id].Slug : "-")} {row.BestScore:0.000})"
                : "-";
            var verdict = (linked, expected) switch
            {
                (null, null) => "ok (left unresolved)",
                (null, _) => "missed",
                (_, null) => "WRONG (should stay unresolved)",
                _ when linked == expected => "ok",
                _ => "WRONG"
            };
            _ = verdict switch
            {
                "ok" => correct++,
                "ok (left unresolved)" => correctlyUnresolved++,
                "missed" => missed++,
                _ => wrong++
            };
            output.WriteLine($"{verdict,-32} {text,-34} -> {linked ?? "-",-18} expected {expected ?? "-",-18} {how}");
        }

        output.WriteLine($"correct {correct}, correctly unresolved {correctlyUnresolved}, missed {missed}, wrong {wrong} of {cases.Count}");
        Assert.Equal(0, wrong);
    }
}
