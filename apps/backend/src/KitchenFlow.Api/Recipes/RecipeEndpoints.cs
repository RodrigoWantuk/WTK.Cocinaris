using System.Diagnostics;
using KitchenFlow.Api.Observability;
using KitchenFlow.Api.Services;
using KitchenFlow.Modules.Recipes.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace KitchenFlow.Api.Recipes;

/// <summary>Maps authenticated recipe cook-now HTTP routes to the Recipes application-service boundary.</summary>
public static class RecipeEndpoints
{
    /// <summary>Maps the authenticated recipe routes without embedding persistence or AI orchestration in endpoint handlers.</summary>
    public static RouteGroupBuilder MapRecipeEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/generation-sessions", (RecipeApiService service, HttpRequest request, CancellationToken cancellationToken) => service.RequestCandidatesAsync(request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeGenerationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapGet("/generation-sessions/{sessionId:guid}", (RecipeApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.GetSessionAsync(sessionId, context, cancellationToken))
            .Produces<RecipeGenerationSessionResponse>()
            .ProducesProblem(404);
        group.MapPost("/generation-sessions/{sessionId:guid}/selection", (RecipeApiService service, Guid sessionId, SelectCandidateRequest body, HttpRequest request, CancellationToken cancellationToken) => service.SelectCandidateAsync(sessionId, body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>(StatusCodes.Status201Created)
            .Produces<RecipeDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(412)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapGet("/", (RecipeApiService service, HttpContext context, CancellationToken cancellationToken) => service.ListRecipesAsync(context, cancellationToken))
            .Produces<IReadOnlyList<RecipeSummaryResponse>>()
            .ProducesProblem(401);
        group.MapGet("/{recipeId:guid}", (RecipeApiService service, Guid recipeId, HttpContext context, CancellationToken cancellationToken) => service.GetRecipeAsync(recipeId, context, cancellationToken))
            .Produces<RecipeDetailResponse>()
            .ProducesProblem(404);
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

/// <summary>HTTP transport adapter for cook-now recipe application commands and queries.</summary>
public sealed class RecipeApiService(RecipeCookNowApplicationService application)
{
    /// <summary>Creates a generation session and requests three validated cook-now candidates.</summary>
    public async Task<IResult> RequestCandidatesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var command = new RequestCandidatesCommand(ParseIdempotencyKey(request), request.HttpContext.TraceIdentifier);
        var result = await application.RequestCandidatesAsync(command, cancellationToken);
        return ToResult(result, request.HttpContext.TraceIdentifier, ToResponse);
    }

    /// <summary>Gets one owner-scoped generation session.</summary>
    public async Task<IResult> GetSessionAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await application.GetSessionAsync(sessionId, cancellationToken);
        return ToResult(result, context.TraceIdentifier, ToResponse);
    }

    /// <summary>Selects one candidate, expands it, and persists an immutable recipe revision.</summary>
    public async Task<IResult> SelectCandidateAsync(Guid sessionId, SelectCandidateRequest body, HttpRequest request, CancellationToken cancellationToken)
    {
        var command = new SelectCandidateCommand(sessionId, body.CandidateId, ParseIdempotencyKey(request), request.HttpContext.TraceIdentifier);
        var result = await application.SelectCandidateAsync(command, cancellationToken);
        return ToResult(result, request.HttpContext.TraceIdentifier, ToResponse);
    }

    /// <summary>Lists recipes owned by the current user.</summary>
    public async Task<IResult> ListRecipesAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var result = await application.ListRecipesAsync(cancellationToken);
        return ToResult(result, context.TraceIdentifier, value => value!.Select(ToResponse).ToList());
    }

    /// <summary>Gets one owned recipe detail.</summary>
    public async Task<IResult> GetRecipeAsync(Guid recipeId, HttpContext context, CancellationToken cancellationToken)
    {
        var result = await application.GetRecipeAsync(recipeId, cancellationToken);
        return ToResult(result, context.TraceIdentifier, ToResponse);
    }

