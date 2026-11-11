using System.Diagnostics;
using Cocinaris.Api.Observability;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Planning.Domain;
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
        group.MapPost("/{sessionId:guid}/recipes/{recipeId:guid}/choice", (ShoppingPlanApiService service, Guid sessionId, Guid recipeId, ShoppingPlanRecipeChoiceRequest body, HttpContext context, CancellationToken cancellationToken) => service.ChooseRecipeAsync(sessionId, recipeId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/preferences", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanPreferencesRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetPreferencesAsync(sessionId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/meal-counts", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanMealCountsRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetMealCountsAsync(sessionId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/cards/{cardId:guid}/day", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanCardDayRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetCardDayAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/cards/{cardId:guid}/yield", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanCardYieldRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetCardYieldAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/cards/{cardId:guid}/repeats", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanCardRepeatsRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetCardRepeatsAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/cards/{cardId:guid}/people", (ShoppingPlanApiService service, Guid sessionId, Guid cardId, ShoppingPlanCardPeopleRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetCardPeopleAsync(sessionId, cardId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/choices", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanChoicesRequest body, HttpContext context, CancellationToken cancellationToken) => service.ChooseManyAsync(sessionId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanChoicesView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{sessionId:guid}/distribution", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanDistributionRequest? body, HttpContext context, CancellationToken cancellationToken) => service.DistributeAsync(sessionId, body, context, cancellationToken))
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
        group.MapPost("/amendments", (ShoppingPlanApiService service, ShoppingPlanAmendmentRequest body, HttpRequest request, CancellationToken cancellationToken) => service.OpenAmendmentAsync(body, request, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapGet("/menu-link", (ShoppingPlanApiService service, Guid menuId, HttpContext context, CancellationToken cancellationToken) => service.GetMenuLinkAsync(menuId, context, cancellationToken))
            .Produces<ShoppingPlanMenuLinkView>();
        group.MapGet("/route-link", (ShoppingPlanApiService service, Guid? menuId, DateOnly? date, HttpContext context, CancellationToken cancellationToken) => service.FindRouteLinkAsync(menuId, date, context, cancellationToken))
            .Produces<ShoppingPlanRouteLinkView>()
            .ProducesProblem(400);
        group.MapGet("/{sessionId:guid}/route", (ShoppingPlanApiService service, Guid sessionId, HttpContext context, CancellationToken cancellationToken) => service.GetRouteAsync(sessionId, context, cancellationToken))
            .Produces<ShoppingPlanRouteView>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/{sessionId:guid}/route/progress", (ShoppingPlanApiService service, Guid sessionId, ShoppingPlanRouteProgress body, HttpContext context, CancellationToken cancellationToken) => service.SaveRouteProgressAsync(sessionId, body, context, cancellationToken))
            .RequireRateLimiting("shopping-plan-decision")
            .Produces<ShoppingPlanRouteView>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        return group;
    }

    internal static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
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
public sealed class ShoppingPlanApiService(ShoppingPlanApplicationService application, ShoppingPlanRouteService routes)
{
    /// <summary>Gets the setup screen's defaults.</summary>
    public async Task<IResult> GetStartAsync(HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetStartAsync(cancellationToken), context.TraceIdentifier);

    /// <summary>Creates a plan; requires an Idempotency-Key.</summary>
    public async Task<IResult> CreateAsync(CreateShoppingPlanRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.CreateAsync(new CreateShoppingPlanCommand(Key(request), request.HttpContext.TraceIdentifier, Language(request), body.Today, body.Days, body.MealTypes, body.MealsOut, body.CookingStyle, body.Priority, body.PrepDate, body.PrepMinutes, body.AnchorIngredient, body.People, body.FromMenuId, body.MealCounts?.Counts(), body.StartDate), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Gets one plan.</summary>
    public async Task<IResult> GetAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Chooses one of the person's saved recipes for a meal.</summary>
    public async Task<IResult> ChooseRecipeAsync(Guid sessionId, Guid recipeId, ShoppingPlanRecipeChoiceRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ChooseRecipeAsync(sessionId, recipeId, body.MealType, cancellationToken, body.PlannedDate), context.TraceIdentifier);

    /// <summary>Sets the switches above the options.</summary>
    public async Task<IResult> SetPreferencesAsync(Guid sessionId, ShoppingPlanPreferencesRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetPreferencesAsync(sessionId, body.UsePreferences, body.UsePantry, body.Goal, cancellationToken), context.TraceIdentifier);

    /// <summary>Changes how many of each meal the plan wants.</summary>
    public async Task<IResult> SetMealCountsAsync(Guid sessionId, ShoppingPlanMealCountsRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetMealCountsAsync(sessionId, body.Counts(), cancellationToken), context.TraceIdentifier);

    /// <summary>Sets or clears the day of a chosen dish.</summary>
    public async Task<IResult> SetCardDayAsync(Guid sessionId, Guid cardId, ShoppingPlanCardDayRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCardDayAsync(sessionId, cardId, body.PlannedDate, cancellationToken), context.TraceIdentifier);

    /// <summary>Sets how many meals one cooking of a chosen dish covers.</summary>
    public async Task<IResult> SetCardYieldAsync(Guid sessionId, Guid cardId, ShoppingPlanCardYieldRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCardYieldAsync(sessionId, cardId, body.Meals, cancellationToken), context.TraceIdentifier);

    /// <summary>Sets how a chosen dish repeats ("Repetir"): cooked once for more meals, or again each time.</summary>
    public async Task<IResult> SetCardRepeatsAsync(Guid sessionId, Guid cardId, ShoppingPlanCardRepeatsRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCardRepeatsAsync(sessionId, cardId, body.Mode, body.Meals, cancellationToken), context.TraceIdentifier);

    /// <summary>Sets how many people one chosen dish is for.</summary>
    public async Task<IResult> SetCardPeopleAsync(Guid sessionId, Guid cardId, ShoppingPlanCardPeopleRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCardPeopleAsync(sessionId, cardId, body.People, cancellationToken), context.TraceIdentifier);

    /// <summary>Chooses several offered dishes at once ("Completar").</summary>
    public async Task<IResult> ChooseManyAsync(Guid sessionId, ShoppingPlanChoicesRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ChooseManyAsync(sessionId, body.Choices?.Select(choice => new ShoppingPlanChoice(choice.CardId, choice.MealType)).ToList(), cancellationToken), context.TraceIdentifier);

    /// <summary>Spreads undated dishes across the period, or removes every day.</summary>
    public async Task<IResult> DistributeAsync(Guid sessionId, ShoppingPlanDistributionRequest? body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.DistributeAsync(sessionId, body?.Clear ?? false, cancellationToken), context.TraceIdentifier);

    /// <summary>Adds up to six AI cards; requires an Idempotency-Key.</summary>
    public async Task<IResult> AddBatchAsync(Guid sessionId, ShoppingPlanBatchRequest? body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.AddBatchAsync(sessionId, Key(request), body?.MealType is { } mealType ? new ShoppingPlanBatchAsk(mealType, body.Request, body.LikeCardId, body.ReplaceOffers ?? false) : null, request.HttpContext.TraceIdentifier, Language(request), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Accepts or rejects a card.</summary>
    public async Task<IResult> DecideAsync(Guid sessionId, Guid cardId, ShoppingPlanDecisionRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.DecideAsync(sessionId, cardId, body.Decision, body.Mode, body.Repeats, body.MealType, body.PlannedDate, cancellationToken), context.TraceIdentifier);

    /// <summary>Undoes the last decision.</summary>
    public async Task<IResult> UndoAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.UndoAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Expands an accepted card and creates its meals.</summary>
    public async Task<IResult> ExpandAsync(Guid sessionId, Guid cardId, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.ExpandAsync(sessionId, cardId, request.HttpContext.TraceIdentifier, Language(request), cancellationToken), request.HttpContext.TraceIdentifier);

    /// <summary>Removes an accepted card.</summary>
    public async Task<IResult> RemoveCardAsync(Guid sessionId, Guid cardId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.RemoveCardAsync(sessionId, cardId, cancellationToken), context.TraceIdentifier);

    /// <summary>Accepts the menu.</summary>
    public async Task<IResult> AcceptAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AcceptAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Discards the plan.</summary>
    public async Task<IResult> AbandonAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AbandonAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Opens a menu in use for choosing a dish again, optionally swapping one.</summary>
    public async Task<IResult> OpenAmendmentAsync(ShoppingPlanAmendmentRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.OpenAmendmentAsync(body.MenuId, body.Today, body.ReplaceEntryId, cancellationToken, Language(request)), request.HttpContext.TraceIdentifier);

    /// <summary>Gets the plan behind a menu.</summary>
    public async Task<IResult> GetMenuLinkAsync(Guid menuId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetMenuLinkAsync(menuId, cancellationToken), context.TraceIdentifier);

    /// <summary>Finds a preparation session still to finish, for one menu or for one day.</summary>
    public async Task<IResult> FindRouteLinkAsync(Guid? menuId, DateOnly? date, HttpContext context, CancellationToken cancellationToken) =>
        menuId is null && date is null
            ? ApiProblem.Create(context, StatusCodes.Status400BadRequest, "validation_failed", "Send a menu or a day.")
            : ToResult(await routes.FindLinkAsync(menuId, date, cancellationToken), context.TraceIdentifier);

    /// <summary>Gets a preparation session's route.</summary>
    public async Task<IResult> GetRouteAsync(Guid sessionId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await routes.GetAsync(sessionId, cancellationToken), context.TraceIdentifier);

    /// <summary>Replaces a preparation session's progress.</summary>
    public async Task<IResult> SaveRouteProgressAsync(Guid sessionId, ShoppingPlanRouteProgress body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await routes.SaveProgressAsync(sessionId, body, cancellationToken), context.TraceIdentifier);

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
        "domain_rule_violated" or "plan_in_progress" or "expansions_pending" or "no_more_options" or "request_blocked" or "meal_count_below_chosen" or "ai_output_invalid" => StatusCodes.Status422UnprocessableEntity,
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
/// <param name="FromMenuId">Menu of an accepted plan to repeat. That plan's answers then replace all of these except <paramref name="Today"/>, and the new plan starts today.</param>
/// <param name="MealCounts">How many of each meal. When given, style, goal, and preparation are optional.</param>
/// <param name="StartDate">First day of the period, when counts are given; today when null.</param>
public sealed record CreateShoppingPlanRequest(DateOnly? Today, int Days, IReadOnlyList<string>? MealTypes, int MealsOut, string? CookingStyle, string? Priority, DateOnly? PrepDate, int? PrepMinutes, string? AnchorIngredient, int? People, Guid? FromMenuId = null, ShoppingPlanMealCountsRequest? MealCounts = null, DateOnly? StartDate = null);

/// <summary>Body for asking for more cards. The "Quero outras opções" fields of PLAN-0064 were removed in PLAN-0065 Slice 5.</summary>
/// <param name="MealType">One meal type to suggest for; without it, the cards are for whatever is still missing.</param>
/// <param name="Request">A specific dish in the person's words, at most 120 characters; untrusted.</param>
/// <param name="LikeCardId">A card to offer dishes close to.</param>
/// <param name="ReplaceOffers">Whether earlier unseen dishes of this meal give way; false when absent.</param>
public sealed record ShoppingPlanBatchRequest(string? MealType = null, string? Request = null, Guid? LikeCardId = null, bool? ReplaceOffers = null);

/// <summary>Body for deciding a card.</summary>
/// <param name="Decision"><c>accept</c> or <c>reject</c>.</param>
/// <param name="Mode"><c>yield</c> (one cooking covers the meals) or <c>again</c> (cook again another day); required when repeats exceed one.</param>
/// <param name="Repeats">Meals to fill: 1 to 4 with <c>yield</c>, 1 to 14 with <c>again</c>.</param>
/// <param name="MealType">The meal this dish fills; the card's own when null.</param>
/// <param name="PlannedDate">An optional day inside the period.</param>
public sealed record ShoppingPlanDecisionRequest(string? Decision, string? Mode, int? Repeats, string? MealType = null, DateOnly? PlannedDate = null);

/// <summary>Body for choosing a saved recipe.</summary>
/// <param name="MealType">The meal it fills.</param>
/// <param name="PlannedDate">An optional day inside the period.</param>
public sealed record ShoppingPlanRecipeChoiceRequest(string? MealType, DateOnly? PlannedDate = null);

/// <summary>Body for opening a menu in use for changes (PLAN-0065 Slice 4).</summary>
/// <param name="MenuId">An accepted menu.</param>
/// <param name="Today">The owner's day.</param>
/// <param name="ReplaceEntryId">A dish still to cook to swap, removed by this request; null to fill what is missing.</param>
public sealed record ShoppingPlanAmendmentRequest(Guid MenuId, DateOnly? Today, Guid? ReplaceEntryId = null);

/// <summary>Body for the switches above the options.</summary>
/// <param name="UsePreferences">Whether the profile's tastes count. Restrictions and dislikes always do.</param>
/// <param name="UsePantry">Whether to build on the pantry and the dishes already chosen.</param>
/// <param name="Goal"><c>quick</c>, <c>short</c>, <c>save</c>, or null.</param>
public sealed record ShoppingPlanPreferencesRequest(bool UsePreferences, bool UsePantry, string? Goal);

/// <summary>Body for how many of each meal a plan wants.</summary>
/// <param name="Breakfast">Breakfasts.</param>
/// <param name="Lunch">Lunches.</param>
/// <param name="Dinner">Dinners.</param>
/// <param name="Snack">Snacks.</param>
public sealed record ShoppingPlanMealCountsRequest(int Breakfast, int Lunch, int Dinner, int Snack)
{
    /// <summary>The counts by meal type.</summary>
    public IReadOnlyDictionary<string, int> Counts() =>
        new Dictionary<string, int>(StringComparer.Ordinal) { ["breakfast"] = Breakfast, ["lunch"] = Lunch, ["dinner"] = Dinner, ["snack"] = Snack };
}

/// <summary>Body for the day of a chosen dish.</summary>
/// <param name="PlannedDate">A day inside the period, or null for no day.</param>
public sealed record ShoppingPlanCardDayRequest(DateOnly? PlannedDate);

/// <summary>Body for how many meals one cooking covers.</summary>
/// <param name="Meals">Meals, 1 to 4.</param>
public sealed record ShoppingPlanCardYieldRequest(int Meals);

/// <summary>Body for how a chosen dish repeats; the whole state, so a retry or an undo sends a value, not a step.</summary>
/// <param name="Mode"><c>yield</c> (cooked once, 1 to 4 meals), <c>again</c> (cooked each time, 1 to 14), or null for one meal.</param>
/// <param name="Meals">Meals the dish fills.</param>
public sealed record ShoppingPlanCardRepeatsRequest(string? Mode, int Meals);

/// <summary>Body for how many people one dish is for.</summary>
/// <param name="People">People, 1 to 12.</param>
public sealed record ShoppingPlanCardPeopleRequest(int People);

/// <summary>Body for choosing several offered dishes at once.</summary>
/// <param name="Choices">One to forty different cards.</param>
public sealed record ShoppingPlanChoicesRequest(IReadOnlyList<ShoppingPlanChoiceRequest>? Choices);

/// <summary>One dish to choose.</summary>
/// <param name="CardId">An offered card.</param>
/// <param name="MealType">The meal it fills.</param>
public sealed record ShoppingPlanChoiceRequest(Guid CardId, string? MealType);

/// <summary>Body for spreading dishes across the period.</summary>
/// <param name="Clear">Whether to remove every day instead; false when absent.</param>
public sealed record ShoppingPlanDistributionRequest(bool? Clear = null);

