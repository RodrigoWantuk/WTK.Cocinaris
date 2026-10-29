using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Embeddings;
using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Ingredients;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api.Ingredients;

/// <summary>Registers the ingredient identity module (PLAN-0055) for the API composition root.</summary>
public static class IngredientServiceCollectionExtensions
{
    /// <summary>
    /// Adds the curated catalog, matching, the PostgreSQL store, the resolution job, and the
    /// embedding provider: a fake in Testing, OpenRouter when <c>Ai:Embeddings:Enabled</c> is true
    /// and an OpenRouter key is set, and none otherwise. Without a provider the job still links
    /// curated and near-identical names; other names keep matching by text.
    /// </summary>
    public static IServiceCollection AddCocinarisIngredients(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<IngredientResolutionOptions>()
            .Bind(configuration.GetSection("Ingredients:Resolution"))
            .Validate(options => options.IsValid(), "Ingredient resolution options must be positive and coherent.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<IngredientResolutionOptions>>().Value);
        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection("Ai:Embeddings"))
            .Validate(options => options.IsValid(), "Embedding options must be coherent.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<EmbeddingOptions>>().Value);
        services.AddSingleton(IngredientCatalog.Default);
        services.AddScoped<IIngredientStore, PostgreSqlIngredientStore>();
        services.AddScoped<IIngredientMatching, IngredientIdentityService>();

        var apiKey = configuration.GetValue<string>("Ai:Providers:OpenRouter:ApiKey");
        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IEmbeddingProvider>(provider => new FakeEmbeddingProvider(provider.GetRequiredService<EmbeddingOptions>().Dimensions));
        }
        else if (configuration.GetValue("Ai:Embeddings:Enabled", false) && !string.IsNullOrWhiteSpace(apiKey))
        {
            // OpenRouterOptions is bound once by the recipe thumbnail registration.
            services.AddHttpClient("cocinaris-openrouter-embeddings", (provider, client) =>
                client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<EmbeddingOptions>().TimeoutSeconds));
            services.AddScoped<IEmbeddingProvider>(provider => new OpenRouterEmbeddingProvider(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("cocinaris-openrouter-embeddings"),
                provider.GetRequiredService<IOptions<OpenRouterOptions>>().Value,
                provider.GetRequiredService<EmbeddingOptions>()));
        }

        services.AddScoped(provider => new IngredientResolutionService(
            provider.GetRequiredService<IIngredientStore>(),
            provider.GetRequiredService<IngredientCatalog>(),
            provider.GetService<IEmbeddingProvider>(),
            provider.GetRequiredService<EmbeddingOptions>(),
            provider.GetRequiredService<IngredientResolutionOptions>(),
            provider.GetRequiredService<AiUsageGovernor>(),
            provider.GetRequiredService<TimeProvider>()));
        if (!environment.IsEnvironment("Testing"))
        {
            services.AddHostedService<IngredientResolutionWorker>();
        }

        return services;
    }
}
