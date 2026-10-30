using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Profiles.Application;

namespace Cocinaris.Api.Inventory;

/// <summary>
/// Background job that removes pantry lots whose auto-removal day has passed (PLAN-0056). Every
/// 30 minutes it reads lots due in some time zone (day at most UTC today), resolves each owner's
/// time zone from their profile, and removes only those whose day has ended there. Failures are
/// logged by exception type only, and the next run retries.
/// </summary>
public sealed class InventoryAutoRemovalWorker(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<InventoryAutoRemovalWorker> logger) : BackgroundService
{
    /// <summary>Time between runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(scopes, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Inventory auto-removal run failed: {ExceptionType}", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One run; returns the number of lots removed. Public so tests can drive it without the timer.</summary>
    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        // The earliest time zones are a day ahead of UTC, so anything due anywhere has a day of at most UTC today.
        var utcToday = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var due = await services.GetRequiredService<IInventoryAutoRemovalSource>().FindDueAsync(utcToday.AddDays(1), 500, cancellationToken);
        var removal = services.GetRequiredService<InventoryAutoRemovalService>();
        var profiles = services.GetRequiredService<IGetProfileSessionProjectionUseCase>();
        var removed = 0;
        foreach (var owner in due.GroupBy(item => item.OwnerUserId))
        {
            var projection = await profiles.GetAsync(owner.Key, cancellationToken);
            var zone = projection.TimeZone is { Length: > 0 } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
            var ownerToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
            foreach (var lot in owner)
            {
                if (await removal.RemoveIfDueAsync(owner.Key, lot.LotId, ownerToday, cancellationToken))
                {
                    removed++;
                }
            }
        }

        return removed;
    }
}
