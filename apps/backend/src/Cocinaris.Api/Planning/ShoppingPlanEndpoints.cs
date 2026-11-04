using System.Diagnostics;
using Cocinaris.Api.Observability;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Cocinaris.Api.Planning;

/// <summary>Maps the authenticated shopping plan routes (PLAN-0062). Mutations are CSRF-protected and rate limited.</summary>
public static class ShoppingPlanEndpoints
{
    /// <summary>Maps the setup defaults, the plan, its cards, review, acceptance, and discard.</summary>
    public static RouteGroupBuilder MapShoppingPlanEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("/start", (ShoppingPlanApiService service, HttpContext context, CancellationToken cancellationToken) => service.GetStartAsync(context, cancellationToken))
            .Produces<ShoppingPlanStartView>();
        group.MapPost("/", (ShoppingPlanApiService service, CreateShoppingPlanRequest body, HttpRequest request, CancellationToken cancellationToken) => service.CreateAsync(body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>(StatusCodes.Status201Created)
            .Produces<ShoppingPlanView>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(422);
        group.MapGet("/{sessionId:guid}", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(sessionId, context, cancellationToken))
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404);
        group.MapPost("/{sessionId:guid}/batches", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanBatchRequest? body, HttpRequest request, CancellationToken cancellationToken) => service.AddBatchAsync(sessionId, body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapPost("/{sessionId:guid}/cards/{cardId:guid}/decision", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanDecisionRequest body, HttpContext context, CancellationToken cancellationToken) => service.DecideAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/cards/{cardId:guid}/reason", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanReasonRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetReasonAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/undo", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.UndoAsync(sessionId, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/cards/{cardId:guid}/expansion", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, HttpRequest request, CancellationToken cancellationToken) => service.ExpandAsync(sessionId, cardId, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(503);
        group.MapPost("/{sessionId:guid}/cards/{cardId:guid}/removal", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, HttpContext context, CancellationToken cancellationToken) => service.RemoveCardAsync(sessionId, cardId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/review", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.ReviewAsync(sessionId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/replacements", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanReplacementRequest body, HttpContext context, CancellationToken cancellationToken) => service.ReplaceAsync(sessionId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/acceptance", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.AcceptAsync(sessionId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/abandon", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.AbandonAsync(sessionId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        return group;
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method))
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

/// <summary>HTTP adapter for shopping plans.</summary>
public sealed class ShoppingPlanApiService(ShoppingPlanApplicationService application)
{
    /// <summary>Gets the setup screen's defaults.</summary>
    public async Task<IResult> GetStartAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetStartAsync(cancellationToken), context.TraceIdentifier);

    /// <summary>Creates a plan; requires an Idempotency-Key.</summary>
    public async Task<IResult> CreateAsync(CreateShoppingPlanRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.CreateAsync(new CreateShoppingPlanCommand(Key(request), request.HttpContext.TraceIdentifier, Language(request), body.Today, body.Days, body.MealTypes, body.MealsOut, body.CookingStyle, body.Priority, body.PrepDate, body.PrepMinutes, body.AnchorIngredient, body.People), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Gets one plan.</summary>
    public async Task<IResult> GetAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Adds up to six AI cards; requires an Idempotency-Key.</summary>
    public async Task<IResult> AddBatchAsync(Guid sessionId, ShoppingPlanBatchRequest? body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.AddBatchAsync(sessionId, Key(request), body?.DishFamilies, body?.Cuisine, body?.Time, request.HttpContext.TraceIdentifier, Language(request), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Accepts or rejects a card.</summary>
    public async Task<IResult> DecideAsync(Guid sessionId, Guid cardId, ShoppingPlanDecisionRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.DecideAsync(sessionId, cardId, body.Decision, body.Mode, body.Repeats, cancellationToken), context.TraceIdentifier);

    /// <summary>Records why a card was refused.</summary>
    public async Task<IResult> SetReasonAsync(Guid sessionId, Guid cardId, ShoppingPlanReasonRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetReasonAsync(sessionId, cardId, body.Reason, cancellationToken), context.TraceIdentifier);

    /// <summary>Undoes the last decision.</summary>
    public async Task<IResult> UndoAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.UndoAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Expands an accepted card and creates its meals.</summary>
    public async Task<IResult> ExpandAsync(Guid sessionId, Guid cardId, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.ExpandAsync(sessionId, cardId, request.HttpContext.TraceIdentifier, Language(request), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Removes an accepted card.</summary>
    public async Task<IResult> RemoveCardAsync(Guid sessionId, Guid cardId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.RemoveCardAsync(sessionId, cardId, cancellationToken), context.TraceIdentifier);

    /// <summary>Moves to review and computes the order.</summary>
    public async Task<IResult> ReviewAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ReviewAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Removes one meal so another can be chosen.</summary>
    public async Task<IResult> ReplaceAsync(Guid sessionId, ShoppingPlanReplacementRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ReplaceAsync(sessionId, body.EntryId, cancellationToken), context.TraceIdentifier);

    /// <summary>Accepts the menu.</summary>
    public async Task<IResult> AcceptAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AcceptAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Discards the plan.</summary>
    public async Task<IResult> AbandonAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AbandonAsync(sessionId, cancellationToken), context.TraceIdentifier);

    private static Guid? Key(HttpRequest request) =>
        request.Headers.TryGetValue("Idempotency-Key", out var values) && Guid.TryParse(values.ToString(), out var parsed) ? parsed : null;

    // Only supported tags pass, so a header can never inject free text into a prompt.
    private static string? Language(HttpRequest request) =>
        request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(item => item.Quality ?? 1)
            .Select(item => item.Value.Value)
            .FirstOrDefault(tag => tag is "pt-BR" or "en" or "es");

    private static IResult ToResult<T>(RecipeApplicationResult<T> result, string traceId)
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

        return Results.Json(result.Value, statusCode: result.Success == RecipeApplicationSuccess.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static int StatusFor(string errorCode) => errorCode switch
    {
        "validation_failed" => StatusCodes.Status400BadRequest,
        "resource_not_found" => StatusCodes.Status404NotFound,
        "domain_rule_violated" or "plan_in_progress" or "expansions_pending" or "ai_output_invalid" => StatusCodes.Status422UnprocessableEntity,
        "ai_capability_unavailable" or "ai_budget_exhausted" or "ai_budget_unavailable" or "ai_provider_timeout" or "ai_provider_unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict
    };
}

/// <summary>Body for creating a plan.</summary>
/// <param name="Today">The owner's local day.</param>
/// <param name="Days">Days covered, 1 to 14.</param>
/// <param name="MealTypes">Meals to plan.</param>
/// <param name="MealsOut">Meals eaten out.</param>
/// <param name="CookingStyle"><c>once</c>, <c>mix</c>, or <c>daily</c>.</param>
/// <param name="Priority"><c>waste</c>, <c>short</c>, <c>save</c>, or <c>time</c>.</param>
/// <param name="PrepDate">Session day, up to seven days ahead; ignored for <c>daily</c>.</param>
/// <param name="PrepMinutes">Session length: 60, 120, or 240; ignored for <c>daily</c>.</param>
/// <param name="AnchorIngredient">Optional typed ingredient, at most 60 characters.</param>
/// <param name="People">People for this plan, 1 to 12; the profile's default when null. It never updates the profile.</param>
public sealed record CreateShoppingPlanRequest(DateOnly? Today, int Days, IReadOnlyList<string>? MealTypes, int MealsOut, string? CookingStyle, string? Priority, DateOnly? PrepDate, int? PrepMinutes, string? AnchorIngredient, int? People);

/// <summary>Body for asking for more cards.</summary>
/// <param name="DishFamilies">Up to three family codes.</param>
/// <param name="Cuisine">A cuisine code.</param>
/// <param name="Time"><c>quick</c> or <c>medium</c>.</param>
public sealed record ShoppingPlanBatchRequest(IReadOnlyList<string>? DishFamilies, string? Cuisine, string? Time);

/// <summary>Body for deciding a card.</summary>
/// <param name="Decision"><c>accept</c> or <c>reject</c>.</param>
/// <param name="Mode"><c>yield</c> (one cooking covers the meals) or <c>again</c> (cook again another day); required when repeats exceed one.</param>
/// <param name="Repeats">Meals to fill, 1 to 4.</param>
public sealed record ShoppingPlanDecisionRequest(string? Decision, string? Mode, int? Repeats);

/// <summary>Body for the reason a card was refused.</summary>
/// <param name="Reason"><c>dislike</c>, <c>too_much_work</c>, or <c>tired_of</c>.</param>
public sealed record ShoppingPlanReasonRequest(string? Reason);

/// <summary>Body for replacing a meal.</summary>
/// <param name="EntryId">Menu entry to remove.</param>
public sealed record ShoppingPlanReplacementRequest(Guid? EntryId);
