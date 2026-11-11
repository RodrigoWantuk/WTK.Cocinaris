using System.Diagnostics.Metrics;
using Cocinaris.Modules.Identity;
using Cocinaris.SharedKernel.Jobs;
using Cocinaris.SharedKernel.Privacy;

namespace Cocinaris.Api.Privacy;

/// <summary>Why a deletion request was refused, as a closed code.</summary>
public enum AccountDeletionRefusal
{
    /// <summary>The typed word is not the confirmation word.</summary>
    WrongConfirmation,

    /// <summary>The session's sign-in is older than <see cref="SessionGuard.RecentSignIn"/> (decision E1).</summary>
    ReauthenticationRequired,
}

/// <summary>
/// Asks for an account deletion (PLAN-0049 Slice 2, decision D1: at once, no way back). The request is
/// recorded durably, its job is queued, and every session of the account ends; the erasure itself
/// runs in <see cref="AccountDeletionJob"/>.
/// </summary>
public sealed class AccountDeletionService(
    ICurrentUserAccessor currentUser,
    IAccountDeletionStore store,
    AccountDeletionMarks marks,
    IJobQueue jobs,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor)
{
    /// <summary>The words that confirm, one per language; compared without case or surrounding spaces.</summary>
    public static readonly IReadOnlySet<string> ConfirmationWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EXCLUIR", "DELETE", "ELIMINAR" };

    /// <summary>Records the deletion of the current account, or returns the one already pending.</summary>
    /// <param name="confirmation">What the person typed.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>When the deletion was requested, or why it was refused.</returns>
    public async Task<(DateTimeOffset? RequestedAt, AccountDeletionRefusal? Refusal)> RequestAsync(string? confirmation, CancellationToken cancellationToken)
    {
        if (confirmation is null || !ConfirmationWords.Contains(confirmation.Trim()))
        {
            return (null, AccountDeletionRefusal.WrongConfirmation);
        }

        var principal = httpContextAccessor.HttpContext?.User ?? throw new UnauthorizedAccessException("No request.");
        var now = timeProvider.GetUtcNow();
        if (SessionGuard.SignedInAt(principal) is not { } signedInAt || now - signedInAt > SessionGuard.RecentSignIn)
        {
            return (null, AccountDeletionRefusal.ReauthenticationRequired);
        }

        var login = SessionGuard.LoginOf(principal) ?? throw new UnauthorizedAccessException("The authenticated identity is incomplete.");
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (deletionId, requestedAt) = await store.RequestAsync(SessionGuard.HashOf(login.Issuer, login.Subject), user.Id, now, cancellationToken);
        marks.Invalidate();
        // The erasure runs as a job at once (PLAN-0039 Slice 3); the recovery job queues it again if this is lost.
        await jobs.EnqueueAsync(AccountDeletionJob.Request(deletionId, now), restart: false, cancellationToken);
        return (requestedAt, null);
    }
}

/// <summary>
/// Erases one account (PLAN-0049 Slice 2) as a job of PLAN-0039 Slice 3: one transaction per attempt,
/// retried by the runner's backoff with only the exception type recorded. Twenty attempts take about 90 minutes;
/// a deletion still pending after them shows in the runbook query, and the recovery job queues it again.
/// </summary>
public sealed class AccountDeletionJob(IAccountDeletionStore store, AccountDeletionMarks marks, TimeProvider timeProvider) : IJobHandler
{
    /// <summary>Job kind.</summary>
    public const string JobKind = "account_deletion";

    /// <summary>Attempts before the job stops trying on its own.</summary>
    public const int MaxAttempts = 20;

    private static readonly Meter Meter = new("Cocinaris.Privacy.Deletion");
    private static readonly Counter<long> Deletions = Meter.CreateCounter<long>("cocinaris.privacy.deletion", "{deletion}", "Account erasures attempted, by outcome: completed or failed.");

    /// <inheritdoc />
    public string Kind => JobKind;

    /// <summary>The job of one deletion, due at <paramref name="at"/>.</summary>
    /// <param name="deletionId">The deletion.</param>
    /// <param name="at">When it is due.</param>
    public static JobRequest Request(Guid deletionId, DateTimeOffset at) =>
        new(JobKind, null, deletionId.ToString(), "{}", at, MaxAttempts);

    /// <inheritdoc />
    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(job.SubjectKey, out var deletionId))
        {
            return JobResult.Failed("invalid_subject");
        }

        try
        {
            if (await store.EraseAsync(deletionId, timeProvider.GetUtcNow(), cancellationToken) is not null)
            {
                Deletions.Add(1, new KeyValuePair<string, object?>("outcome", "completed"));
                // A completed deletion lets the login start a new account at once.
                marks.Invalidate();
            }

            return JobResult.Succeeded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Deletions.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            return JobResult.Retry(exception.GetType().Name);
        }
    }
}

/// <summary>Queues a job for every pending deletion that has none, such as one whose request failed after it was recorded.</summary>
public sealed class AccountDeletionRecovery(IAccountDeletionStore store, TimeProvider timeProvider) : IJobRecovery
{
    /// <inheritdoc />
    public async Task<int> RecoverAsync(IJobQueue queue, CancellationToken cancellationToken)
    {
        var pending = await store.ListPendingAsync(100, cancellationToken);
        foreach (var deletionId in pending)
        {
            // An active job is kept; only a lost or finished one is queued again.
            await queue.EnqueueAsync(AccountDeletionJob.Request(deletionId, timeProvider.GetUtcNow()), restart: false, cancellationToken);
        }

        return pending.Count;
    }
}

/// <summary>Runs every <see cref="IJobRecovery"/> every 10 minutes (PLAN-0039 Slice 3), so no work waits forever for a lost job.</summary>
public sealed class JobRecoveryJob(IEnumerable<IJobRecovery> recoveries, IJobQueue queue) : IScheduledJob
{
    /// <inheritdoc />
    public string Kind => "job_recovery";

    /// <inheritdoc />
    public DateTimeOffset FirstRunAt(DateTimeOffset now) => now + TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public DateTimeOffset NextRunAt(DateTimeOffset now) => now + TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken)
    {
        foreach (var recovery in recoveries)
        {
            await recovery.RecoverAsync(queue, cancellationToken);
        }

        return JobResult.Succeeded;
    }
}
