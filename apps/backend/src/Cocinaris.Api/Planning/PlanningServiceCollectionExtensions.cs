using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.Api.Planning;

/// <summary>Registers the Planning module (PLAN-0029) for the API composition root.</summary>
public static class PlanningServiceCollectionExtensions
{
    /// <summary>Adds menu planning, its PostgreSQL store, and the HTTP adapter.</summary>
    public static IServiceCollection AddCocinarisPlanning(this IServiceCollection services)
    {
        services.AddScoped<IMenuPlanStore, PostgreSqlMenuPlanStore>();
        services.AddScoped<MenuPlanningApplicationService>();
        services.AddScoped<PlanningApiService>();
        return services;
    }
}
