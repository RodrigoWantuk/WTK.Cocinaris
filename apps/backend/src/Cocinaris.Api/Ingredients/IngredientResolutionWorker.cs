using Cocinaris.Modules.Ingredients;

namespace Cocinaris.Api.Ingredients;

/// <summary>
/// Background job that syncs the ingredient catalog and drains the name resolution queue
/// (PLAN-0055), one batch per scope. It keeps going while there is work and polls otherwise. A
/// failed step is logged by exception type only, never with names or vectors, and the loop
/// continues; the claim lease makes a crashed step retry later.
/// </summary>
public sealed class IngredientResolutionWorker(IServiceScopeFactory scopes, IngredientResolutionOptions options, ILogger<IngredientResolutionWorker> logger) : BackgroundService
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
                worked = await scope.ServiceProvider.GetRequiredService<IngredientResolutionService>().ProcessNextBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Ingredient resolution step failed: {ExceptionType}", exception.GetType().Name);
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
