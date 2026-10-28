using KitchenFlow.Infrastructure.Persistence;
using KitchenFlow.Modules.Ai.Abstractions;
using KitchenFlow.Modules.Ai.Providers;
using KitchenFlow.Modules.Ai.Usage;
using KitchenFlow.Modules.Recipes.Application;
using KitchenFlow.Api.Recipes;
using Microsoft.Extensions.Options;

namespace KitchenFlow.Api;

/// <summary>Registers AI Gateway and Recipes module services for the API composition root.</summary>
public static class RecipeAiServiceCollectionExtensions
{
    /// <summary>
    /// Adds cook-now recipe generation, AI Gateway operations, usage governance, and provider
    /// selection. Uses <see cref="FakeAiProvider"/> only in the Testing environment; otherwise
    /// registers DeepSeek when enabled with an API key, or <see cref="UnavailableAiProvider"/> when
    /// the live capability is disabled so generate surfaces a truthful degraded state.
    /// </summary>
    public static IServiceCollection AddKitchenFlowRecipeAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AiUsageOptions>()
            .Bind(configuration.GetSection("Ai:Usage"))
            .Validate(options => options.IsValid(), "AI usage ceilings must be positive and coherent.")
            .ValidateOnStart();

        services.AddOptions<DeepSeekOptions>()
            .Bind(configuration.GetSection("Ai:Providers:DeepSeek"))
            .Validate(options => options.IsValid(environment.IsDevelopment() || environment.IsEnvironment("Testing")), "DeepSeek configuration is absent or unsafe for this environment.")
            .ValidateOnStart();

        services.AddSingleton(AiOperationRegistry.CreateDefault());
        services.AddScoped<IAiUsageLedgerStore, PostgreSqlAiUsageLedgerStore>();
        services.AddScoped(provider =>
            new AiUsageGovernor(
                provider.GetRequiredService<IAiUsageLedgerStore>(),
                provider.GetRequiredService<IOptions<AiUsageOptions>>().Value,
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IRecipeGenerationStore, PostgreSqlRecipeGenerationStore>();
        services.AddScoped<IRecipeStore, PostgreSqlRecipeStore>();
        services.AddScoped<IRecipeCookNowUnitOfWork, PostgreSqlRecipeCookNowUnitOfWork>();
        services.AddScoped<IRecipeContextAssembler, RecipeContextAssembler>();
        services.AddScoped<RecipeCookNowApplicationService>();
        services.AddScoped<RecipeApiService>();

        var deepSeekSection = configuration.GetSection("Ai:Providers:DeepSeek");
        var enabled = deepSeekSection.GetValue("Enabled", false);
        var apiKey = deepSeekSection.GetValue<string>("ApiKey");
        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IAiProvider, FakeAiProvider>();
        }
        else if (enabled && !string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddHttpClient("kitchenflow-deepseek", (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddScoped<IAiProvider>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
                var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("kitchenflow-deepseek");
                return new DeepSeekAiProvider(httpClient, options);
            });
        }
        else
        {
            services.AddSingleton<IAiProvider, UnavailableAiProvider>();
        }

        return services;
    }
}
