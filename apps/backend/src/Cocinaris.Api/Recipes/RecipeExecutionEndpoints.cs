using System.Diagnostics;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Api.Recipes;

/// <summary>Maps recipe execution routes (PLAN-0042) on the authenticated, CSRF-protected recipe group.</summary>
public static class RecipeExecutionEndpoints
{
    /// <summary>Maps start, read, list, completion, and abandonment of recipe executions.</summary>
    public static RouteGroupBuilder MapRecipeExecutionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{recipeId:guid}/executions", (RecipeExecutionApiService service, Guid recipeId, StartRecipeExecutionRequest? body, HttpContext context, CancellationToken cancellationToken) => service.StartAsync(recipeId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeExecutionResponse>(StatusCodes.Status201Created)
            .Produces<RecipeExecutionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(404);
        group.MapGet("/executions/in-progress", (RecipeExecutionApiService service, HttpContext context, CancellationToken cancellationToken) => service.ListInProgressAsync(context, cancellationToken))
            .Produces<IReadOnlyList<RecipeExecutionSummaryResponse>>();
        group.MapGet("/executions/{executionId:guid}", (RecipeExecutionApiService service, Guid executionId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(executionId, context, cancellationToken))
            .Produces<RecipeExecutionResponse>()
            .ProducesProblem(404);
        group.MapPost("/executions/{executionId:guid}/completion", (RecipeExecutionApiService service, Guid executionId, CompleteRecipeExecutionRequest body, HttpRequest request, CancellationToken cancellationToken) => service.CompleteAsync(executionId, body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeExecutionResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(412)
            .ProducesProblem(422);
        group.MapPost("/executions/{executionId:guid}/abandonment", (RecipeExecutionApiService service, Guid executionId, HttpContext context, CancellationToken cancellationToken) => service.AbandonAsync(executionId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeExecutionResponse>()
            .ProducesProblem(404)
            .ProducesProblem(412)
            .ProducesProblem(422);
        return group;
    }
}

/// <summary>HTTP adapter for the recipe execution application service.</summary>
public sealed class RecipeExecutionApiService(RecipeExecutionApplicationService application)
{
    /// <summary>Starts cooking a recipe, or returns its in-progress execution.</summary>
    public async Task<IResult> StartAsync(Guid recipeId, StartRecipeExecutionRequest? body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.StartAsync(new StartRecipeExecutionCommand(recipeId, body?.Servings), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Gets one execution with its completion proposal.</summary>
    public async Task<IResult> GetAsync(Guid executionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(executionId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Lists in-progress executions.</summary>
    public async Task<IResult> ListInProgressAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ListInProgressAsync(cancellationToken), context.TraceIdentifier, items => items!.Select(item => new RecipeExecutionSummaryResponse(item.ExecutionId, item.RecipeId, item.RecipeName, item.Servings, item.StartedAt)).ToList());

    /// <summary>Completes an execution and reconciles inventory; requires an Idempotency-Key.</summary>
    public async Task<IResult> CompleteAsync(Guid executionId, CompleteRecipeExecutionRequest body, HttpRequest request, CancellationToken cancellationToken)
    {
        Guid? key = request.Headers.TryGetValue("Idempotency-Key", out var values) && Guid.TryParse(values.ToString(), out var parsed) ? parsed : null;
        var command = new CompleteRecipeExecutionCommand(
            executionId,
            body.Consumptions?.Select(item => new ExecutionConsumptionCommand(item.LotId, item.Quantity, item.Unit, item.Version)).ToList(),
            body.Leftover is { } leftover ? new ExecutionLeftoverCommand(leftover.Portions, leftover.StorageLocation, leftover.UseBy) : null,
            body.Rating,
            body.Notes,
            key,
            request.HttpContext.TraceIdentifier);
        return ToResult(await application.CompleteAsync(command, cancellationToken), request.HttpContext.TraceIdentifier, ToResponse);
    }

    /// <summary>Abandons an execution without touching inventory.</summary>
    public async Task<IResult> AbandonAsync(Guid executionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AbandonAsync(executionId, cancellationToken), context.TraceIdentifier, ToResponse);

    private static RecipeExecutionResponse ToResponse(RecipeExecutionView view) => new(
        view.ExecutionId, view.RecipeId, view.RevisionNumber, view.RecipeName, view.Servings, view.RecipeServings, view.Status, view.StartedAt, view.FinishedAt, view.InventoryBatchId, view.Rating, view.Notes, view.NormalizedRecipeJson,
        view.Proposal is { } proposal
            ? new RecipeExecutionProposalResponse(
                proposal.Lines.Select(line => new RecipeExecutionProposalLineResponse(line.IngredientId, line.DisplayName, line.Optional, line.RequiredQuantity, line.Unit, line.Status, line.LotId, line.ProposedQuantity)).ToList(),
                proposal.PantryLots.Select(lot => new RecipeExecutionPantryLotResponse(lot.LotId, lot.ProductName, lot.Quantity, lot.Unit, lot.StorageLocation, lot.Version)).ToList())
            : null);

    private static IResult ToResult<TApp, THttp>(RecipeApplicationResult<TApp> result, string traceId, Func<TApp, THttp> map)
    {
        if (result.Problem is { } problem)
        {
            var extensions = new Dictionary<string, object?> { ["errorCode"] = problem.ErrorCode, ["traceId"] = Activity.Current?.Id ?? traceId };
            if (problem.Errors is not null)
            {
                extensions["errors"] = problem.Errors;
            }

            return Results.Problem(detail: problem.Detail, statusCode: StatusFor(problem.ErrorCode), extensions: extensions);
        }

        return Results.Json(map(result.Value!), statusCode: result.Success == RecipeApplicationSuccess.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static int StatusFor(string errorCode) => errorCode switch
    {
        "validation_failed" => StatusCodes.Status400BadRequest,
        "resource_not_found" => StatusCodes.Status404NotFound,
        "precondition_required" => StatusCodes.Status428PreconditionRequired,
        "precondition_failed" => StatusCodes.Status412PreconditionFailed,
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict
    };
}

/// <summary>Optional body for starting an execution.</summary>
/// <param name="Servings">Portions to cook, 1 to 24; the recipe's servings when omitted.</param>
public sealed record StartRecipeExecutionRequest(int? Servings);

/// <summary>One confirmed lot consumption in the lot's canonical unit (<c>Gram</c>, <c>Milliliter</c>, <c>Unit</c>).</summary>
/// <param name="LotId">Pantry lot.</param>
/// <param name="Quantity">Quantity used, positive.</param>
/// <param name="Unit">The lot's canonical unit.</param>
/// <param name="Version">The lot version from the proposal; a changed lot fails with 412.</param>
public sealed record RecipeExecutionConsumptionRequest(Guid LotId, decimal Quantity, string Unit, Guid Version);

/// <summary>Leftover portions to store.</summary>
/// <param name="Portions">Portions stored, 1 to the portions cooked.</param>
/// <param name="StorageLocation"><c>Pantry</c>, <c>Refrigerator</c>, or <c>Freezer</c>.</param>
/// <param name="UseBy">Optional user-entered use-by date.</param>
public sealed record RecipeExecutionLeftoverRequest(int Portions, string StorageLocation, DateOnly? UseBy);

/// <summary>Completion body. Every field is the user's confirmed choice; none is inferred server-side.</summary>
public sealed record CompleteRecipeExecutionRequest(IReadOnlyList<RecipeExecutionConsumptionRequest>? Consumptions, RecipeExecutionLeftoverRequest? Leftover, int? Rating, string? Notes);

/// <summary>One proposal line; <c>status</c> is <c>ready</c>, <c>insufficient</c>, <c>lot_unavailable</c>, or <c>not_tracked</c>.</summary>
public sealed record RecipeExecutionProposalLineResponse(string IngredientId, string DisplayName, bool Optional, decimal RequiredQuantity, string Unit, string Status, Guid? LotId, decimal? ProposedQuantity);

/// <summary>An active measured pantry lot the user can pick.</summary>
public sealed record RecipeExecutionPantryLotResponse(Guid LotId, string ProductName, decimal Quantity, string Unit, string StorageLocation, Guid Version);

/// <summary>Deterministic, editable completion proposal.</summary>
public sealed record RecipeExecutionProposalResponse(IReadOnlyList<RecipeExecutionProposalLineResponse> Lines, IReadOnlyList<RecipeExecutionPantryLotResponse> PantryLots);

/// <summary>One recipe execution. <c>status</c> is <c>InProgress</c>, <c>Completed</c>, or <c>Abandoned</c>; <c>proposal</c> is present while in progress.</summary>
public sealed record RecipeExecutionResponse(Guid ExecutionId, Guid RecipeId, int RevisionNumber, string RecipeName, int Servings, int RecipeServings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, Guid? InventoryBatchId, int? Rating, string? Notes, string NormalizedRecipeJson, RecipeExecutionProposalResponse? Proposal);

/// <summary>Compact in-progress execution.</summary>
public sealed record RecipeExecutionSummaryResponse(Guid ExecutionId, Guid RecipeId, string RecipeName, int Servings, DateTimeOffset StartedAt);
