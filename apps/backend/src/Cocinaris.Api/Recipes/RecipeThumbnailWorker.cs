using Cocinaris.Modules.Recipes.Thumbnails;

namespace Cocinaris.Api.Recipes;

/// <summary>
/// Background job that drains the recipe thumbnail queue (PLAN-0030): one image at a time, each
/// in its own scope. It keeps going while there is work and polls otherwise. A failure in one
/// step is logged without image data or prompts, and the loop continues; the asset's lease makes
/// a crashed step retry later.
/// </summary>
public sealed class RecipeThumbnailWorker(IServiceScopeFactory scopes, Modules.Ai.Images.RecipeThumbnailOptions options, ILogger<RecipeThumbnailWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                worked = await scope.ServiceProvider.GetRequiredService<RecipeThumbnailService>().ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Recipe thumbnail step failed: {ExceptionType}", exception.GetType().Name);
            }

            if (!worked)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
