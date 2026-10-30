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
        group.MapGet("/reservations", (PlanningApiService service, DateOnly? from, HttpContext context, CancellationToken cancellationToken) => service.GetReservationsAsync(from, context, cancellationToken))
            .Produces<ReservationsResponse>();
        group.MapPut("/protections/{lotId:guid}", (PlanningApiService service, Guid lotId, LotProtectionRequest body, HttpContext context, CancellationToken cancellationToken) => service.ProtectAsync(lotId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ReservationsResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404);
        group.MapDelete("/protections/{lotId:guid}", (PlanningApiService service, Guid lotId, HttpContext context, CancellationToken cancellationToken) => service.UnprotectAsync(lotId, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<ReservationsResponse>();
        group.MapPut("/package-sizes", (PlanningApiService service, PackageSizeRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetPackageSizeAsync(body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<PackageSizeRequest>()
            .ProducesProblem(400);
        group.MapGet("/preparation", (PlanningApiService service, DateOnly? from, int? days, HttpContext context, CancellationToken cancellationToken) => service.GetPreparationAsync(from, days, context, cancellationToken))
            .Produces<MealPreparationResponse>()
            .ProducesProblem(400);
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
        ToResult(await application.ChangeEntryAsync(new ChangeMenuEntryCommand(menuId, entryId, body.PlannedDate, body.MealType, body.Servings, body.Status, body.ReserveIngredients), cancellationToken), context.TraceIdentifier, ToResponse);

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
                item.Uses.Select(use => new ShoppingUseResponse(use.EntryId, use.RecipeName, use.PlannedDate, use.Quantity, use.Optional, use.MissingQuantity)).ToList(),
                item.AvailableIsEstimate,
                item.StoreSection,
                item.PackageSize,
                item.PackageCount,
                item.ExpectedRemainder)).ToList()));

    /// <summary>Records or forgets the package size of a shopping item.</summary>
    public async Task<IResult> SetPackageSizeAsync(PackageSizeRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetPackageSizeAsync(body.ItemKey, body.Size, cancellationToken), context.TraceIdentifier, size => body with { Size = size });

    /// <summary>Gets pre-meal reminders for the dated meals of menus in use.</summary>
    public async Task<IResult> GetPreparationAsync(DateOnly? from, int? days, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetPreparationAsync(from, days, cancellationToken), context.TraceIdentifier, view => new MealPreparationResponse(
            view!.From,
            view.Days,
            view.Reminders.Select(item => new MealPreparationReminderResponse(item.PlanId, item.EntryId, item.RecipeName, item.PlannedDate, item.MealType, item.Kind, item.ItemName, item.Unit, item.Quantity, item.DueDate, item.LotId, item.UseByDate)).ToList()));

    /// <summary>Checks or unchecks a shopping line.</summary>
    public async Task<IResult> SetCheckedAsync(Guid menuId, ShoppingCheckRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetCheckedAsync(menuId, body.ItemKey, body.Checked, cancellationToken), context.TraceIdentifier, isChecked => body with { Checked = isChecked });

    /// <summary>Gets pantry commitments: protections and menu reservations.</summary>
    public async Task<IResult> GetReservationsAsync(DateOnly? from, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetReservationsAsync(from, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Protects a lot.</summary>
    public async Task<IResult> ProtectAsync(Guid lotId, LotProtectionRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ProtectAsync(lotId, body.Kind, body.MinimumQuantity, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Removes a lot's protection.</summary>
    public async Task<IResult> UnprotectAsync(Guid lotId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.UnprotectAsync(lotId, cancellationToken), context.TraceIdentifier, ToResponse);

    private static ReservationsResponse ToResponse(ReservationsView view) => new(
        view.From,
        view.Lots.Select(lot => new LotCommitmentResponse(lot.LotId, lot.ProductName, lot.Unit, lot.Quantity, lot.ProtectedQuantity, lot.ReservedQuantity, lot.FreeQuantity, lot.Protection is null ? null : new LotProtectionRequest(lot.Protection.Kind, lot.Protection.MinimumQuantity), lot.Reservations.Select(ToResponse).ToList())).ToList(),
        view.Meals.Select(ToResponse).ToList());

    private static LotReservationResponse ToResponse(LotReservationView view) => new(view.MenuEntryId, view.RecipeName, view.PlannedDate, view.Quantity);

    private static MealReadinessResponse ToResponse(MealReadinessView view) => new(view.EntryId, view.Readiness, view.Missing.Select(item => new MissingIngredientResponse(item.Name, item.Unit, item.Quantity)).ToList());

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

    private static MenuEntryResponse ToResponse(MenuEntryView view) => new(view.EntryId, view.RecipeId, view.RecipeName, view.RevisionNumber, view.RevisionPinned, view.PlannedDate, view.MealType, view.Servings, view.Status, view.ExecutionId, view.ReserveIngredients, view.Readiness is null ? null : ToResponse(view.Readiness));

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
/// <param name="ReserveIngredients">Whether the meal reserves pantry quantity; unchanged when omitted.</param>
public sealed record ChangeMenuEntryRequest(DateOnly? PlannedDate, string? MealType, int Servings, string? Status, bool? ReserveIngredients = null);

/// <summary>Body (and projection) of a lot protection.</summary>
/// <param name="Kind"><c>whole</c> or <c>minimum</c>.</param>
/// <param name="MinimumQuantity">Quantity to keep, in the lot's unit, for <c>minimum</c>.</param>
public sealed record LotProtectionRequest(string? Kind, decimal? MinimumQuantity);

/// <summary>Pantry commitments from <paramref name="From"/>; read-only, never changes physical quantity.</summary>
public sealed record ReservationsResponse(DateOnly From, IReadOnlyList<LotCommitmentResponse> Lots, IReadOnlyList<MealReadinessResponse> Meals);

/// <summary>One lot with its protection and the meals counting on it; quantities in <paramref name="Unit"/>.</summary>
public sealed record LotCommitmentResponse(Guid LotId, string ProductName, string Unit, decimal Quantity, decimal ProtectedQuantity, decimal ReservedQuantity, decimal FreeQuantity, LotProtectionRequest? Protection, IReadOnlyList<LotReservationResponse> Reservations);

/// <summary>Quantity of a lot reserved for one meal.</summary>
public sealed record LotReservationResponse(Guid MenuEntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity);

/// <summary>A meal's readiness: <c>ready</c> or <c>missing</c>, with what is missing.</summary>
public sealed record MealReadinessResponse(Guid EntryId, string Readiness, IReadOnlyList<MissingIngredientResponse> Missing);

/// <summary>An ingredient a meal still lacks.</summary>
public sealed record MissingIngredientResponse(string Name, string Unit, decimal Quantity);

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
/// <remarks><c>Readiness</c> is present for meals still to cook that reserve ingredients.</remarks>
public sealed record MenuEntryResponse(Guid EntryId, Guid RecipeId, string RecipeName, int RevisionNumber, bool RevisionPinned, DateOnly? PlannedDate, string? MealType, int Servings, string Status, Guid? ExecutionId, bool ReserveIngredients, MealReadinessResponse? Readiness);

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
/// <param name="AvailableIsEstimate">Whether <paramref name="AvailableQuantity"/> includes an estimated pantry amount.</param>
/// <param name="StoreSection">Store section to group the line by: <c>produce</c>, <c>butcher_fish</c>, <c>dairy_eggs</c>, <c>bakery</c>, <c>dry_goods</c>, <c>drinks</c>, or <c>other</c> (PLAN-0037 Phase 3).</param>
/// <param name="PackageSize">Package size the owner confirmed for this item, in <paramref name="Unit"/> (PLAN-0029 Phase 4); null when none was confirmed.</param>
/// <param name="PackageCount">Whole packages that cover <paramref name="ToBuyQuantity"/>; null without a package size or with nothing to buy.</param>
/// <param name="ExpectedRemainder">What those packages leave after the menu, in <paramref name="Unit"/>; null with <paramref name="PackageCount"/>.</param>
public sealed record ShoppingItemResponse(string ItemKey, string DisplayName, string Unit, string Category, decimal RequiredQuantity, decimal AvailableQuantity, decimal ToBuyQuantity, bool Checked, IReadOnlyList<ShoppingUseResponse> Uses, bool AvailableIsEstimate, string StoreSection, decimal? PackageSize, int? PackageCount, decimal? ExpectedRemainder);

/// <summary>One meal that needs a shopping line.</summary>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day, or null when flexible.</param>
/// <param name="Quantity">What this meal needs.</param>
/// <param name="Optional">Whether the recipe marks it optional.</param>
/// <param name="MissingQuantity">What this meal still lacks after the pantry is allocated in plan order.</param>
public sealed record ShoppingUseResponse(Guid EntryId, string RecipeName, DateOnly? PlannedDate, decimal Quantity, bool Optional, decimal MissingQuantity);

/// <summary>Body (and response) for a shopping item's package size.</summary>
/// <param name="ItemKey">Item key from the shopping list; its unit is the size's unit.</param>
/// <param name="Size">Size of one package, greater than zero and at most 100,000; null forgets it.</param>
public sealed record PackageSizeRequest(string? ItemKey, decimal? Size);

/// <summary>Pre-meal reminders for the dated meals of menus in use (PLAN-0029 Phase 4). Read-only.</summary>
/// <param name="From">First day checked.</param>
/// <param name="Days">Days checked.</param>
/// <param name="Reminders">Reminders, earliest due first.</param>
public sealed record MealPreparationResponse(DateOnly From, int Days, IReadOnlyList<MealPreparationReminderResponse> Reminders);

/// <summary>Something to do before a planned meal.</summary>
/// <param name="MenuId">Menu.</param>
/// <param name="EntryId">Menu entry.</param>
/// <param name="RecipeName">Recipe name.</param>
/// <param name="PlannedDate">Day of the meal.</param>
/// <param name="MealType">Meal, if set.</param>
/// <param name="Kind"><c>buy</c> (a required ingredient is not covered), <c>thaw</c> (a reserved lot is frozen), or <c>use_by</c> (a reserved lot's date falls before the meal; advisory).</param>
/// <param name="ItemName">Ingredient or product name.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>, when there is a quantity.</param>
/// <param name="Quantity">Missing quantity (<c>buy</c>) or quantity reserved from the lot.</param>
/// <param name="DueDate">Day to act by.</param>
/// <param name="LotId">Pantry lot, for <c>thaw</c> and <c>use_by</c>.</param>
/// <param name="UseByDate">The lot's earliest date, for <c>use_by</c>.</param>
public sealed record MealPreparationReminderResponse(Guid MenuId, Guid EntryId, string RecipeName, DateOnly PlannedDate, string? MealType, string Kind, string ItemName, string? Unit, decimal? Quantity, DateOnly DueDate, Guid? LotId, DateOnly? UseByDate);
