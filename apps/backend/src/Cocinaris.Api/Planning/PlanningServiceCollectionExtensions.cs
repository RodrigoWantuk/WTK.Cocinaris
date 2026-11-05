using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Api.Planning;

/// <summary>Registers the Planning module (PLAN-0029) for the API composition root.</summary>
public static class PlanningServiceCollectionExtensions
{
    /// <summary>Adds menu planning, its PostgreSQL store, and the HTTP adapter.</summary>
    public static IServiceCollection AddCocinarisPlanning(this IServiceCollection services)
    {
        services.AddScoped<IMenuPlanStore, PostgreSqlMenuPlanStore>();
        services.AddScoped<MenuPlanningApplicationService>();
        // The cooking proposal reads planning commitments through a port the Recipes module owns.
        services.AddScoped<IPantryCommitmentLookup, PantryCommitmentLookup>();
        services.AddScoped<IExecutionPlanLookup, ExecutionPlanLookup>();
        services.AddScoped<PlanningApiService>();
        services.AddScoped<IShoppingPlanStore, PostgreSqlShoppingPlanStore>();
        services.AddScoped<ShoppingPlanApplicationService>();
        services.AddScoped<ShoppingPlanRouteService>();
        services.AddScoped<ShoppingPlanApiService>();
        services.AddScoped<ShoppingArrivalService>();
        services.AddOptions<WebPushOptions>().BindConfiguration("Planning:WebPush");
        services.AddHttpClient(WebPushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<IWebPushSender, WebPushSender>();
        services.AddScoped<IPushSubscriptionStore, PostgreSqlPushSubscriptionStore>();
        services.AddScoped<PushSubscriptionService>();
        services.AddScoped<PlanReminderPushService>();
        return services;
    }
}
