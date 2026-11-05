using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.Api.Planning;

/// <summary>
/// Sends the day's plan notification (ADR-0013). Runs at the top of each hour, and once at start, so a
/// restart inside the sending hour still counts. Without push settings it does nothing. Failures are
/// logged by exception type only, and one owner's failure does not stop the others.
/// </summary>
public sealed class PlanReminderPushWorker(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<PlanReminderPushWorker> logger) : BackgroundService
{
    private const int MaxOwnersPerRun = 5000;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(scopes, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Plan reminder push run failed: {ExceptionType}", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(PlanReminderPushSchedule.DelayToNextHour(timeProvider.GetUtcNow()), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One run; returns how many owners were notified. Public so tests can drive it without the timer.</summary>
    /// <param name="scopes">Creates one scope per owner, so no tracked state crosses owners.</param>
    /// <param name="logger">Log.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, ILogger logger, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> owners;
        await using (var listing = scopes.CreateAsyncScope())
        {
            if (listing.ServiceProvider.GetRequiredService<IWebPushSender>().PublicKey is null)
            {
                return 0;
            }

            owners = await listing.ServiceProvider.GetRequiredService<IPushSubscriptionStore>().ListOwnersAsync(MaxOwnersPerRun, cancellationToken);
        }

        var sent = 0;
        foreach (var owner in owners)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<PlanReminderPushService>().ProcessOwnerAsync(owner, cancellationToken);
                sent += outcome == PlanReminderPushOutcome.Sent ? 1 : 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Plan reminder push failed for one owner: {ExceptionType}", exception.GetType().Name);
            }
        }

        return sent;
    }
}
