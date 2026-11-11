using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cocinaris.SharedKernel.Jobs;

namespace Cocinaris.Api.Jobs;

/// <summary>
/// Runs due jobs (PLAN-0039 Slice 2, decision R10): claims up to <see cref="Parallelism"/> at a time,
/// runs each in its own scope with its kind's handler, and records the result. A job whose handler
/// throws is retried like any failure; one with no handler is given up.
/// </summary>
public sealed class JobProcessor(IJobStore store, IEnumerable<IJobHandler> handlers, IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<JobProcessor> logger)
{
    /// <summary>Jobs run at once. Two matches a person's two concurrent AI calls.</summary>
    public const int Parallelism = 2;

    /// <summary>How long a claim lasts; longer than any handler's work, so a live job is never taken twice.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    /// <summary>Backoff after a failed attempt, by attempt number (decision R8); later attempts use the last.</summary>
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    private static readonly Meter Meter = new("Cocinaris.Jobs");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("cocinaris.jobs.run", "{run}", "Job attempts by kind and outcome: succeeded, retried, failed, or cancelled.");
    private static readonly Histogram<double> Lag = Meter.CreateHistogram<double>("cocinaris.jobs.lag", "s", "Seconds between a job becoming due and its attempt starting.");

    private readonly Dictionary<string, IJobHandler> byKind = handlers.ToDictionary(handler => handler.Kind, StringComparer.Ordinal);

    /// <summary>Runs one batch of due jobs; returns how many were run.</summary>
    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await store.ClaimDueAsync(timeProvider.GetUtcNow(), Lease, Parallelism, cancellationToken);
        await Task.WhenAll(claimed.Select(job => RunAsync(job, cancellationToken)));
        return claimed.Count;
    }

    private async Task RunAsync(JobContext job, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        JobResult result;
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            result = byKind.TryGetValue(job.Kind, out var known)
                ? await ((IJobHandler)scope.ServiceProvider.GetRequiredService(known.GetType())).HandleAsync(job, cancellationToken)
                : JobResult.Failed("no_handler");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Job {JobId} of kind {Kind} threw {ExceptionType}", job.JobId, job.Kind, exception.GetType().Name);
            result = JobResult.Retry(exception.GetType().Name);
        }

        var retryAfter = Backoff[Math.Clamp(job.Attempt - 1, 0, Backoff.Length - 1)];
        await scope.ServiceProvider.GetRequiredService<IJobStore>().FinishAsync(job, result, timeProvider.GetUtcNow(), retryAfter, CancellationToken.None);
        var outcome = result.Outcome switch
        {
            JobState.Queued => job.IsLastAttempt && result.CountsAsAttempt ? "failed" : "retried",
            JobState.Succeeded => "succeeded",
            JobState.Cancelled => "cancelled",
            _ => "failed",
        };
        Runs.Add(1, new KeyValuePair<string, object?>("kind", job.Kind), new KeyValuePair<string, object?>("outcome", outcome));
        Lag.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("kind", job.Kind));
    }
}

/// <summary>Runs <see cref="JobProcessor"/> as soon as a job is queued, and every 15 seconds; resumes leased jobs after a restart.</summary>
public sealed class JobRunner(IServiceScopeFactory scopes, JobSignal signal, ILogger<JobRunner> logger) : BackgroundService
{
    /// <summary>Longest wait between looks for work.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ran = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                ran = await scope.ServiceProvider.GetRequiredService<JobProcessor>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Job run failed: {ExceptionType}", exception.GetType().Name);
            }

            // A full batch means more may be waiting; look again at once.
            if (ran >= JobProcessor.Parallelism)
            {
                continue;
            }

            try
            {
                await signal.WaitAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