    private static Guid? ParseIdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values))
        {
            return null;
        }

        return Guid.TryParse(values.ToString(), out var key) ? key : null;
    }

    private static IResult ToResult<TApp, THttp>(RecipeApplicationResult<TApp> result, string traceId, Func<TApp, THttp> map)
    {
        if (result.Problem is not null)
        {
            return Problem(result.Problem.ErrorCode, result.Problem.Detail, StatusFor(result.Problem.ErrorCode), traceId, result.Problem.Errors);
        }

        var status = result.Success == RecipeApplicationSuccess.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        return Results.Json(map(result.Value!), statusCode: status);
    }

    private static RecipeGenerationSessionResponse ToResponse(RecipeGenerationSessionView view) => new(
        view.SessionId,
        view.Status,
        view.Candidates?.Select(item => new RecipeCandidateResponse(
            item.CandidateId,
            item.CandidateStrategy,
            item.Name,
            item.TargetMealType,
            item.DishFormat,
            item.PrimaryTechnique,
            item.PrimaryIngredientRefs,
            item.Summary,
            item.Servings,
            item.ActiveMinutes,
            item.PassiveMinutes,
            item.TotalMinutes,
            item.Difficulty,
            item.RequiredEquipmentIds,
            item.RequiredCapabilities)).ToList(),
        view.FailureReason);

    private static RecipeDetailResponse ToResponse(RecipeDetailView view) => new(
        view.RecipeId,
        view.RevisionNumber,
        view.Name,
        view.MealTypes,
        view.Servings,
        view.NormalizedRecipeJson,
        view.ThumbnailVisualJson,
        view.CreatedAt);

    private static RecipeSummaryResponse ToResponse(RecipeSummaryView view) => new(view.RecipeId, view.Name, view.MealTypes, view.Servings, view.CreatedAt);

    private static int StatusFor(string errorCode) => errorCode switch
    {
        "validation_failed" => StatusCodes.Status400BadRequest,
        "resource_not_found" => StatusCodes.Status404NotFound,
        "precondition_required" => StatusCodes.Status428PreconditionRequired,
        "precondition_failed" => StatusCodes.Status412PreconditionFailed,
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        "ai_capability_unavailable" or "ai_budget_exhausted" or "ai_budget_unavailable" or "ai_provider_timeout" or "ai_provider_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "ai_output_invalid" => StatusCodes.Status422UnprocessableEntity,
        "ai_operation_conflict" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status409Conflict
    };

    private static IResult Problem(string errorCode, string detail, int statusCode, string traceId, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["errorCode"] = errorCode, ["traceId"] = Activity.Current?.Id ?? traceId };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(detail: detail, statusCode: statusCode, extensions: extensions);
    }
}

/// <summary>HTTP body for selecting one cook-now candidate.</summary>
/// <param name="CandidateId">Stable candidate identifier from the generation session.</param>
public sealed record SelectCandidateRequest(string CandidateId);

/// <summary>HTTP projection of one cook-now generation session.</summary>
public sealed record RecipeGenerationSessionResponse(Guid SessionId, string Status, IReadOnlyList<RecipeCandidateResponse>? Candidates, string? FailureReason);

/// <summary>HTTP projection of one cook-now candidate.</summary>
public sealed record RecipeCandidateResponse(
    string CandidateId,
    string CandidateStrategy,
    string Name,
    string TargetMealType,
    string DishFormat,
    string PrimaryTechnique,
    IReadOnlyList<string> PrimaryIngredientRefs,
    string Summary,
    int Servings,
    int ActiveMinutes,
    int PassiveMinutes,
    int TotalMinutes,
    string Difficulty,
    IReadOnlyList<string> RequiredEquipmentIds,
    IReadOnlyList<string> RequiredCapabilities);

/// <summary>HTTP projection of one owned recipe summary.</summary>
public sealed record RecipeSummaryResponse(Guid RecipeId, string Name, IReadOnlyList<string> MealTypes, int Servings, DateTimeOffset CreatedAt);

/// <summary>HTTP projection of one owned recipe detail.</summary>
public sealed record RecipeDetailResponse(
    Guid RecipeId,
    int RevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    DateTimeOffset CreatedAt);
