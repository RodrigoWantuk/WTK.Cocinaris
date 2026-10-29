using System.Diagnostics;
using Cocinaris.Api.Observability;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Cocinaris.Api.Recipes;

/// <summary>Maps authenticated recipe cook-now HTTP routes to the Recipes application-service boundary.</summary>
public static class RecipeEndpoints
{
    /// <summary>Maps the authenticated recipe routes without embedding persistence or AI orchestration in endpoint handlers.</summary>
    public static RouteGroupBuilder MapRecipeEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.MapPost("/generation-sessions", (RecipeApiService service, RequestCandidatesRequest? body, HttpRequest request, CancellationToken cancellationToken) => service.RequestCandidatesAsync(body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeGenerationSessionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(404)
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
        group.MapRecipeLibraryEndpoints();
        group.MapRecipeExecutionEndpoints();
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
public sealed class RecipeApiService(RecipeCookNowApplicationService application, MenuPlanningApplicationService planning)
{
    private const int MaxPlannedNames = 20;

    /// <summary>
    /// Creates a generation session and requests three validated candidates: cook-now, or for a
    /// menu slot when <paramref name="body"/> names one (PLAN-0029 Phase 2). The menu is resolved
    /// here, owner-scoped, so the names of meals already planned come from the backend.
    /// </summary>
    public async Task<IResult> RequestCandidatesAsync(RequestCandidatesRequest? body, HttpRequest request, CancellationToken cancellationToken)
    {
        MenuPlanningTarget? target = null;
        if (body?.MenuPlanning is { } slot)
        {
            var menu = await planning.GetAsync(slot.MenuId, cancellationToken);
            if (menu.Problem is not null || menu.Value is null)
            {
                return Problem("resource_not_found", "The menu was not found.", StatusCodes.Status404NotFound, request.HttpContext.TraceIdentifier);
            }

            var plan = menu.Value;
            var invalid = slot.Servings is < 1 or > 24 ? "servings"
                : slot.PlannedDate is { } day && (day < plan.StartDate || day > plan.EndDate) ? "plannedDate"
                : slot.MealType is not null and not ("breakfast" or "lunch" or "dinner" or "snack") ? "mealType"
                : plan.Status == "archived" ? "menuId"
                : null;
            if (invalid is not null)
            {
                return Problem("validation_failed", "The menu slot is invalid.", StatusCodes.Status400BadRequest, request.HttpContext.TraceIdentifier, new Dictionary<string, string[]> { [invalid] = ["The menu slot is invalid."] });
            }

            var names = plan.Entries
                .Where(entry => entry.Status != "skipped" && entry.RecipeName.Length > 0)
                .Select(entry => entry.RecipeName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxPlannedNames)
                .ToList();
            target = new MenuPlanningTarget(slot.PlannedDate, slot.MealType, slot.Servings, names);
        }

        var command = new RequestCandidatesCommand(ParseIdempotencyKey(request), request.HttpContext.TraceIdentifier, RequestLanguage(request), target);
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
        var command = new SelectCandidateCommand(sessionId, body.CandidateId, ParseIdempotencyKey(request), request.HttpContext.TraceIdentifier, RequestLanguage(request));
        var result = await application.SelectCandidateAsync(command, cancellationToken);
        return ToResult(result, request.HttpContext.TraceIdentifier, ToResponse);
    }

    /// <summary>
    /// First supported tag of <c>Accept-Language</c>. The frontend sends its interface locale there,
    /// and the browser default otherwise; the application layer accepts only supported tags.
    /// </summary>
    private static string? RequestLanguage(HttpRequest request) =>
        request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(item => item.Quality ?? 1)
            .Select(item => item.Value.Value?.ToLowerInvariant() switch { "pt-br" or "pt" => "pt-BR", "en" or "en-us" or "en-gb" => "en", "es" or "es-es" => "es", _ => null })
            .FirstOrDefault(tag => tag is not null);

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

    // A selection returns the generated recipe it created: revision 1, not a favorite, not archived.
    private static RecipeDetailResponse ToResponse(RecipeDetailView view) => new(
        view.RecipeId,
        view.RevisionNumber,
        view.Name,
        view.MealTypes,
        view.Servings,
        view.NormalizedRecipeJson,
        view.ThumbnailVisualJson,
        view.CreatedAt,
        view.RevisionNumber,
        view.CreatedAt,
        "generated",
        false,
        null,
        null,
        null,
        null,
        "generated",
        null);

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

/// <summary>Optional body for requesting candidates; without it the request is cook-now.</summary>
/// <param name="MenuPlanning">Menu slot to plan (PLAN-0029 Phase 2).</param>
public sealed record RequestCandidatesRequest(MenuPlanningSlotRequest? MenuPlanning);

/// <summary>A slot of an owned menu.</summary>
/// <param name="MenuId">Menu.</param>
/// <param name="PlannedDate">Day inside the menu's period, or null for flexible.</param>
/// <param name="MealType"><c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c>, or null.</param>
/// <param name="Servings">Portions, 1 to 24.</param>
public sealed record MenuPlanningSlotRequest(Guid MenuId, DateOnly? PlannedDate, string? MealType, int Servings);

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

/// <summary>HTTP projection of one library row.</summary>
/// <param name="RecipeId">Recipe identifier.</param>
/// <param name="Name">Current revision name.</param>
/// <param name="MealTypes">Current revision meal types.</param>
/// <param name="Servings">Current revision servings.</param>
/// <param name="CreatedAt">Recipe creation instant.</param>
/// <param name="RevisionNumber">Current revision number.</param>
/// <param name="Origin"><c>generated</c>, <c>manual</c>, or <c>derived</c>.</param>
/// <param name="IsFavorite">Whether the owner marked it as a favorite.</param>
/// <param name="ArchivedAt">Archive instant, or null while in the library.</param>
/// <param name="UpdatedAt">Instant of the last revision.</param>
/// <param name="TimesCooked">Completed cooking sessions.</param>
/// <param name="LastCookedAt">Completion instant of the latest session.</param>
/// <param name="LastRating">Rating given in the latest session, if any.</param>
public sealed record RecipeSummaryResponse(
    Guid RecipeId,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    DateTimeOffset CreatedAt,
    int RevisionNumber,
    string Origin,
    bool IsFavorite,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset UpdatedAt,
    int TimesCooked,
    DateTimeOffset? LastCookedAt,
    int? LastRating);

/// <summary>HTTP projection of one revision of an owned recipe with its library state.</summary>
/// <param name="RecipeId">Recipe identifier.</param>
/// <param name="RevisionNumber">Revision shown.</param>
/// <param name="Name">Revision name.</param>
/// <param name="MealTypes">Revision meal types.</param>
/// <param name="Servings">Servings the quantities are written for.</param>
/// <param name="NormalizedRecipeJson">Revision content (protocol 0.3 shape).</param>
/// <param name="ThumbnailVisualJson">Thumbnail descriptor; <c>{}</c> for recipes written by the owner.</param>
/// <param name="CreatedAt">Recipe creation instant.</param>
/// <param name="CurrentRevisionNumber">Latest revision; differs from <paramref name="RevisionNumber"/> when an older revision is shown.</param>
/// <param name="UpdatedAt">Instant of the latest revision.</param>
/// <param name="Origin"><c>generated</c>, <c>manual</c>, or <c>derived</c>.</param>
/// <param name="IsFavorite">Whether the owner marked it as a favorite.</param>
/// <param name="ArchivedAt">Archive instant, or null while in the library.</param>
/// <param name="ParentRecipeId">Parent of a derived recipe.</param>
/// <param name="ParentRevisionNumber">Parent revision a derived recipe started from.</param>
/// <param name="DerivationReason">How a derived recipe differs from its parent.</param>
/// <param name="RevisionOrigin"><c>generated</c>, <c>manual</c>, <c>edited</c>, or <c>derived</c>.</param>
/// <param name="ChangeReason">Reason given for an edited revision.</param>
public sealed record RecipeDetailResponse(
    Guid RecipeId,
    int RevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    DateTimeOffset CreatedAt,
    int CurrentRevisionNumber,
    DateTimeOffset UpdatedAt,
    string Origin,
    bool IsFavorite,
    DateTimeOffset? ArchivedAt,
    Guid? ParentRecipeId,
    int? ParentRevisionNumber,
    string? DerivationReason,
    string RevisionOrigin,
    string? ChangeReason);
