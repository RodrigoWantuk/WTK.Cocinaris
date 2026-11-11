using Cocinaris.Modules.Planning.Application;

namespace Cocinaris.Api.Planning;

/// <summary>
/// Sends the day's plan notification (ADR-0013; a job kind of PLAN-0039 Slice 3). Runs at the top of
/// each hour, and a minute after start, so a restart inside the sending hour still counts. Without
/// push settings it does nothing. One owner's failure is logged by exception type and does not stop
/// the others.
/// </summary>
public sealed class PlanReminderPushJob(IServiceScopeFactory scopes, ILogger<PlanReminderPushJob> logger) : Cocinaris.SharedKernel.Jobs.IScheduledJob
{
    private const int MaxOwnersPerRun = 5000;

    /// <inheritdoc />
    public string Kind => "plan_reminders";

    /// <inheritdoc />
    public DateTimeOffset FirstRunAt(DateTimeOffset now) => now + TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public DateTimeOffset NextRunAt(DateTimeOffset now) => now + PlanReminderPushSchedule.DelayToNextHour(now);

    /// <inheritdoc />
    public async Task<Cocinaris.SharedKernel.Jobs.JobResult> HandleAsync(Cocinaris.SharedKernel.Jobs.JobContext job, CancellationToken cancellationToken)
    {
        await RunOnceAsync(scopes, logger, cancellationToken);
        return Cocinaris.SharedKernel.Jobs.JobResult.Succeeded;
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
