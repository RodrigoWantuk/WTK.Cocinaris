using Cocinaris.Api.Services;
using Cocinaris.Api.Observability;
using Microsoft.AspNetCore.Antiforgery;

namespace Cocinaris.Api.Profiles;

/// <summary>Maps profile HTTP routes to the profile application-service boundary.</summary>
public static class ProfileEndpoints
{
    /// <summary>Maps authenticated profile routes.</summary>
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("", (ProfileApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(context, cancellationToken))
            .Produces<ProfileResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403);
        group.MapPut("", (ProfileApplicationService service, ProfileMutationRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PutAsync(request, requestContext, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .Produces<ProfileResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(412)
            .ProducesProblem(428);
        group.MapPatch("", (ProfileApplicationService service, ProfileMutationRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PatchAsync(request, requestContext, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .Produces<ProfileResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(412)
            .ProducesProblem(428);
        group.MapGet("/preferences", (ProfileApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.GetPreferencesAsync(context, cancellationToken))
            .Produces<PreferencesCollectionResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403);
        group.MapPut("/preferences", (ProfileApplicationService service, PreferencesRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PutPreferencesAsync(request, requestContext, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<PreferencesCollectionResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(412)
            .ProducesProblem(428);
        group.MapGet("/equipment", (ProfileApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.GetEquipmentAsync(context, cancellationToken))
            .Produces<EquipmentCollectionResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403);
        group.MapPut("/equipment", (ProfileApplicationService service, EquipmentRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PutEquipmentAsync(request, requestContext, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<EquipmentCollectionResponse>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(412)
            .ProducesProblem(428);
        group.MapGet("/completeness", (ProfileApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.GetCompletenessAsync(context, cancellationToken))
            .Produces<ProfileCompletenessResponse>()
            .ProducesProblem(401)
            .ProducesProblem(403);
        return group;
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (HttpMethods.IsPut(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method))
        {
            try
            {
                await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                context.HttpContext.RequestServices.GetRequiredService<SecurityMetrics>().RecordFailure("csrf");
                return ApiProblem.Create(context.HttpContext, StatusCodes.Status400BadRequest, "validation_failed", "The CSRF token is missing or invalid.");
            }
        }

        return await next(context);
    }
}
