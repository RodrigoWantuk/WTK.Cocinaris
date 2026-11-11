using Cocinaris.SharedKernel.Jobs;

namespace Cocinaris.Api.Jobs;

/// <summary>Registers job handlers (PLAN-0039): each is scoped, and also known to the runner as an <see cref="IJobHandler"/>.</summary>
public static class JobServiceCollectionExtensions
{
    /// <summary>Registers <typeparamref name="T"/> as a job handler, scheduled or not.</summary>
    public static IServiceCollection AddJobHandler<T>(this IServiceCollection services)
        where T : class, IJobHandler
    {
        services.AddScoped<T>();
        services.AddScoped<IJobHandler>(provider => provider.GetRequiredService<T>());
        return services;
    }
}
