using System.Threading.Channels;

namespace Cocinaris.SharedKernel.Jobs;

/// <summary>A job's state (PLAN-0039 Slice 2).</summary>
public enum JobState
{
    /// <summary>Waiting for its next attempt.</summary>
    Queued,

    /// <summary>Held by a runner until its lease ends.</summary>
    Running,

    /// <summary>Done.</summary>
    Succeeded,

    /// <summary>Given up after its attempts, with a closed failure code.</summary>
    Failed,

    /// <summary>No longer wanted, for example a dish the person took back.</summary>
    Cancelled,
}

/// <summary>A job to run.</summary>
/// <param name="Kind">Closed job kind, such as <c>plan_card_expansion</c>.</param>
/// <param name="OwnerUserId">The person the work is for, or null for system work.</param>
/// <param name="SubjectKey">What the job is about, unique per kind, so asking twice keeps one job.</param>
/// <param name="PayloadJson">Small JSON the handler needs; ids and codes, never personal text.</param>
/// <param name="RunAt">Earliest start.</param>
/// <param name="MaxAttempts">Attempts that may fail before the job is given up.</param>
public sealed record JobRequest(string Kind, Guid? OwnerUserId, string SubjectKey, string PayloadJson, DateTimeOffset RunAt, int MaxAttempts);

/// <summary>Queues and cancels jobs; the runner side is <see cref="IJobStore"/>.</summary>
public interface IJobQueue
{
    /// <summary>
    /// Queues a job. With <paramref name="restart"/> false, a job already queued or running for the same
    /// subject is kept, and a finished one is queued again. With it true, a queued one also starts over
    /// with no attempts used; a running one is never touched.
    /// </summary>
    Task EnqueueAsync(JobRequest request, bool restart, CancellationToken cancellationToken);

    /// <summary>Cancels a queued job of the subject; a running one finishes, and its handler finds nothing to do.</summary>
    Task CancelAsync(string kind, string subjectKey, CancellationToken cancellationToken);
}

/// <summary>A claimed job, as a handler sees it.</summary>
/// <param name="JobId">Job id.</param>
/// <param name="Kind">Job kind.</param>
/// <param name="OwnerUserId">The person, if any.</param>
/// <param name="SubjectKey">Subject.</param>
/// <param name="PayloadJson">Payload.</param>
/// <param name="Attempt">This attempt's number, from 1.</param>
/// <param name="MaxAttempts">Attempts allowed.</param>
public sealed record JobContext(Guid JobId, string Kind, Guid? OwnerUserId, string SubjectKey, string PayloadJson, int Attempt, int MaxAttempts)
{
    /// <summary>Gets whether a failure now gives the job up.</summary>
    public bool IsLastAttempt => Attempt >= MaxAttempts;
}

/// <summary>What a handler reports.</summary>
/// <param name="Outcome">The result.</param>
/// <param name="Code">A closed code for a retry or a failure.</param>
/// <param name="RetryAfter">For a retry, when to try again; null uses the kind's backoff.</param>
/// <param name="CountsAsAttempt">For a retry, whether it uses up an attempt; a wait for the person's other AI work does not.</param>
/// <param name="RepeatAt">For a scheduled job, when it runs next, with every attempt back; null otherwise.</param>
public sealed record JobResult(JobState Outcome, string? Code = null, TimeSpan? RetryAfter = null, bool CountsAsAttempt = true, DateTimeOffset? RepeatAt = null)
{
    /// <summary>Done.</summary>
    public static JobResult Succeeded { get; } = new(JobState.Succeeded);

    /// <summary>Try again later.</summary>
    public static JobResult Retry(string code, TimeSpan? after = null, bool countsAsAttempt = true) => new(JobState.Queued, code, after, countsAsAttempt);

    /// <summary>Give up now.</summary>
    public static JobResult Failed(string code) => new(JobState.Failed, code);

    /// <summary>A scheduled job's run is over: run again at <paramref name="at"/>, keeping <paramref name="code"/> when this run failed.</summary>
    public static JobResult Repeat(DateTimeOffset at, string? code = null) => new(JobState.Queued, code, RepeatAt: at);
}

/// <summary>
/// A job that runs on a schedule rather than for one subject (PLAN-0039 Slice 3): one row per kind,
/// queued at start, that queues itself again after every run. A failed run never ends it; it only
/// waits for the next.
/// </summary>
public interface IScheduledJob : IJobHandler
{
    /// <summary>When the first run after a start is due, so a deploy is not slowed by every job at once.</summary>
    DateTimeOffset FirstRunAt(DateTimeOffset now);

    /// <summary>When the run after one that ended at <paramref name="now"/> is due.</summary>
    DateTimeOffset NextRunAt(DateTimeOffset now);
}

/// <summary>Finds work that lost its job, such as a deletion whose job was never queued, and queues it again.</summary>
public interface IJobRecovery
{
    /// <summary>Queues jobs for lost work; returns how many.</summary>
    Task<int> RecoverAsync(IJobQueue queue, CancellationToken cancellationToken);
}

/// <summary>Runs one kind of job. Handlers are idempotent: a job may run again after a lost lease.</summary>
public interface IJobHandler
{
    /// <summary>Gets the kind this handler runs.</summary>
    string Kind { get; }

    /// <summary>Runs one attempt.</summary>
    Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken);
}

/// <summary>The runner's side of the job table.</summary>
public interface IJobStore
{
    /// <summary>Claims up to <paramref name="max"/> due jobs, queued ones and running ones whose lease ended, for <paramref name="lease"/>.</summary>
    Task<IReadOnlyList<JobContext>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int max, CancellationToken cancellationToken);

    /// <summary>Records the result of an attempt; a retry past the last attempt becomes a failure, and a repeat starts over.</summary>
    Task FinishAsync(JobContext job, JobResult result, DateTimeOffset now, TimeSpan defaultRetryAfter, CancellationToken cancellationToken);

    /// <summary>When the next queued job is due, or null when none is queued; the runner sleeps until then.</summary>
    Task<DateTimeOffset?> NextDueAtAsync(CancellationToken cancellationToken);
}

/// <summary>Wakes the runner when a job is queued, so it starts within moments rather than at the next poll.</summary>
public sealed class JobSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>Asks the runner to look for work.</summary>
    public void Notify() => channel.Writer.TryWrite(true);

    /// <summary>Waits for a notification or the timeout.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        try
        {
            await channel.Reader.WaitToReadAsync(timer.Token);
            channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout passed; the runner polls anyway.
        }
    }
}
