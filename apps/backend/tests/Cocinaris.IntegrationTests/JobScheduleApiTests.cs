using Cocinaris.Api.Jobs;
using Cocinaris.Api.Privacy;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.SharedKernel.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cocinaris.IntegrationTests;

/// <summary>PLAN-0039 Slice 3: scheduled work runs as jobs of one runner, never ends on a failure, and lost work is queued again.</summary>
public sealed partial class RecipeExecutionApiTests
{
    [Fact]
    public async Task ScheduledJobsAreQueuedOnceRepeatAfterEveryRunAndLostDeletionsComeBack()
    {
        var (host, _) = PlanHost();
        await using var _host = host;
        using (var scope = host.Services.CreateScope())
        {
            await JobRunner.QueueScheduledAsync(scope.ServiceProvider, TimeProvider.System, CancellationToken.None);
            // A second start keeps the times already planned.
            await JobRunner.QueueScheduledAsync(scope.ServiceProvider, TimeProvider.System, CancellationToken.None);
        }

        List<JobRecord> scheduled;
        using (var scope = host.Services.CreateScope())
        {
            scheduled = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.AsNoTracking().Where(job => job.SubjectKey == JobRunner.ScheduleSubject).ToListAsync();
        }

        // Every kind the host has, once; the others depend on settings the test host leaves off.
        Assert.Contains("retention", scheduled.Select(job => job.Kind));
        Assert.Contains("job_recovery", scheduled.Select(job => job.Kind));
        Assert.Equal(scheduled.Count, scheduled.Select(job => job.Kind).Distinct().Count());
        Assert.All(scheduled, job => Assert.Equal("Queued", job.State));
        Assert.True(scheduled.Single(job => job.Kind == "retention").NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(9));

        // A pending deletion whose job was lost (recorded, then the request died).
        var deletionId = Guid.NewGuid();
        using (var scope = host.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.AccountDeletions.Add(new AccountDeletionRecord { Id = deletionId, SubjectHash = new string('a', 64), OwnerUserId = Guid.NewGuid(), State = "Pending", RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-30) });
            await database.SaveChangesAsync();
            // Both scheduled jobs come due now, and retention is made to fail once.
            await database.Jobs.Where(job => job.SubjectKey == JobRunner.ScheduleSubject && (job.Kind == "retention" || job.Kind == "job_recovery"))
                .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await database.Database.ExecuteSqlRawAsync("ALTER TABLE platform.idempotency_records RENAME TO idempotency_records_hidden");
        }

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<JobProcessor>().RunDueAsync(CancellationToken.None));
            }
        }
        finally
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("ALTER TABLE platform.idempotency_records_hidden RENAME TO idempotency_records");
        }

        using (var scope = host.Services.CreateScope())
        {
            var jobs = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Jobs.AsNoTracking().ToListAsync();
            // The failed run waits for its next time instead of ending, and keeps why it failed.
            var retention = jobs.Single(job => job.Kind == "retention");
            Assert.Equal(("Queued", 0, "PostgresException"), (retention.State, retention.Attempts, retention.LastFailureCode));
            Assert.True(retention.NextAttemptAt > DateTimeOffset.UtcNow.AddHours(23));
            var recovery = jobs.Single(job => job.Kind == "job_recovery");
            Assert.Equal(("Queued", (string?)null), (recovery.State, recovery.LastFailureCode));
            Assert.True(recovery.NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(9));
            // The recovery queued the lost deletion.
            Assert.Equal(("account_deletion", "Queued"), jobs.Where(job => job.SubjectKey == deletionId.ToString()).Select(job => (job.Kind, job.State)).Single());
        }
    }
}
