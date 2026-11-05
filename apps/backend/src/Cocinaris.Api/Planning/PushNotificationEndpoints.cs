using System.Diagnostics;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Api.Planning;

/// <summary>Maps a person's choice about notifications (ADR-0013). Mutations are CSRF-protected and rate limited.</summary>
public static class PushNotificationEndpoints
{
    /// <summary>Maps the status, subscribing, and removing a subscription.</summary>
    public static RouteGroupBuilder MapPushNotificationEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ShoppingPlanEndpoints.ValidateCsrfAsync);
        group.MapGet("/", (PushSubscriptionService service) => Results.Json(service.GetStatus()))
            .Produces<PushStatusView>();
        group.MapPut("/subscription", async (PushSubscriptionService service, PushSubscriptionRequest body, CancellationToken cancellationToken) =>
                ToResult(await service.SubscribeAsync(body.Endpoint, body.P256dh, body.Auth, cancellationToken)))
            .RequireRateLimiting("mutation")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(400)
            .ProducesProblem(422);
        group.MapPost("/subscription/removal", async (PushSubscriptionService service, PushSubscriptionRemovalRequest body, CancellationToken cancellationToken) =>
                ToResult(await service.UnsubscribeAsync(body.Endpoint, cancellationToken)))
            .RequireRateLimiting("mutation")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(400);
        return group;
    }

    private static IResult ToResult(RecipeApplicationResult<bool> result)
    {
        if (result.Problem is not { } problem)
        {
            return Results.NoContent();
        }

        var extensions = new Dictionary<string, object?> { ["errorCode"] = problem.ErrorCode, ["traceId"] = Activity.Current?.Id };
        if (problem.Errors is not null)
        {
            extensions["errors"] = problem.Errors;
        }

        return Results.Problem(detail: problem.Detail, statusCode: problem.ErrorCode == "validation_failed" ? StatusCodes.Status400BadRequest : StatusCodes.Status422UnprocessableEntity, extensions: extensions);
    }
}

/// <summary>Body for saving this browser's subscription.</summary>
/// <param name="Endpoint">Push service address.</param>
/// <param name="P256dh">Browser's public key, base64url.</param>
/// <param name="Auth">Browser's secret, base64url.</param>
public sealed record PushSubscriptionRequest(string? Endpoint, string? P256dh, string? Auth);

/// <summary>Body for forgetting this browser's subscription.</summary>
/// <param name="Endpoint">Push service address.</param>
public sealed record PushSubscriptionRemovalRequest(string? Endpoint);
