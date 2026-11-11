using Cocinaris.SharedKernel.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>
/// The job table in PostgreSQL (PLAN-0039 Slice 2, ADR-0003's database-only bootstrap). Claims use
/// <c>FOR UPDATE SKIP LOCKED</c>, so two runners never take the same job, and a lease that ends puts a
/// job whose runner died back in play.
/// </summary>
public sealed class PostgreSqlJobStore(ApplicationDbContext database, JobSignal signal, TimeProvider timeProvider) : IJobQueue, IJobStore
{
    /// <summary>Runs of one job at most, waits included, so a job can never loop forever.</summary>
    public const int MaxRuns = 40;

    /// <inheritdoc />
    public async Task EnqueueAsync(JobRequest request, bool restart, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        // A running job is never reset; a queued one only on restart; a finished one always.
        var when = restart ? "jobs.\"State\" <> 'Running'" : "jobs.\"State\" IN ('Succeeded', 'Failed', 'Cancelled')";
        var sql = $"""
            INSERT INTO platform.jobs AS jobs ("Id", "Kind", "OwnerUserId", "SubjectKey", "PayloadJson", "State", "Attempts", "MaxAttempts", "Runs", "NextAttemptAt", "CreatedAt", "UpdatedAt")
            VALUES (@id, @kind, @owner, @subject, CAST(@payload AS jsonb), 'Queued', 0, @max, 0, @runAt, @now, @now)
            ON CONFLICT ("Kind", "SubjectKey") DO UPDATE SET
                "State" = 'Queued', "Attempts" = 0, "Runs" = 0, "MaxAttempts" = EXCLUDED."MaxAttempts", "PayloadJson" = EXCLUDED."PayloadJson",
                "OwnerUserId" = EXCLUDED."OwnerUserId", "NextAttemptAt" = EXCLUDED."NextAttemptAt", "LeaseUntil" = NULL,
                "LastFailureCode" = NULL, "CompletedAt" = NULL, "UpdatedAt" = EXCLUDED."UpdatedAt"
            WHERE {when}
            """;
        await database.Database.ExecuteSqlRawAsync(sql,
        [
            new NpgsqlParameter("id", Guid.NewGuid()),
            new NpgsqlParameter("kind", request.Kind),
            new NpgsqlParameter("owner", (object?)request.OwnerUserId ?? DBNull.Value),
            new NpgsqlParameter("subject", request.SubjectKey),
            new NpgsqlParameter("payload", request.PayloadJson),
            new NpgsqlParameter("max", Math.Max(1, request.MaxAttempts)),
            new NpgsqlParameter("runAt", request.RunAt),
            new NpgsqlParameter("now", now),
        ], cancellationToken);
        signal.Notify();
    }

    /// <inheritdoc />
    public Task CancelAsync(string kind, string subjectKey, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return database.Jobs.Where(job => job.Kind == kind && job.SubjectKey == subjectKey && job.State == nameof(JobState.Queued))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.State, nameof(JobState.Cancelled))
                .SetProperty(job => job.CompletedAt, now)
                .SetProperty(job => job.UpdatedAt, now), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobContext>> ClaimDueAsync(DateTimeOffset now, TimeSpan lease, int max, CancellationToken cancellationToken)
    {
        var claimed = await database.Jobs.FromSqlRaw("""
            UPDATE platform.jobs SET "State" = 'Running', "Attempts" = "Attempts" + 1, "Runs" = "Runs" + 1, "LeaseUntil" = @leaseUntil, "UpdatedAt" = @now
            WHERE "Id" IN (
                SELECT "Id" FROM platform.jobs
                WHERE ("State" = 'Queued' AND "NextAttemptAt" <= @now) OR ("State" = 'Running' AND "LeaseUntil" < @now)
                ORDER BY "NextAttemptAt"
                LIMIT @max
                FOR UPDATE SKIP LOCKED)
            RETURNING *
            """,
            new NpgsqlParameter("leaseUntil", now + lease),
            new NpgsqlParameter("now", now),
            new NpgsqlParameter("max", max))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return claimed.Select(job => new JobContext(job.Id, job.Kind, job.OwnerUserId, job.SubjectKey, job.PayloadJson, job.Attempts, job.MaxAttempts)).ToList();
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> NextDueAtAsync(CancellationToken cancellationToken) =>
        await database.Jobs.Where(job => job.State == nameof(JobState.Queued)).MinAsync(job => (DateTimeOffset?)job.NextAttemptAt, cancellationToken);

    /// <inheritdoc />
    public async Task FinishAsync(JobContext job, JobResult result, DateTimeOffset now, TimeSpan defaultRetryAfter, CancellationToken cancellationToken)
    {
        var record = await database.Jobs.SingleOrDefaultAsync(item => item.Id == job.JobId, cancellationToken);
        // A cancel or a restart while it ran wins over this attempt's result.
        if (record is null || record.State != nameof(JobState.Running))
        {
            return;
        }

        if (result.RepeatAt is { } repeatAt)
        {
            // A scheduled job starts over: every attempt back, due at its next run.
            record.State = nameof(JobState.Queued);
            record.Attempts = 0;
            record.Runs = 0;
            record.LeaseUntil = null;
            record.NextAttemptAt = repeatAt;
            record.LastFailureCode = result.Code;
            record.CompletedAt = null;
            record.UpdatedAt = now;
            await database.SaveChangesAsync(cancellationToken);
            database.ChangeTracker.Clear();
            return;
        }

        var retry = result.Outcome == JobState.Queued;
        var attempts = retry && !result.CountsAsAttempt ? record.Attempts - 1 : record.Attempts;
        var exhausted = retry && (attempts >= record.MaxAttempts || record.Runs >= MaxRuns);
        record.Attempts = attempts;
        record.LeaseUntil = null;
        record.UpdatedAt = now;
        record.LastFailureCode = result.Code is null ? record.LastFailureCode : result.Code.Length > 120 ? result.Code[..120] : result.Code;
        if (retry && !exhausted)
        {
            record.State = nameof(JobState.Queued);
            record.NextAttemptAt = now + (result.RetryAfter ?? defaultRetryAfter);
        }
        else
        {
            record.State = retry ? nameof(JobState.Failed) : result.Outcome.ToString();
            record.CompletedAt = now;
        }

        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
    }
}
