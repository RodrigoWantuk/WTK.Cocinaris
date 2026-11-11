using System.Diagnostics.Metrics;
using Cocinaris.Api.Services;
using Cocinaris.SharedKernel.Privacy;
using Microsoft.Extensions.Options;

namespace Cocinaris.Api.Privacy;

/// <summary>
/// One retention run (PLAN-0049 Slice 3): computes the limits from the documented decisions and applies
/// every rule. The shared recipe library is never part of it (decision D5).
/// </summary>
public sealed class RetentionJob(IRetentionStore store, IOptions<IdempotencyOptions> idempotency, TimeProvider timeProvider, ILogger<RetentionJob> logger)
{
    /// <summary>Rows per rule and run; a backlog is worked off over several days.</summary>
    public const int MaxRowsPerRule = 5000;

    /// <summary>How long AI usage is kept (decision D4).</summary>
    public const int UsageMonths = 13;

    /// <summary>How long a discarded plan and the last-notified day are kept.</summary>
    public static readonly TimeSpan PlanningLeftovers = TimeSpan.FromDays(30);

    private static readonly Meter Meter = new("Cocinaris.Privacy.Retention");
    private static readonly Counter<long> Removed = Meter.CreateCounter<long>("cocinaris.privacy.retention_deleted", "{row}", "Rows deleted or cleared by the retention job, by rule.");

    /// <summary>The limits of a run at <paramref name="now"/>.</summary>
    public RetentionCutoffs CutoffsAt(DateTimeOffset now) => new(
        now - idempotency.Value.Retention,
        now.AddMonths(-UsageMonths),
        now - SessionGuard.TombstoneLifetime,
        DateOnly.FromDateTime(now.UtcDateTime).AddDays(-PlanningLeftovers.Days),
        now - PlanningLeftovers);

    /// <summary>Runs every rule once; returns the counts per rule.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RunAsync(CancellationToken cancellationToken)
    {
        var counts = await store.ApplyAsync(CutoffsAt(timeProvider.GetUtcNow()), MaxRowsPerRule, cancellationToken);
        foreach (var (rule, count) in counts.Where(pair => pair.Value > 0))
        {
            Removed.Add(count, new KeyValuePair<string, object?>("rule", rule));
        }

        logger.LogInformation("Retention run: {Counts}", string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}")));
        return counts;
    }
}

/// <summary>Runs <see cref="RetentionJob"/> ten minutes after start and then once a day.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<RetentionWorker> logger) : BackgroundService
{
    /// <summary>Time between runs.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Wait after start, so a deploy is not slowed by the first run.</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Retention run failed: {ExceptionType}", exception.GetType().Name);
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
}
