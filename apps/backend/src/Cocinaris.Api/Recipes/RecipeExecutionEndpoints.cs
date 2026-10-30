using System.Diagnostics;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;

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
        group.MapGet("/{recipeId:guid}/availability", (RecipeExecutionApiService service, Guid recipeId, HttpContext context, CancellationToken cancellationToken) => service.GetAvailabilityAsync(recipeId, context, cancellationToken))
            .Produces<IReadOnlyList<RecipeAvailabilityLineResponse>>()
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
        group.MapPut("/executions/{executionId:guid}/adjustments", (RecipeExecutionApiService service, Guid executionId, RecipeExecutionAdjustmentsRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetAdjustmentsAsync(executionId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeExecutionResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
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
        ToResult(await application.StartAsync(new StartRecipeExecutionCommand(recipeId, body?.Servings, body?.MenuEntryId), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Gets one execution with its completion proposal.</summary>
    public async Task<IResult> GetAsync(Guid executionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(executionId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>What the pantry covers of a recipe at its own servings.</summary>
    public async Task<IResult> GetAvailabilityAsync(Guid recipeId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAvailabilityAsync(recipeId, cancellationToken), context.TraceIdentifier, lines => lines!.Select(line => new RecipeAvailabilityLineResponse(line.IngredientId, line.Status, line.Unit, line.MissingQuantity)).ToList());

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

    /// <summary>Replaces the session's ingredient adjustments (PLAN-0042 Phase 2).</summary>
    public async Task<IResult> SetAdjustmentsAsync(Guid executionId, RecipeExecutionAdjustmentsRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetAdjustmentsAsync(executionId, (body.Adjustments ?? []).Select(item => new RecipeExecutionAdjustment(item.IngredientId ?? string.Empty, item.Kind ?? string.Empty, item.SubstituteName, item.Quantity)).ToList(), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Abandons an execution without touching inventory.</summary>
    public async Task<IResult> AbandonAsync(Guid executionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AbandonAsync(executionId, cancellationToken), context.TraceIdentifier, ToResponse);

    private static RecipeExecutionResponse ToResponse(RecipeExecutionView view) => new(
        view.ExecutionId, view.RecipeId, view.RevisionNumber, view.RecipeName, view.Servings, view.RecipeServings, view.Status, view.StartedAt, view.FinishedAt, view.InventoryBatchId, view.Rating, view.Notes, view.NormalizedRecipeJson,
        view.Proposal is { } proposal
            ? new RecipeExecutionProposalResponse(
                proposal.Lines.Select(line => new RecipeExecutionProposalLineResponse(line.IngredientId, line.DisplayName, line.Optional, line.RequiredQuantity, line.Unit, line.Status, line.LotId, line.ProposedQuantity, line.ProtectedQuantity, (line.ReservedElsewhere ?? []).Select(item => new RecipeExecutionReservationResponse(item.MenuEntryId, item.RecipeName, item.PlannedDate, item.Quantity)).ToList(), line.Adjustment, line.OriginalName)).ToList(),
                proposal.PantryLots.Select(lot => new RecipeExecutionPantryLotResponse(lot.LotId, lot.ProductName, lot.Quantity, lot.Unit, lot.StorageLocation, lot.Version, lot.ProtectedQuantity, lot.ReservedQuantity, (lot.ReservedFor ?? []).Select(item => new RecipeExecutionReservationResponse(item.MenuEntryId, item.RecipeName, item.PlannedDate, item.Quantity)).ToList())).ToList())
            : null,
        (view.Adjustments ?? []).Select(item => new RecipeExecutionAdjustmentResponse(item.IngredientId, item.Kind, item.SubstituteName, item.Quantity)).ToList());

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
/// <param name="MenuEntryId">Menu entry the session starts from (PLAN-0029); the entry then shows as cooking, and as cooked once completed.</param>
public sealed record StartRecipeExecutionRequest(int? Servings, Guid? MenuEntryId = null);

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

/// <summary>One proposal line; <c>status</c> is <c>ready</c>, <c>insufficient</c>, <c>lot_unavailable</c>, <c>not_tracked</c>, or <c>skipped</c> (left out this time). <c>Adjustment</c> is the session's change to it (<c>substitute</c>, <c>quantity</c>, <c>skip</c>), and <c>OriginalName</c> the recipe's name when substituted.</summary>
/// <remarks>
/// <c>Status</c> adds <c>protected</c> (PLAN-0029 Phase 3). <c>ProtectedQuantity</c> is never proposed;
/// <c>ReservedElsewhere</c> lists other menu meals counting on the same lot.
/// </remarks>
public sealed record RecipeExecutionProposalLineResponse(string IngredientId, string DisplayName, bool Optional, decimal RequiredQuantity, string Unit, string Status, Guid? LotId, decimal? ProposedQuantity, decimal ProtectedQuantity, IReadOnlyList<RecipeExecutionReservationResponse> ReservedElsewhere, string? Adjustment, string? OriginalName);

/// <summary>Quantity of a lot another menu meal counts on.</summary>
public sealed record RecipeExecutionReservationResponse(Guid MenuEntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity);

/// <summary>An active measured pantry lot the user can pick.</summary>
/// <remarks><c>ProtectedQuantity</c> is locked by the owner; <c>ReservedQuantity</c> is reserved for other menu meals.</remarks>
public sealed record RecipeExecutionPantryLotResponse(Guid LotId, string ProductName, decimal Quantity, string Unit, string StorageLocation, Guid Version, decimal ProtectedQuantity, decimal ReservedQuantity, IReadOnlyList<RecipeExecutionReservationResponse> ReservedFor);

/// <summary>Deterministic, editable completion proposal.</summary>
public sealed record RecipeExecutionProposalResponse(IReadOnlyList<RecipeExecutionProposalLineResponse> Lines, IReadOnlyList<RecipeExecutionPantryLotResponse> PantryLots);

/// <summary>One recipe execution. <c>status</c> is <c>InProgress</c>, <c>Completed</c>, or <c>Abandoned</c>; <c>proposal</c> is present while in progress; <c>adjustments</c> are the session's ingredient changes (PLAN-0042 Phase 2).</summary>
public sealed record RecipeExecutionResponse(Guid ExecutionId, Guid RecipeId, int RevisionNumber, string RecipeName, int Servings, int RecipeServings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, Guid? InventoryBatchId, int? Rating, string? Notes, string NormalizedRecipeJson, RecipeExecutionProposalResponse? Proposal, IReadOnlyList<RecipeExecutionAdjustmentResponse> Adjustments);

/// <summary>Whether the pantry covers one recipe ingredient at the recipe's servings (advisory).</summary>
/// <param name="IngredientId">Ingredient identifier in the revision.</param>
/// <param name="Status"><c>available</c>, <c>partial</c>, or <c>missing</c>.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="MissingQuantity">Quantity the pantry lacks, in <paramref name="Unit"/>; zero when available.</param>
public sealed record RecipeAvailabilityLineResponse(string IngredientId, string Status, string Unit, decimal MissingQuantity);

/// <summary>Compact in-progress execution.</summary>
public sealed record RecipeExecutionSummaryResponse(Guid ExecutionId, Guid RecipeId, string RecipeName, int Servings, DateTimeOffset StartedAt);

/// <summary>One ingredient change made while cooking (PLAN-0042 Phase 2).</summary>
/// <param name="IngredientId">Recipe ingredient.</param>
/// <param name="Kind"><c>substitute</c>, <c>quantity</c>, or <c>skip</c>.</param>
/// <param name="SubstituteName">What was used instead, for <c>substitute</c>.</param>
/// <param name="Quantity">Amount used this time, in the proposal line's unit.</param>
public sealed record RecipeExecutionAdjustmentResponse(string IngredientId, string Kind, string? SubstituteName, decimal? Quantity);

/// <summary>Request body replacing a session's ingredient adjustments; an empty list clears them.</summary>
/// <param name="Adjustments">At most 40, one per ingredient.</param>
public sealed record RecipeExecutionAdjustmentsRequest(IReadOnlyList<RecipeExecutionAdjustmentRequestItem>? Adjustments);

/// <summary>One requested ingredient adjustment.</summary>
/// <param name="IngredientId">Recipe ingredient.</param>
/// <param name="Kind"><c>substitute</c>, <c>quantity</c>, or <c>skip</c>.</param>
/// <param name="SubstituteName">What was used instead (1–120 characters), for <c>substitute</c>.</param>
/// <param name="Quantity">Amount used this time (required for <c>quantity</c>, optional for <c>substitute</c>).</param>
public sealed record RecipeExecutionAdjustmentRequestItem(string? IngredientId, string? Kind, string? SubstituteName, decimal? Quantity);

