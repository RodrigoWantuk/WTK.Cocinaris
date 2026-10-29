using System.Diagnostics;
using Cocinaris.Api.Observability;
using Cocinaris.Api.Services;
using Cocinaris.Modules.Planning.Application;
using Cocinaris.Modules.Recipes.Application;
using Microsoft.AspNetCore.Antiforgery;

namespace Cocinaris.Api.Planning;

/// <summary>Maps the authenticated menu planning and shopping routes (PLAN-0029).</summary>
public static class PlanningEndpoints
{
    /// <summary>Maps menus, entries, the menu of the day, and the shopping list. Mutations are CSRF-protected and rate limited.</summary>
    public static RouteGroupBuilder MapPlanningEndpoints(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter(ValidateCsrfAsync);
        group.MapGet("/menus", (PlanningApiService service, bool? archived, HttpContext context, CancellationToken cancellationToken) => service.ListAsync(archived == true, context, cancellationToken))
            .Produces<IReadOnlyList<MenuPlanSummaryResponse>>();
        group.MapPost("/menus", (PlanningApiService service, CreateMenuPlanRequest body, HttpRequest request, CancellationToken cancellationToken) => service.CreateAsync(body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>(StatusCodes.Status201Created)
            .Produces<MenuPlanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400);
        group.MapGet("/menus/{menuId:guid}", (PlanningApiService service, Guid menuId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(menuId, context, cancellationToken))
            .Produces<MenuPlanResponse>()
            .ProducesProblem(404);
        group.MapPut("/menus/{menuId:guid}", (PlanningApiService service, Guid menuId, ChangeMenuPlanRequest body, HttpContext context, CancellationToken cancellationToken) => service.ChangeAsync(menuId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/menus/{menuId:guid}/acceptance", (PlanningApiService service, Guid menuId, HttpContext context, CancellationToken cancellationToken) => service.AcceptAsync(menuId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/menus/{menuId:guid}/archive", (PlanningApiService service, Guid menuId, HttpContext context, CancellationToken cancellationToken) => service.ArchiveAsync(menuId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>()
            .ProducesProblem(404);
        group.MapPost("/menus/{menuId:guid}/entries", (PlanningApiService service, Guid menuId, AddMenuEntryRequest body, HttpRequest request, CancellationToken cancellationToken) => service.AddEntryAsync(menuId, body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>(StatusCodes.Status201Created)
            .Produces<MenuPlanResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPut("/menus/{menuId:guid}/entries/{entryId:guid}", (PlanningApiService service, Guid menuId, Guid entryId, ChangeMenuEntryRequest body, HttpContext context, CancellationToken cancellationToken) => service.ChangeEntryAsync(menuId, entryId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapDelete("/menus/{menuId:guid}/entries/{entryId:guid}", (PlanningApiService service, Guid menuId, Guid entryId, HttpContext context, CancellationToken cancellationToken) => service.RemoveEntryAsync(menuId, entryId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<MenuPlanResponse>()
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapGet("/menus/{menuId:guid}/shopping", (PlanningApiService service, Guid menuId, DateOnly? from, HttpContext context, CancellationToken cancellationToken) => service.GetShoppingAsync(menuId, from, context, cancellationToken))
            .Produces<ShoppingListResponse>()
            .ProducesProblem(404);
        group.MapPut("/menus/{menuId:guid}/shopping/checks", (PlanningApiService service, Guid menuId, ShoppingCheckRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetCheckedAsync(menuId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ShoppingCheckRequest>()
            .ProducesProblem(400)
            .ProducesProblem(404);
        group.MapGet("/today", (PlanningApiService service, DateOnly? date, HttpContext context, CancellationToken cancellationToken) => service.GetTodayAsync(date, context, cancellationToken))
            .Produces<IReadOnlyList<TodayMenuEntryResponse>>()
            .ProducesProblem(400);
        return group;
    }

    private static async ValueTask<object?> ValidateCsrfAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
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

/// <summary>HTTP adapter for the menu planning application service.</summary>
public sealed class PlanningApiService(MenuPlanningApplicationService application)
{
    /// <summary>Lists menus; archived menus only when asked for.</summary>
    public async Task<IResult> ListAsync(bool archived, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ListAsync(archived, cancellationToken), context.TraceIdentifier, items => items!.Select(item => new MenuPlanSummaryResponse(item.PlanId, item.Name, item.StartDate, item.EndDate, item.Status, item.EntryCount, item.UpdatedAt)).ToList());

    /// <summary>Gets one menu.</summary>
    public async Task<IResult> GetAsync(Guid menuId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(menuId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Creates a draft menu; requires an Idempotency-Key.</summary>
    public async Task<IResult> CreateAsync(CreateMenuPlanRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.CreateAsync(new CreateMenuPlanCommand(IdempotencyKey(request), body.Name, body.StartDate, body.EndDate), cancellationToken), request.HttpContext.TraceIdentifier, ToResponse);

    /// <summary>Renames a menu or changes its period.</summary>
    public async Task<IResult> ChangeAsync(Guid menuId, ChangeMenuPlanRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ChangeAsync(new ChangeMenuPlanCommand(menuId, body.Name, body.StartDate, body.EndDate), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Accepts a menu.</summary>
    public async Task<IResult> AcceptAsync(Guid menuId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.AcceptAsync(menuId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Archives a menu.</summary>
    public async Task<IResult> ArchiveAsync(Guid menuId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ArchiveAsync(menuId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Adds a recipe to a menu; requires an Idempotency-Key.</summary>
    public async Task<IResult> AddEntryAsync(Guid menuId, AddMenuEntryRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.AddEntryAsync(new AddMenuEntryCommand(menuId, IdempotencyKey(request), body.RecipeId, body.PlannedDate, body.MealType, body.Servings, body.PinRevision), cancellationToken), request.HttpContext.TraceIdentifier, ToResponse);

    /// <summary>Changes one entry.</summary>
    public async Task<IResult> ChangeEntryAsync(Guid menuId, Guid entryId, ChangeMenuEntryRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ChangeEntryAsync(new ChangeMenuEntryCommand(menuId, entryId, body.PlannedDate, body.MealType, body.Servings, body.Status), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Removes one entry.</summary>
    public async Task<IResult> RemoveEntryAsync(Guid menuId, Guid entryId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.RemoveEntryAsync(menuId, entryId, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Projects the shopping list from <paramref name="from"/> (the owner's current day).</summary>
    public async Task<IResult> GetShoppingAsync(Guid menuId, DateOnly? from, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetShoppingAsync(menuId, from, cancellationToken), context.TraceIdentifier, list => new ShoppingListResponse(
            list!.PlanId,
            list.From,
            list.MealCount,
            list.Items.Select(item => new ShoppingItemResponse(
                item.ItemKey,
                item.DisplayName,
                item.Unit,
                item.Category,
                item.RequiredQuantity,
                item.AvailableQuantity,
                item.ToBuyQuantity,
                item.Checked,
                item.Uses.Select(use => new ShoppingUseResponse(use.EntryId, use.RecipeName, use.PlannedDate, use.Quantity, use.Optional)).ToList())).ToList()));

    /// <summary>Checks or unchecks a shopping line.</summary>
    public async Task<IResult> SetCheckedAsync(Guid menuId, ShoppingCheckRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCheckedAsync(menuId, body.ItemKey, body.Checked, cancellationToken), context.TraceIdentifier, isChecked => body with { Checked = isChecked });

    /// <summary>Gets the accepted menus' meals for the owner's current day.</summary>
    public async Task<IResult> GetTodayAsync(DateOnly? date, HttpContext context, CancellationToken cancellationToken)
    {
        if (date is null)
        {
            return ApiProblem.Create(context, StatusCodes.Status400BadRequest, "validation_failed", "The date query parameter (the owner's current day) is required.");
        }

        return ToResult(await application.GetTodayAsync(date.Value, cancellationToken), context.TraceIdentifier, items => items!.Select(item => new TodayMenuEntryResponse(item.PlanId, item.PlanName, ToResponse(item.Entry))).ToList());
    }

    private static Guid? IdempotencyKey(HttpRequest request) =>
        request.Headers.TryGetValue("Idempotency-Key", out var values) && Guid.TryParse(values.ToString(), out var parsed) ? parsed : null;

    private static MenuPlanResponse ToResponse(MenuPlanView view) => new(view.PlanId, view.Name, view.StartDate, view.EndDate, view.Status, view.AcceptedAt, view.UpdatedAt, view.Entries.Select(ToResponse).ToList());

    private static MenuEntryResponse ToResponse(MenuEntryView view) => new(view.EntryId, view.RecipeId, view.RecipeName, view.RevisionNumber, view.RevisionPinned, view.PlannedDate, view.MealType, view.Servings, view.Status, view.ExecutionId);

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
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict
    };
}

/// <summary>Body for creating a draft menu.</summary>
/// <param name="Name">Optional name, at most 80 characters.</param>
/// <param name="StartDate">First day.</param>
/// <param name="EndDate">Last day, inclusive; at most 31 days in all.</param>
public sealed record CreateMenuPlanRequest(string? Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Body for renaming a menu or changing its period.</summary>
/// <param name="Name">Name, at most 80 characters.</param>
/// <param name="StartDate">First day.</param>
/// <param name="EndDate">Last day; dated meals must still fit.</param>
public sealed record ChangeMenuPlanRequest(string? Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Body for adding a recipe to a menu.</summary>
/// <param name="RecipeId">Owned recipe.</param>
/// <param name="PlannedDate">Day inside the period, or null for "some time in the period".</param>
/// <param name="MealType"><c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c>, or null.</param>
/// <param name="Servings">Portions, 1 to 24; the recipe's servings when null.</param>
/// <param name="PinRevision">Pin the current revision instead of following later edits.</param>
public sealed record AddMenuEntryRequest(Guid RecipeId, DateOnly? PlannedDate, string? MealType, int? Servings, bool PinRevision = false);

/// <summary>Body for changing a menu entry; every field is replaced.</summary>
/// <param name="PlannedDate">Day inside the period, or null for flexible.</param>
/// <param name="MealType">Meal, or null.</param>
/// <param name="Servings">Portions, 1 to 24.</param>
/// <param name="Status"><c>planned</c> or <c>skipped</c>.</param>
public sealed record ChangeMenuEntryRequest(DateOnly? PlannedDate, string? MealType, int Servings, string? Status);

/// <summary>Body (and response) for checking a shopping line.</summary>
/// <param name="ItemKey">Item key from the shopping list.</param>
/// <param name="Checked">Whether the line is checked off.</param>
public sealed record ShoppingCheckRequest(string? ItemKey, bool Checked);

/// <summary>Menu list row; <paramref name="Status"/> is <c>draft</c>, <c>accepted</c>, or <c>archived</c>.</summary>
public sealed record MenuPlanSummaryResponse(Guid MenuId, string Name, DateOnly StartDate, DateOnly EndDate, string Status, int EntryCount, DateTimeOffset UpdatedAt);

/// <summary>A menu with its meals, ordered by day (flexible last) and meal.</summary>
public sealed record MenuPlanResponse(Guid MenuId, string Name, DateOnly StartDate, DateOnly EndDate, string Status, DateTimeOffset? AcceptedAt, DateTimeOffset UpdatedAt, IReadOnlyList<MenuEntryResponse> Entries);

/// <summary>
/// One meal. <paramref name="Status"/> is <c>planned</c>, <c>skipped</c>, <c>cooking</c>, or
/// <c>cooked</c>; the last two come from the cooking session started from it
/// (<paramref name="ExecutionId"/>).
/// </summary>
public sealed record MenuEntryResponse(Guid EntryId, Guid RecipeId, string RecipeName, int RevisionNumber, bool RevisionPinned, DateOnly? PlannedDate, string? MealType, int Servings, string Status, Guid? ExecutionId);

/// <summary>A meal of an accepted menu for the requested day.</summary>
public sealed record TodayMenuEntryResponse(Guid MenuId, string MenuName, MenuEntryResponse Entry);

/// <summary>A menu's shopping list. Read-only: it never reserves or changes the pantry.</summary>
/// <param name="MenuId">Menu.</param>
/// <param name="From">First day considered.</param>
/// <param name="MealCount">Meals still to cook that were included.</param>
/// <param name="Items">Lines, ordered by category and name.</param>
public sealed record ShoppingListResponse(Guid MenuId, DateOnly From, int MealCount, IReadOnlyList<ShoppingItemResponse> Items);

/// <summary>One shopping line; quantities are in <paramref name="Unit"/> (<c>Gram</c>, <c>Milliliter</c>, <c>Unit</c>).</summary>
/// <param name="ItemKey">Key to check the line off.</param>
/// <param name="DisplayName">Name shown.</param>
/// <param name="Unit">Canonical unit.</param>
/// <param name="Category"><c>required</c>, <c>assumed</c>, <c>optional</c>, <c>runs_out</c>, or <c>available</c>.</param>
/// <param name="RequiredQuantity">What the meals need in all.</param>
/// <param name="AvailableQuantity">What the pantry holds now.</param>
/// <param name="ToBuyQuantity">What is left to buy, never negative; no package size is assumed.</param>
/// <param name="Checked">Whether the owner checked it off.</param>
/// <param name="Uses">Meals that need it.</param>
public sealed record ShoppingItemResponse(string ItemKey, string DisplayName, string Unit, string Category, decimal RequiredQuantity, decimal AvailableQuantity, decimal ToBuyQuantity, bool Checked, IReadOnlyList<ShoppingUseResponse> Uses);

/// <summary>One meal that needs a shopping line.</summary>
public sealed record ShoppingUseResponse(Guid EntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity, bool Optional);
