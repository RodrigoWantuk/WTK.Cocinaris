using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Ai.Abstractions;
using Cocinaris.Modules.Ai.Images;
using Cocinaris.Modules.Ai.Providers;
using Cocinaris.Modules.Ai.Usage;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Thumbnails;
using Cocinaris.Api.Recipes;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api;

/// <summary>Registers AI Gateway and Recipes module services for the API composition root.</summary>
public static class RecipeAiServiceCollectionExtensions
{
    /// <summary>
    /// Adds cook-now recipe generation, AI Gateway operations, usage governance, and provider
    /// selection. Uses <see cref="FakeAiProvider"/> only in the Testing environment; otherwise
    /// registers DeepSeek when enabled with an API key, or <see cref="UnavailableAiProvider"/> when
    /// the live capability is disabled so generate surfaces a truthful degraded state.
    /// </summary>
    public static IServiceCollection AddCocinarisRecipeAi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AiUsageOptions>()
            .Bind(configuration.GetSection("Ai:Usage"))
            .Validate(options => options.IsValid(), "AI usage ceilings must be positive and coherent.")
            .ValidateOnStart();

        services.AddOptions<DeepSeekOptions>()
            .Bind(configuration.GetSection("Ai:Providers:DeepSeek"))
            .Validate(options => options.IsValid(environment.IsDevelopment() || environment.IsEnvironment("Testing")), "DeepSeek configuration is absent or unsafe for this environment.")
            .ValidateOnStart();

        services.AddOptions<AiPricingOptions>()
            .Bind(configuration.GetSection("Ai:Pricing"))
            .Validate(options => options.IsValid(), "AI model prices must be non-negative.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AiPricingOptions>>().Value);
        services.AddSingleton(AiOperationRegistry.CreateDefault());
        services.AddScoped<IAiUsageLedgerStore, PostgreSqlAiUsageLedgerStore>();
        services.AddScoped(provider =>
            new AiUsageGovernor(
                provider.GetRequiredService<IAiUsageLedgerStore>(),
                provider.GetRequiredService<IOptions<AiUsageOptions>>().Value,
                provider.GetRequiredService<AiPricingOptions>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IRecipeGenerationStore, PostgreSqlRecipeGenerationStore>();
        services.AddScoped<IRecipeStore, PostgreSqlRecipeStore>();
        services.AddScoped<IRecipeLibraryStore, PostgreSqlRecipeLibraryStore>();
        services.AddScoped<IRecipeCookNowUnitOfWork, PostgreSqlRecipeCookNowUnitOfWork>();
        services.AddScoped<IRecipeContextAssembler, RecipeContextAssembler>();
        services.AddScoped<RecipeCookNowApplicationService>();
        services.AddScoped<RecipeApiService>();
        services.AddScoped<IRecipeExecutionStore, PostgreSqlRecipeExecutionStore>();
        services.AddScoped<IRecipeExecutionUnitOfWork, PostgreSqlRecipeExecutionUnitOfWork>();
        services.AddScoped<RecipeExecutionApplicationService>();
        services.AddScoped<RecipeExecutionApiService>();
        services.AddScoped(provider => new RecipeLibraryApplicationService(
            provider.GetRequiredService<Cocinaris.Modules.Identity.ICurrentUserAccessor>(),
            provider.GetRequiredService<IRecipeLibraryStore>(),
            provider.GetRequiredService<IRecipeExecutionStore>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<RecipeThumbnailService>()));
        services.AddScoped<RecipeLibraryApiService>();

        var deepSeekSection = configuration.GetSection("Ai:Providers:DeepSeek");
        var enabled = deepSeekSection.GetValue("Enabled", false);
        var apiKey = deepSeekSection.GetValue<string>("ApiKey");
        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IAiProvider, FakeAiProvider>();
        }
        else if (enabled && !string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddHttpClient("cocinaris-deepseek", (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(120);
            });
            services.AddScoped<IAiProvider>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
                var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("cocinaris-deepseek");
                return new DeepSeekAiProvider(httpClient, options);
            });
        }
        else
        {
            services.AddSingleton<IAiProvider, UnavailableAiProvider>();
        }

        AddRecipeThumbnails(services, configuration, environment);
        return services;
    }

    /// <summary>
    /// Registers recipe thumbnails (PLAN-0030): the policy under <c>Ai:Images</c>, the OpenRouter
    /// provider when enabled with a key (a fake in Testing), the file store, and the background job.
    /// With generation disabled, existing images are still served and new ones are not queued.
    /// </summary>
    private static void AddRecipeThumbnails(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<RecipeThumbnailOptions>()
            .Bind(configuration.GetSection("Ai:Images"))
            .Validate(options => options.IsValid(), "Recipe thumbnail options must be positive and coherent.")
            .ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RecipeThumbnailOptions>>().Value);
        services.AddOptions<OpenRouterOptions>().Bind(configuration.GetSection("Ai:Providers:OpenRouter"));
        services.AddScoped<IRecipeThumbnailStore, PostgreSqlRecipeThumbnailStore>();
        services.AddSingleton<IRecipeThumbnailFileStore>(provider =>
        {
            var configured = provider.GetRequiredService<RecipeThumbnailOptions>().StoragePath;
            // systemd's StateDirectory sets STATE_DIRECTORY; development keeps files in .local-data.
            var root = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Environment.GetEnvironmentVariable("STATE_DIRECTORY") is { Length: > 0 } state
                    ? Path.Combine(state.Split(':')[0], "media", "thumbnails")
                    : Path.Combine(environment.ContentRootPath, ".local-data", "media", "thumbnails");
            return new FileSystemRecipeThumbnailFileStore(root);
        });

        var images = configuration.GetSection("Ai:Images");
        var apiKey = configuration.GetValue<string>("Ai:Providers:OpenRouter:ApiKey");
        if (environment.IsEnvironment("Testing"))
        {
            services.AddSingleton<IImageGenerationProvider>(provider =>
            {
                var options = provider.GetRequiredService<RecipeThumbnailOptions>();
                return new FakeImageProvider(options.ExpectedWidth, options.ExpectedHeight);
            });
        }
        else if (images.GetValue("Enabled", false) && !string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddHttpClient("cocinaris-openrouter", (provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<OpenRouterOptions>>().Value.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<RecipeThumbnailOptions>().TimeoutSeconds);
            });
            services.AddScoped<IImageGenerationProvider>(provider => new OpenRouterImageProvider(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("cocinaris-openrouter"),
                provider.GetRequiredService<IOptions<OpenRouterOptions>>().Value,
                provider.GetRequiredService<RecipeThumbnailOptions>().Model));
        }

        services.AddSingleton<IThumbnailImageEncoder, SkiaThumbnailImageEncoder>();
        services.AddScoped(provider => new RecipeThumbnailService(
            provider.GetRequiredService<IRecipeThumbnailStore>(),
            provider.GetRequiredService<IRecipeThumbnailFileStore>(),
            provider.GetRequiredService<IThumbnailImageEncoder>(),
            provider.GetService<IImageGenerationProvider>(),
            provider.GetRequiredService<AiUsageGovernor>(),
            provider.GetRequiredService<RecipeThumbnailOptions>(),
            provider.GetRequiredService<TimeProvider>()));
        if (!environment.IsEnvironment("Testing"))
        {
            services.AddHostedService<RecipeThumbnailWorker>();
        }
    }
}
