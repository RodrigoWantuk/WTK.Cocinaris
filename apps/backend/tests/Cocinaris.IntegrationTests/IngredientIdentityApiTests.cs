using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Embeddings;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Ingredients;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0055: ingredient identity against PostgreSQL with pgvector and pg_trgm.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task AnEnglishRecipeProposesAPantryLotNamedInPortugueseAndQueuesUnknownNames()
    {
        using var client = Client("ingredient-cook");
        var csrf = await CsrfAsync(client);
        var chicken = await CreateLotAsync(client, csrf, "Peito de frango", 500m);
        var created = await PostAsync(client, csrf, "/api/v1/recipes", new
        {
            content = new
            {
                name = "Grilled chicken",
                mealTypes = new[] { "dinner" },
                servings = 2,
                ingredients = new object[]
                {
                    new { displayName = "Chicken breast", quantity = 300m, unit = "g", optional = false },
                    new { displayName = "Grandma's Magic Dust", quantity = 5m, unit = "g", optional = false }
                },
                stages = new object[] { new { name = "Grill", instructions = "Grill the chicken.", activeMinutes = 15, passiveMinutes = 0 } }
            }
        }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var recipeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipeId").GetGuid();

        var execution = await PostAsync(client, csrf, $"/api/v1/recipes/{recipeId}/executions", new { });

        Assert.Equal(HttpStatusCode.Created, execution.StatusCode);
        var lines = (await execution.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("proposal").GetProperty("lines");
        Assert.Equal(("ready", chicken), (lines[0].GetProperty("status").GetString(), lines[0].GetProperty("lotId").GetGuid()));
        Assert.Equal("not_tracked", lines[1].GetProperty("status").GetString());
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Curated names never enter the queue; the unknown one does, with its exact spelling.
        var queued = Assert.Single(await database.IngredientResolutions.AsNoTracking().Where(item => item.NormalizedText == "grandma's magic dust").ToListAsync());
        Assert.Equal(("Pending", "Grandma's Magic Dust"), (queued.State, queued.ExactText));
        Assert.False(await database.IngredientResolutions.AnyAsync(item => item.NormalizedText == "peito de frango" || item.NormalizedText == "chicken breast"));
    }

    [Fact]
    public async Task TheJobResolvesByTrigramAndEmbeddingCachesByExactTextAndChargesTheLedger()
    {
        var embeddings = new ScriptedEmbeddingProvider(IngredientCatalog.Default)
        {
            Queries = { ["Peito de galinha"] = "chicken-breast" }
        };
        await using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IEmbeddingProvider>(embeddings)));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Test-Subject", "ingredient-resolver");
        await CsrfAsync(client);
        Guid owner;
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            owner = await database.Users.Where(user => user.Subject == "ingredient-resolver").Select(user => user.Id).SingleAsync();
            var matching = scope.ServiceProvider.GetRequiredService<IIngredientMatching>();
            var keys = await matching.KeysAsync(owner, ["Peito de frango", "Azeite extra virgem", "Peito de galinha", "Xyzzy powder"], CancellationToken.None);
            Assert.Equal("ingredient:chicken-breast", keys.Key("Peito de frango"));
            Assert.Equal("xyzzy powder", keys.Key("Xyzzy powder"));
        }

        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IngredientResolutionService>();
            Assert.True(await service.ProcessNextBatchAsync(CancellationToken.None));
            Assert.False(await service.ProcessNextBatchAsync(CancellationToken.None));
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var aliases = await database.IngredientAliases.AsNoTracking().Where(item => item.Method != "Catalog").ToDictionaryAsync(item => item.NormalizedText);
            var catalog = IngredientCatalog.Default;
            Assert.Equal(("Trigram", "olive-oil"), (aliases["azeite extra virgem"].Method, catalog.ById[aliases["azeite extra virgem"].IngredientId].Slug));
            Assert.Equal(("Embedding", "chicken-breast"), (aliases["peito de galinha"].Method, catalog.ById[aliases["peito de galinha"].IngredientId].Slug));
            Assert.True(aliases["peito de galinha"].Score >= 0.9);
            var unresolved = Assert.Single(await database.IngredientResolutions.AsNoTracking().ToListAsync(), item => item.NormalizedText == "xyzzy powder");
            // Decided rows forget their owner; the exact text stays so a later catalog can re-check it.
            Assert.Equal(("Unresolved", "below_threshold", (Guid?)null, "Xyzzy powder"), (unresolved.State, unresolved.Reason, unresolved.RequestedByUserId, unresolved.ExactText));
            Assert.Equal(2, await database.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM ingredients.text_embeddings""").SingleAsync());
            Assert.Equal(catalog.Items.Count, await database.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM ingredients.catalog_vectors""").SingleAsync());
            var ledger = await database.AiUsageLedgerEntries.AsNoTracking().Where(item => item.Operation == AiOperationRegistry.IngredientEmbed).ToListAsync();
            // Catalog documents in batches of 32, then one call for the two queries, all charged to the owner.
            Assert.Equal((catalog.Items.Count + 31) / 32 + 1, ledger.Count);
            Assert.All(ledger, entry => Assert.Equal(("Settled", owner), (entry.Status, entry.OwnerUserId)));
        }

        var callsBefore = embeddings.Calls;
        using (var scope = host.Services.CreateScope())
        {
            // A new threshold is a new evaluation: the unresolved name is re-checked, and its vector
            // comes from the exact-text cache, so the provider is not called again.
            var resolution = scope.ServiceProvider.GetRequiredService<IngredientResolutionOptions>();
            var tuned = new IngredientResolutionService(
                scope.ServiceProvider.GetRequiredService<IIngredientStore>(),
                IngredientCatalog.Default,
                embeddings,
                scope.ServiceProvider.GetRequiredService<EmbeddingOptions>(),
                new IngredientResolutionOptions { EmbeddingThreshold = resolution.EmbeddingThreshold - 0.01 },
                scope.ServiceProvider.GetRequiredService<AiUsageGovernor>(),
                TimeProvider.System);
            Assert.True(await tuned.ProcessNextBatchAsync(CancellationToken.None));
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var again = await database.IngredientResolutions.AsNoTracking().SingleAsync(item => item.NormalizedText == "xyzzy powder");
            Assert.Equal(("Unresolved", tuned.Evaluation), (again.State, again.Evaluation));
        }

        Assert.Equal(callsBefore, embeddings.Calls);
    }

    /// <summary>
    /// Deterministic vectors for assertions: each catalog ingredient is its own axis; a scripted
    /// query lands close to its ingredient's axis, and any other text on an unused axis.
    /// </summary>
    private sealed class ScriptedEmbeddingProvider(IngredientCatalog catalog) : IEmbeddingProvider
    {
        private const int Size = 1024;
        private readonly Dictionary<string, int> _documents = catalog.Items.ToDictionary(item => item.DocumentText, item => item.Id);

        public Dictionary<string, string> Queries { get; } = new(StringComparer.Ordinal);

        public int Calls { get; private set; }

        public string Name => "scripted";

        public string Space => "scripted|1024";

        public int Dimensions => Size;

        public Task<EmbeddingResult> EmbedAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var vectors = request.Inputs.Select(input =>
            {
                var vector = new float[Size];
                var query = input.Contains("\nQuery: ", StringComparison.Ordinal) ? input[(input.IndexOf("\nQuery: ", StringComparison.Ordinal) + 8)..] : null;
                if (query is null && _documents.TryGetValue(input, out var id))
                {
                    vector[id] = 1;
                }
                else if (query is not null && Queries.TryGetValue(query, out var slug))
                {
                    var target = catalog.Items.Single(item => item.Slug == slug).Id;
                    vector[target] = 0.96f;
                    vector[Size - 2] = 0.28f;
                }
                else
                {
                    vector[Size - 1] = 1;
                }

                return vector;
            }).ToList();
            return Task.FromResult(new EmbeddingResult(vectors, request.Inputs.Count * 5, 0m, "scripted", 1, EmbeddingFailureKind.None));
        }
    }
}
