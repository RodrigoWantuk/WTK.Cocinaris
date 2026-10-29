using Microsoft.AspNetCore.Antiforgery;
using Cocinaris.Api.Services;
using Cocinaris.Api.Observability;

namespace Cocinaris.Api.Inventory;

/// <summary>Maps inventory HTTP routes to the inventory application-service boundary.</summary>
public static class InventoryEndpoints
{
    /// <summary>Maps the authenticated inventory routes without embedding persistence or domain orchestration in endpoint handlers.</summary>
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.AddEndpointFilter(async (context, next) =>
        {
            await context.HttpContext.RequestServices.GetRequiredService<InventoryCalendar>().ResolveAsync(context.HttpContext.RequestAborted);
            return await next(context);
        });
        group.MapGet("/attention", (InventoryApplicationService service, HttpContext context, CancellationToken cancellationToken) => service.AttentionAsync(context, cancellationToken)).Produces<AttentionListResponse>();
        group.MapGet("/lots", (InventoryApplicationService service, int? pageSize, string? status, string? storageLocation, string? search, string? cursor, HttpContext context, CancellationToken cancellationToken) => service.ListAsync(pageSize, status, storageLocation, search, cursor, context, cancellationToken)).Produces<ListLotsResponse>().ProducesProblem(400);
        group.MapPost("/lots", (InventoryApplicationService service, CreateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.CreateAsync(request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(409).ProducesProblem(422);
        group.MapGet("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(lotId, context, cancellationToken)).Produces<LotResponse>().ProducesProblem(404);
        group.MapPatch("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, UpdateLotRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.UpdateAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(404).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapPost("/lots/{lotId:guid}/adjustments", (InventoryApplicationService service, Guid lotId, AdjustmentRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.AdjustAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapPost("/lots/{lotId:guid}/transitions", (InventoryApplicationService service, Guid lotId, TransitionRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.TransitionAsync(lotId, request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<LotResponse>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapDelete("/lots/{lotId:guid}", (InventoryApplicationService service, Guid lotId, HttpRequest requestContext, CancellationToken cancellationToken) => service.DeleteAsync(lotId, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces(StatusCodes.Status204NoContent).ProducesProblem(404).ProducesProblem(412).ProducesProblem(428);
        group.MapGet("/lots/{lotId:guid}/history", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.HistoryAsync(lotId, context, cancellationToken)).Produces<IReadOnlyList<LotHistoryResponse>>().ProducesProblem(404);
        group.MapPost("/preparations", (InventoryApplicationService service, PrepareComponentsRequest request, HttpRequest requestContext, CancellationToken cancellationToken) => service.PrepareAsync(request, requestContext, cancellationToken)).RequireRateLimiting("mutation").Produces<PreparationResponse>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409).ProducesProblem(412).ProducesProblem(422).ProducesProblem(428);
        group.MapGet("/preparations/{batchId:guid}", (InventoryApplicationService service, Guid batchId, HttpContext context, CancellationToken cancellationToken) => service.GetPreparationAsync(batchId, context, cancellationToken)).Produces<PreparationResponse>().ProducesProblem(404);
        group.MapGet("/lots/{lotId:guid}/provenance", (InventoryApplicationService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.ProvenanceAsync(lotId, context, cancellationToken)).Produces<LotProvenanceResponse>().ProducesProblem(404);
        return group;
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (HttpMethods.IsPost(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method) || HttpMethods.IsDelete(context.HttpContext.Request.Method))
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
