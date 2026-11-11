using Cocinaris.Modules.Recipes.Thumbnails;
using Cocinaris.SharedKernel.Jobs;

namespace Cocinaris.Api.Recipes;

/// <summary>
/// Scheduled job that drains the recipe thumbnail queue (PLAN-0030; a job kind of PLAN-0039 Slice 3):
/// one image per run, so a dish being prepared for a menu never waits behind a row of images. With work
/// left it runs again at once; otherwise after <c>Ai:Images:PollSeconds</c>. The images keep their own
/// table, with its states and leases, because people see those states and reuse depends on them.
/// </summary>
public sealed class RecipeThumbnailJob(RecipeThumbnailService thumbnails, Modules.Ai.Images.RecipeThumbnailOptions options) : IScheduledJob
{
    /// <inheritdoc />
    public string Kind => "dish_images";

    /// <inheritdoc />
    public DateTimeOffset FirstRunAt(DateTimeOffset now) => now + TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public DateTimeOffset NextRunAt(DateTimeOffset now) => now + TimeSpan.FromSeconds(options.PollSeconds);

    /// <inheritdoc />
    public async Task<JobResult> HandleAsync(JobContext job, CancellationToken cancellationToken) =>
        await thumbnails.ProcessNextAsync(cancellationToken)
            ? JobResult.Repeat(DateTimeOffset.UtcNow)
            : JobResult.Succeeded;
}
