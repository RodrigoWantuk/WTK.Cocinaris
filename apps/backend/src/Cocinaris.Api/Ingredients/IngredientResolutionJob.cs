using Cocinaris.Modules.Ingredients;
using Cocinaris.SharedKernel.Jobs;

namespace Cocinaris.Api.Ingredients;

/// <summary>
/// Scheduled job that syncs the ingredient catalog and drains the name resolution queue (PLAN-0055; a
/// job kind of PLAN-0039 Slice 3), one batch per run. With work left it runs again at once; otherwise
/// after <c>Ingredients:Resolution:PollSeconds</c>. The queue keeps its own table, because it is also
/// where unrecognized names are kept for a later catalog.
/// </summary>
public sealed class IngredientResolutionJob(IngredientResolutionService resolution, IngredientResolutionOptions options) : IScheduledJob
{
    /// <inheritdoc />
    public string Kind => "ingredient_resolution";

    /// <inheritdoc />
    public DateTimeOffset FirstRunAt(DateTimeOffset now) => now + TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public DateTimeOffset NextRunAt(DateTimeOffset now) => now + TimeSpan.FromSeconds(options.PollSeconds);

    /// <inheritdoc />
    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken) =>
        await resolution.ProcessNextBatchAsync(cancellationToken)
            ? JobResult.Repeat(DateTimeOffset.UtcNow)
            : JobResult.Succeeded;
}
