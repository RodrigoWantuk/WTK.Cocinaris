using System.Net;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Ai.Embeddings;
using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0055: ingredient catalog, matching keys, the resolution decision rule, and the embedding adapter.</summary>
public sealed class IngredientIdentityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheCatalogIsUnambiguousAndLinksNamesAcrossLanguages()
    {
        var catalog = IngredientCatalog.Default;

        Assert.True(catalog.Items.Count >= 140);
        Assert.Equal("chicken-breast", catalog.Aliases[IngredientText.Normalize("Peito de Frango")].Slug);
        Assert.Equal("chicken-breast", catalog.Aliases[IngredientText.Normalize("pechuga de pollo")].Slug);
        Assert.Equal("chicken-breast", catalog.Aliases["chicken breasts"].Slug);
        Assert.Equal("black-pepper", catalog.Aliases[IngredientText.Normalize("Pimenta-do-reino")].Slug);
        // Deliberately ambiguous words stay out of the catalog: "pimenta" alone is not black pepper.
        Assert.False(catalog.Aliases.ContainsKey("pimenta"));
        Assert.StartsWith("1-", catalog.Version);
    }

    [Fact]
    public void ACatalogWithOneNameForTwoIngredientsIsRejected()
    {
        const string json = """{"version":1,"items":[{"id":1,"slug":"a","category":"x","names":{"pt":["Batata"]}},{"id":2,"slug":"b","category":"x","names":{"es":["batata"]}}]}""";

        var error = Assert.Throws<InvalidOperationException>(() => IngredientCatalog.Parse(json));
        Assert.Contains("batata", error.Message);
    }

    [Fact]
    public void MatchKeysUseTheIngredientWhenResolvedAndTheNormalizedNameOtherwise()
    {
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["peito de frango"] = "chicken-breast", ["chicken breast"] = "chicken-breast" });

        Assert.Equal(keys.Key(" Peito  de FRANGO "), keys.Key("Chicken breast"));
        Assert.Equal("ingredient:chicken-breast", keys.Key("chicken breast"));
        Assert.Equal("feijao tropeiro", keys.Key("Feijão  Tropeiro"));
        Assert.Equal(IngredientMatchKeys.NamesOnly.Key("Feijão"), RecipeIngredientReader.NormalizeName("Feijão"));
    }

    [Theory]
    [InlineData(0.91, 1, 0.70, true)]   // clear winner
    [InlineData(0.79, 1, 0.10, false)]  // below threshold
    [InlineData(0.85, 2, 0.84, false)]  // two ingredients almost tied: no decision
    [InlineData(0.85, 1, 0.84, true)]   // the runner-up is the same ingredient
    public void ADecisionNeedsTheThresholdAndAClearMargin(double best, int secondId, double second, bool decided)
    {
        var candidates = new[] { new IngredientCandidate(1, best), new IngredientCandidate(secondId, second) };

        Assert.Equal(decided, IngredientResolutionService.Decide(candidates, 0.80, 0.03) is not null);
    }

    [Fact]
    public void TheEmbeddingCacheKeyChangesWithTheExactTextRoleAndSpace()
    {
        var key = IngredientResolutionService.CacheKey("qwen|nebius|1024", "query", "q1", "Frango");

        Assert.Equal(64, key.Length);
        Assert.Equal(key, IngredientResolutionService.CacheKey("qwen|nebius|1024", "query", "q1", "Frango"));
        // Only the identical text reuses a vector (owner decision 2026-10-30).
        Assert.NotEqual(key, IngredientResolutionService.CacheKey("qwen|nebius|1024", "query", "q1", "frango"));
        Assert.NotEqual(key, IngredientResolutionService.CacheKey("qwen|nebius|1024", "query", "q1", "Frango "));
        Assert.NotEqual(key, IngredientResolutionService.CacheKey("qwen|nebius|1024", "document", "q1", "Frango"));
        Assert.NotEqual(key, IngredientResolutionService.CacheKey("qwen|nebius|512", "query", "q1", "Frango"));
        Assert.NotEqual(key, IngredientResolutionService.CacheKey("qwen|nebius|1024", "query", "q2", "Frango"));
    }

    [Fact]
    public void VectorsAreTruncatedAndScaledToUnitLength()
    {
        var vector = EmbeddingVectors.TruncateAndNormalize([3f, 4f, 100f], 2)!;

        Assert.Equal([0.6f, 0.8f], vector);
        Assert.Null(EmbeddingVectors.TruncateAndNormalize([1f], 2));
        Assert.Null(EmbeddingVectors.TruncateAndNormalize([0f, 0f], 2));
        Assert.Null(EmbeddingVectors.TruncateAndNormalize([float.NaN, 1f], 2));
    }

    [Fact]
    public async Task TheOpenRouterAdapterPinsProvidersDeniesDataCollectionAndReordersVectors()
    {
        string? sent = null;
        var handler = new StubHandler(async request =>
        {
            sent = await request.Content!.ReadAsStringAsync();
            Assert.Equal("https://openrouter.ai/api/v1/embeddings", request.RequestUri!.ToString());
            Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());
            // Returned out of order and longer than requested: the adapter reorders and truncates.
            return Json("""{"model":"qwen/qwen3-embedding-8b","data":[{"index":1,"embedding":[0,3,4,9]},{"index":0,"embedding":[1,0,0,9]}],"usage":{"prompt_tokens":7,"cost":0.00000007}}""");
        });
        var provider = new OpenRouterEmbeddingProvider(new HttpClient(handler), new OpenRouterOptions { ApiKey = "test-key" }, new EmbeddingOptions { Dimensions = 3 });

        var result = await provider.EmbedAsync(new EmbeddingRequest(["a", "b"]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([1f, 0f, 0f], result.Vectors![0]);
        Assert.Equal([0f, 0.6f, 0.8f], result.Vectors[1]);
        Assert.Equal((7, 0.00000007m), (result.PromptTokens!.Value, result.CostUsd!.Value));
        using var body = JsonDocument.Parse(sent!);
        Assert.Equal("qwen/qwen3-embedding-8b", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(["a", "b"], body.RootElement.GetProperty("input").EnumerateArray().Select(item => item.GetString()));
        var routing = body.RootElement.GetProperty("provider");
        Assert.Equal(["nebius", "deepinfra"], routing.GetProperty("order").EnumerateArray().Select(item => item.GetString()));
        Assert.False(routing.GetProperty("allow_fallbacks").GetBoolean());
        Assert.Equal("deny", routing.GetProperty("data_collection").GetString());
        Assert.Equal("qwen/qwen3-embedding-8b|nebius,deepinfra|3", provider.Space);
    }

    [Theory]
    [InlineData("""{"data":[{"index":0,"embedding":[1,0,0]}]}""", "wrong_count")]
    [InlineData("""{"data":[{"index":0,"embedding":[1,0]},{"index":1,"embedding":[1,0,0]}]}""", "bad_vector")]
    [InlineData("""{"data":[{"index":0,"embedding":[1,0,0]},{"index":0,"embedding":[1,0,0]}]}""", "wrong_count")]
    [InlineData("not json", "bad_json")]
    public async Task AnIncompleteAnswerIsRejectedWhole(string response, string failure)
    {
        var provider = new OpenRouterEmbeddingProvider(new HttpClient(new StubHandler(_ => Task.FromResult(Json(response)))), new OpenRouterOptions { ApiKey = "k" }, new EmbeddingOptions { Dimensions = 3 });

        var result = await provider.EmbedAsync(new EmbeddingRequest(["a", "b"]), CancellationToken.None);

        Assert.Equal((EmbeddingFailureKind.Invalid, failure), (result.Failure, result.FailureCode));
    }

    [Fact]
    public async Task ARateLimitIsTransient()
    {
        var provider = new OpenRouterEmbeddingProvider(new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)))), new OpenRouterOptions { ApiKey = "k" }, new EmbeddingOptions());

        var result = await provider.EmbedAsync(new EmbeddingRequest(["a"]), CancellationToken.None);

        Assert.Equal((EmbeddingFailureKind.Transient, "http_429"), (result.Failure, result.FailureCode));
    }

    [Fact]
    public void TheCookingProposalTakesAPantryLotNamedInAnotherLanguage()
    {
        var chicken = Lot("Peito de frango", 500m, "Gram");
        var recipe = """{"ingredients":[{"ingredientId":"i1","sourceType":"additional","displayName":"Chicken breast","requiredQuantity":300,"unit":"g","optional":false}]}""";
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["peito de frango"] = "chicken-breast", ["chicken breast"] = "chicken-breast" });

        var withoutCatalog = RecipeExecutionApplicationService.BuildProposal(recipe, 2, 2, [chicken]);
        var withCatalog = RecipeExecutionApplicationService.BuildProposal(recipe, 2, 2, [chicken], matchKeys: keys);

        Assert.Equal("not_tracked", withoutCatalog.Lines[0].Status);
        Assert.Equal(("ready", chicken.LotId), (withCatalog.Lines[0].Status, withCatalog.Lines[0].LotId));
    }

    [Fact]
    public void TheShoppingListCountsPantryStockNamedInAnotherLanguage()
    {
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["cebola"] = "onion", ["onions"] = "onion" });
        var meals = new[] { new ShoppingMeal(Guid.NewGuid(), "Soup", null, 2, 2, """{"ingredients":[{"ingredientId":"i1","sourceType":"additional","displayName":"Onions","requiredQuantity":3,"unit":"unit","optional":false}]}""") };

        var item = Assert.Single(ShoppingProjector.Build(meals, [Lot("Cebola", 2m, "Unit")], new HashSet<string>(), keys), line => line.Category != "available");

        Assert.Equal(("ingredient:onion|Unit", 3m, 2m, 1m), (item.ItemKey, item.RequiredQuantity, item.AvailableQuantity, item.ToBuyQuantity));
    }

    [Fact]
    public void ShoppingLinesAreGroupedByStoreSectionOfTheirCatalogIngredient()
    {
        var keys = new IngredientMatchKeys(new Dictionary<string, string> { ["cebola"] = "onion", ["peito de frango"] = "chicken-breast", ["leite"] = "milk", ["arroz"] = "rice" });
        var meals = new[]
        {
            new ShoppingMeal(Guid.NewGuid(), "Jantar", null, 2, 2, """{"ingredients":[ {"ingredientId":"i1","sourceType":"additional","displayName":"Cebola","requiredQuantity":1,"unit":"unit","optional":false}, {"ingredientId":"i2","sourceType":"additional","displayName":"Peito de frango","requiredQuantity":300,"unit":"g","optional":false}, {"ingredientId":"i3","sourceType":"additional","displayName":"Leite","requiredQuantity":200,"unit":"ml","optional":false}, {"ingredientId":"i4","sourceType":"additional","displayName":"Arroz","requiredQuantity":200,"unit":"g","optional":false}, {"ingredientId":"i5","sourceType":"additional","displayName":"Pó mágico","requiredQuantity":5,"unit":"g","optional":false}]}""")
        };

        var sections = ShoppingProjector.Build(meals, [], new HashSet<string>(), keys).ToDictionary(item => item.DisplayName, item => item.StoreSection);

        Assert.Equal(StoreSections.Produce, sections["Cebola"]);
        Assert.Equal(StoreSections.ButcherFish, sections["Peito de frango"]);
        Assert.Equal(StoreSections.DairyEggs, sections["Leite"]);
        Assert.Equal(StoreSections.DryGoods, sections["Arroz"]);
        Assert.Equal(StoreSections.Other, sections["Pó mágico"]);
    }

    [Fact]
    public async Task OnlyNamesOutsideTheCatalogAndTheLearnedAliasesAreQueuedWithTheirExactText()
    {
        var store = new MemoryStore { Learned = { ["frango desfiado"] = "chicken" } };
        var service = new IngredientIdentityService(store, IngredientCatalog.Default, TimeProvider.System);
        var owner = Guid.NewGuid();

        var keys = await service.KeysAsync(owner, ["Peito de frango", "Frango desfiado", "  Pó mágico da vovó ", "pó   MÁGICO da vovó", "", "x"], CancellationToken.None);

        Assert.Equal("ingredient:chicken-breast", keys.Key("peito de frango"));
        Assert.Equal("ingredient:chicken", keys.Key("frango desfiado"));
        Assert.Equal("po magico da vovo", keys.Key("Pó mágico da vovó"));
        var queued = Assert.Single(store.Queued);
        // The first spelling seen is kept exactly (trimmed), for the exact-text embedding cache.
        Assert.Equal(("po magico da vovo", "Pó mágico da vovó", owner), (queued.NormalizedText, queued.ExactText, queued.Owner));
    }

    private static InventoryLotReadModel Lot(string name, decimal quantity, string unit) =>
        new(Guid.NewGuid(), Guid.NewGuid(), name, quantity, unit, null, "Pantry", null, null, null, null, Guid.NewGuid(), Now, Now, null);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class MemoryStore : IIngredientStore
    {
        public Dictionary<string, string> Learned { get; } = new(StringComparer.Ordinal);

        public List<(string NormalizedText, string ExactText, Guid Owner)> Queued { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> FindAliasSlugsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Learned.Where(entry => normalizedNames.Contains(entry.Key)).ToDictionary());

        public Task EnqueueAsync(IReadOnlyCollection<(string NormalizedText, string ExactText)> names, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queued.AddRange(names.Select(name => (name.NormalizedText, name.ExactText, ownerUserId)));
            return Task.CompletedTask;
        }

        public Task SyncCatalogAsync(IngredientCatalog catalog, string evaluation, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PendingIngredientName>> ClaimAsync(int max, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ResolveAsync(string normalizedText, int ingredientId, IngredientResolutionMethod method, double score, string evaluation, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkUnresolvedAsync(string normalizedText, IngredientCandidate? best, string evaluation, string reason, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeferAsync(string normalizedText, int attempts, DateTimeOffset nextAttemptAt, string reason, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngredientCandidate>> TrigramCandidatesAsync(string normalizedText, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, float[]>> FindEmbeddingsAsync(IReadOnlyCollection<string> cacheKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveEmbeddingsAsync(IReadOnlyDictionary<string, float[]> vectors, string space, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<int>> CatalogVectorsToRefreshAsync(string space, IReadOnlyDictionary<int, string> documentKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveCatalogVectorsAsync(string space, IReadOnlyDictionary<int, (string DocumentKey, float[] Vector)> vectors, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngredientCandidate>> NearestAsync(string space, float[] vector, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
