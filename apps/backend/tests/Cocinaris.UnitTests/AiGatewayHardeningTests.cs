using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Providers;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Recipes.Ai;

namespace Cocinaris.UnitTests;

/// <summary>PLAN-0040: v4-flash request shape and fallback, cost accounting, and soft language/detail checks.</summary>
public sealed class AiGatewayHardeningTests
{
    private const string English = "Heat the oil in the pan over medium heat and add the onion. Stir with a wooden spoon until it is soft, then add the rice and cook it for two minutes while you stir it, so that it is coated with the oil.";
    private const string Portuguese = "Aqueça o óleo na panela em fogo médio e junte a cebola. Mexa com uma colher de pau até ela ficar macia, depois adicione o arroz e cozinhe por dois minutos, mexendo sempre, até os grãos ficarem envolvidos no óleo.";

    [Fact]
    public async Task TheProviderSendsV4FlashWithThinkingOffAndFallsBackOnceOnAProviderError()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, Success("deepseek-chat"));
        var provider = new DeepSeekAiProvider(new HttpClient(handler), new DeepSeekOptions { Enabled = true, ApiKey = "test-key" });

        var result = await provider.InvokeAsync(new AiProviderInvocationRequest("op", "{}", PreferNonThinking: true, TimeoutSeconds: 5, CorrelationId: "c", MaxOutputTokens: 6144), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(("deepseek-chat", 2, 100, 50), (result.ModelUsed, result.ProviderCalls, result.PromptTokens, result.CompletionTokens));
        Assert.NotNull(result.LatencyMs);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("deepseek-v4-flash", first.RootElement.GetProperty("model").GetString());
        Assert.Equal("disabled", first.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(6144, first.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(first.RootElement.TryGetProperty("reasoning_effort", out _));
        // The fallback alias does not take the thinking parameter.
        using var second = JsonDocument.Parse(handler.Bodies[1]);
        Assert.Equal("deepseek-chat", second.RootElement.GetProperty("model").GetString());
        Assert.False(second.RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task ThinkingOperationsSendReasoningEffortAndTimeoutsAreNotRetried()
    {
        var handler = new RecordingHandler(HttpStatusCode.GatewayTimeout);
        var provider = new DeepSeekAiProvider(new HttpClient(handler), new DeepSeekOptions { Enabled = true, ApiKey = "test-key" });

        var result = await provider.InvokeAsync(new AiProviderInvocationRequest("op", "{}", PreferNonThinking: false, TimeoutSeconds: 5, CorrelationId: "c"), CancellationToken.None);

        Assert.Equal((AiProviderFailureKind.Timeout, 1), (result.FailureKind, handler.Bodies.Count));
        using var body = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(("enabled", "high"), (body.RootElement.GetProperty("thinking").GetProperty("type").GetString(), body.RootElement.GetProperty("reasoning_effort").GetString()));
    }

    [Fact]
    public void CostIsTokensTimesListPriceAndUnknownModelsPayTheHighestPrice()
    {
        var pricing = new AiPricingOptions();
        Assert.Equal(420, pricing.CostMicroUsd("deepseek-v4-flash", 1000, 1000));
        Assert.Equal(700, pricing.CostMicroUsd("unknown-model", 1000, 1000));
        Assert.Equal(1, pricing.CostMicroUsd("deepseek-v4-flash", 1, 0));
        Assert.Equal(5_000_000, new AiUsageOptions().MonthlyCostCeilingMicroUsd);
    }

    [Fact]
    public void LanguageAndDetailChecksFlagOnlyClearMismatches()
    {
        Assert.Equal("language", RecipeOutputQuality.LanguageIssue([English], "pt-BR")?.Flag);
        Assert.Null(RecipeOutputQuality.LanguageIssue([Portuguese], "pt-BR"));
        Assert.Null(RecipeOutputQuality.LanguageIssue(["Heat the oil."], "pt-BR"));
        Assert.Null(RecipeOutputQuality.LanguageIssue([English], "en"));
        Assert.Equal("language", RecipeOutputQuality.LanguageIssue([Portuguese], "en")?.Flag);
        Assert.Equal("Detailed", RecipeOutputQuality.DetailLevel(new RecipeUserPresets(2, [], [], [], CookingSkill: "Beginner")));
        Assert.Equal("Minimal", RecipeOutputQuality.DetailLevel(new RecipeUserPresets(2, [], [], [], InstructionDetail: "Minimal", CookingSkill: "Beginner")));
    }

    /// <summary>A valid answer in the wrong language is repaired; a clean repair wins.</summary>
    public static string WithSummaries(string defaultContent, string summary)
    {
        var root = JsonNode.Parse(defaultContent)!.AsObject();
        foreach (var candidate in root["candidates"]!.AsArray())
        {
            candidate!["summary"] = summary;
        }

        return root.ToJsonString();
    }

    private static string Success(string model) =>
        "{\"model\":\"" + model + "\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"ok\\\":true}\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50}}";

    private sealed class RecordingHandler(HttpStatusCode first, string? secondBody = null) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Bodies.Count == 1 && first != HttpStatusCode.OK
                ? new HttpResponseMessage(first)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(secondBody ?? "{}", Encoding.UTF8, "application/json") };
        }
    }
}
