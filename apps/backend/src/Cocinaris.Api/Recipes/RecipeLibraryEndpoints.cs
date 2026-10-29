using System.Diagnostics;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Thumbnails;

namespace Cocinaris.Api.Recipes;

/// <summary>Maps recipe library routes (PLAN-0038) on the authenticated, CSRF-protected recipe group.</summary>
public static class RecipeLibraryEndpoints
{
    /// <summary>Maps list, detail, history, manual creation, revisions, derivations, favorites, and archive.</summary>
    public static RouteGroupBuilder MapRecipeLibraryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (RecipeLibraryApiService service, bool? archived, bool? favorites, string? search, HttpContext context, CancellationToken cancellationToken) => service.ListAsync(archived, favorites, search, context, cancellationToken))
            .Produces<IReadOnlyList<RecipeSummaryResponse>>()
            .ProducesProblem(400)
            .ProducesProblem(401);
        group.MapPost("/", (RecipeLibraryApiService service, CreateRecipeRequest body, HttpRequest request, CancellationToken cancellationToken) => service.CreateAsync(body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>(StatusCodes.Status201Created)
            .Produces<RecipeDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(422);
        group.MapGet("/{recipeId:guid}", (RecipeLibraryApiService service, Guid recipeId, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(recipeId, null, context, cancellationToken))
            .Produces<RecipeDetailResponse>()
            .ProducesProblem(404);
        group.MapGet("/{recipeId:guid}/revisions/{revisionNumber:int}", (RecipeLibraryApiService service, Guid recipeId, int revisionNumber, HttpContext context, CancellationToken cancellationToken) => service.GetAsync(recipeId, revisionNumber, context, cancellationToken))
            .Produces<RecipeDetailResponse>()
            .ProducesProblem(404);
        group.MapGet("/{recipeId:guid}/thumbnail", (RecipeLibraryApiService service, Guid recipeId, int? revision, HttpContext context, CancellationToken cancellationToken) => service.GetThumbnailAsync(recipeId, revision, context, cancellationToken))
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(404);
        group.MapGet("/{recipeId:guid}/history", (RecipeLibraryApiService service, Guid recipeId, HttpContext context, CancellationToken cancellationToken) => service.GetHistoryAsync(recipeId, context, cancellationToken))
            .Produces<RecipeHistoryResponse>()
            .ProducesProblem(404);
        group.MapPost("/{recipeId:guid}/revisions", (RecipeLibraryApiService service, Guid recipeId, ReviseRecipeRequest body, HttpContext context, CancellationToken cancellationToken) => service.ReviseAsync(recipeId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(412);
        group.MapPost("/{recipeId:guid}/derivations", (RecipeLibraryApiService service, Guid recipeId, DeriveRecipeRequest body, HttpRequest request, CancellationToken cancellationToken) => service.DeriveAsync(recipeId, body, request, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>(StatusCodes.Status201Created)
            .Produces<RecipeDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(422);
        group.MapPost("/{recipeId:guid}/favorite", (RecipeLibraryApiService service, Guid recipeId, RecipeFavoriteRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetFavoriteAsync(recipeId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>()
            .ProducesProblem(404);
        group.MapPost("/{recipeId:guid}/archive", (RecipeLibraryApiService service, Guid recipeId, RecipeArchiveRequest body, HttpContext context, CancellationToken cancellationToken) => service.SetArchivedAsync(recipeId, body, context, cancellationToken))
            .RequireRateLimiting("mutation")
            .Produces<RecipeDetailResponse>()
            .ProducesProblem(404);
        return group;
    }
}

/// <summary>HTTP adapter for the recipe library application service.</summary>
public sealed class RecipeLibraryApiService(RecipeLibraryApplicationService application)
{
    /// <summary>Lists the library; archived recipes only when <paramref name="archived"/> is true.</summary>
    public async Task<IResult> ListAsync(bool? archived, bool? favorites, string? search, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ListAsync(new RecipeLibraryQuery(archived == true, favorites == true, search), cancellationToken), context.TraceIdentifier, items => items!.Select(ToResponse).ToList());

    /// <summary>Gets a recipe at its current revision or at a given revision.</summary>
    public async Task<IResult> GetAsync(Guid recipeId, int? revisionNumber, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetAsync(recipeId, revisionNumber, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>
    /// Streams an owned recipe's dish image. The URL carries a version, so the browser may keep it,
    /// but only privately: it sits behind the owner's session.
    /// </summary>
    public async Task<IResult> GetThumbnailAsync(Guid recipeId, int? revision, HttpContext context, CancellationToken cancellationToken)
    {
        var image = await application.OpenThumbnailAsync(recipeId, revision, cancellationToken);
        if (image is not { } found)
        {
            return Results.Problem(detail: "The image was not found.", statusCode: StatusCodes.Status404NotFound, extensions: new Dictionary<string, object?> { ["errorCode"] = "resource_not_found", ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier });
        }

        context.Items[Program.PrivateCacheableItem] = true;
        context.Response.Headers.CacheControl = "private, max-age=2592000, immutable";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(found.Content, found.MediaType);
    }

    /// <summary>Gets a recipe's revisions and finished cooking sessions.</summary>
    public async Task<IResult> GetHistoryAsync(Guid recipeId, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.GetHistoryAsync(recipeId, cancellationToken), context.TraceIdentifier, history => new RecipeHistoryResponse(
            history!.Revisions.Select(item => new RecipeRevisionEntryResponse(item.RevisionNumber, item.Name, item.Origin, item.ChangeReason, item.SourceExecutionId, item.CreatedAt)).ToList(),
            history.Cooking.Select(item => new RecipeCookingEntryResponse(item.ExecutionId, item.RevisionNumber, item.Servings, item.Status, item.StartedAt, item.FinishedAt, item.Rating, item.Notes)).ToList()));

    /// <summary>Creates a recipe from scratch; requires an Idempotency-Key.</summary>
    public async Task<IResult> CreateAsync(CreateRecipeRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.CreateAsync(new CreateRecipeCommand(IdempotencyKey(request), ToDraft(body.Content)), cancellationToken), request.HttpContext.TraceIdentifier, ToResponse);

    /// <summary>Saves an edit as the next revision.</summary>
    public async Task<IResult> ReviseAsync(Guid recipeId, ReviseRecipeRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.ReviseAsync(new ReviseRecipeCommand(recipeId, body.ExpectedRevisionNumber, ToDraft(body.Content), body.ChangeReason, body.SourceExecutionId), cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Creates a derived recipe; requires an Idempotency-Key.</summary>
    public async Task<IResult> DeriveAsync(Guid recipeId, DeriveRecipeRequest body, HttpRequest request, CancellationToken cancellationToken) =>
        ToResult(await application.DeriveAsync(new DeriveRecipeCommand(recipeId, IdempotencyKey(request), body.ParentRevisionNumber, ToDraft(body.Content), body.DerivationReason, body.SourceExecutionId), cancellationToken), request.HttpContext.TraceIdentifier, ToResponse);

    /// <summary>Marks or unmarks a favorite.</summary>
    public async Task<IResult> SetFavoriteAsync(Guid recipeId, RecipeFavoriteRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetFavoriteAsync(recipeId, body.IsFavorite, cancellationToken), context.TraceIdentifier, ToResponse);

    /// <summary>Archives a recipe or brings it back.</summary>
    public async Task<IResult> SetArchivedAsync(Guid recipeId, RecipeArchiveRequest body, HttpContext context, CancellationToken cancellationToken) =>
        ToResult(await application.SetArchivedAsync(recipeId, body.Archived, cancellationToken), context.TraceIdentifier, ToResponse);

    private static Guid? IdempotencyKey(HttpRequest request) =>
        request.Headers.TryGetValue("Idempotency-Key", out var values) && Guid.TryParse(values.ToString(), out var parsed) ? parsed : null;

    private static RecipeContentDraft? ToDraft(RecipeContentRequest? content) => content is null
        ? null
        : new RecipeContentDraft(
            content.Name ?? string.Empty,
            content.MealTypes ?? [],
            content.Servings,
            content.Yield,
            (content.Ingredients ?? []).Select(item => new RecipeDraftIngredient(item.IngredientId, item.DisplayName ?? string.Empty, item.Quantity, item.Unit ?? string.Empty, item.Optional)).ToList(),
            content.Equipment,
            (content.Stages ?? []).Select(item => new RecipeDraftStage(item.StageId, item.Name ?? string.Empty, item.Instructions ?? string.Empty, item.ActiveMinutes, item.PassiveMinutes, item.SensoryCues)).ToList());

    private static RecipeSummaryResponse ToResponse(RecipeSummaryView view) => new(
        view.RecipeId, view.Name, view.MealTypes, view.Servings, view.CreatedAt, view.RevisionNumber, view.Origin, view.IsFavorite, view.ArchivedAt, view.UpdatedAt, view.TimesCooked, view.LastCookedAt, view.LastRating, Thumbnail(view.RecipeId, null, view.Thumbnail));

    private static RecipeThumbnailResponse Thumbnail(Guid recipeId, int? revisionNumber, RecipeThumbnailView? view) => view switch
    {
        { State: "ready", Version: { } version } => new RecipeThumbnailResponse("ready", $"/api/v1/recipes/{recipeId}/thumbnail?{(revisionNumber is { } number ? $"revision={number}&" : "")}v={version}"),
        { State: "pending" } => new RecipeThumbnailResponse("pending", null),
        _ => new RecipeThumbnailResponse("none", null)
    };

    private static RecipeDetailResponse ToResponse(RecipeLibraryDetailView view) => new(
        view.RecipeId,
        view.RevisionNumber,
        view.Name,
        view.MealTypes,
        view.Servings,
        view.NormalizedRecipeJson,
        view.ThumbnailVisualJson,
        view.CreatedAt,
        view.CurrentRevisionNumber,
        view.UpdatedAt,
        view.Origin,
        view.IsFavorite,
        view.ArchivedAt,
        view.ParentRecipeId,
        view.ParentRevisionNumber,
        view.DerivationReason,
        view.RevisionOrigin,
        view.ChangeReason,
        Thumbnail(view.RecipeId, view.RevisionNumber == view.CurrentRevisionNumber ? null : view.RevisionNumber, view.Thumbnail));

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
        "precondition_failed" => StatusCodes.Status412PreconditionFailed,
        "domain_rule_violated" => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status409Conflict
    };
}

/// <summary>Editable recipe content.</summary>
/// <param name="Name">Recipe name, at most 80 characters.</param>
/// <param name="MealTypes">One to four of <c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c>.</param>
/// <param name="Servings">Servings the quantities are written for, 1 to 24.</param>
/// <param name="Yield">Optional yield text.</param>
/// <param name="Ingredients">1 to 40 ingredients in display order.</param>
/// <param name="Equipment">Up to 20 utensil names.</param>
/// <param name="Stages">1 to 30 stages in order.</param>
public sealed record RecipeContentRequest(
    string? Name,
    IReadOnlyList<string>? MealTypes,
    int Servings,
    string? Yield,
    IReadOnlyList<RecipeIngredientRequest>? Ingredients,
    IReadOnlyList<string>? Equipment,
    IReadOnlyList<RecipeStageRequest>? Stages);

/// <summary>One ingredient row; <paramref name="IngredientId"/> keeps an existing row and its pantry link.</summary>
/// <param name="IngredientId">Identifier from the base revision, or null for a new row.</param>
/// <param name="DisplayName">Ingredient name.</param>
/// <param name="Quantity">Quantity, greater than zero.</param>
/// <param name="Unit"><c>g</c>, <c>ml</c>, or <c>unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
public sealed record RecipeIngredientRequest(string? IngredientId, string? DisplayName, decimal Quantity, string? Unit, bool Optional);

/// <summary>One stage; <paramref name="StageId"/> keeps an existing stage and its dependencies.</summary>
/// <param name="StageId">Identifier from the base revision, or null for a new stage.</param>
/// <param name="Name">Stage title.</param>
/// <param name="Instructions">Instructions, at most 2000 characters; line breaks separate steps.</param>
/// <param name="ActiveMinutes">Hands-on minutes, 0 to 1440.</param>
/// <param name="PassiveMinutes">Waiting minutes, 0 to 1440.</param>
/// <param name="SensoryCues">Up to six signs of doneness.</param>
public sealed record RecipeStageRequest(string? StageId, string? Name, string? Instructions, int ActiveMinutes, int PassiveMinutes, IReadOnlyList<string>? SensoryCues);

/// <summary>Body for writing a recipe from scratch.</summary>
/// <param name="Content">Recipe content.</param>
public sealed record CreateRecipeRequest(RecipeContentRequest? Content);

/// <summary>Body for saving an edit as a new revision.</summary>
/// <param name="ExpectedRevisionNumber">Revision the edit started from.</param>
/// <param name="Content">Edited content.</param>
/// <param name="ChangeReason">Optional reason, at most 200 characters.</param>
/// <param name="SourceExecutionId">Completed cooking session of this recipe the edit comes from.</param>
public sealed record ReviseRecipeRequest(int ExpectedRevisionNumber, RecipeContentRequest? Content, string? ChangeReason, Guid? SourceExecutionId);

/// <summary>Body for deriving a new recipe.</summary>
/// <param name="ParentRevisionNumber">Revision the derivation starts from.</param>
/// <param name="Content">Content of the new recipe.</param>
/// <param name="DerivationReason">How the new recipe differs, at most 200 characters.</param>
/// <param name="SourceExecutionId">Completed cooking session of the parent the changes come from.</param>
public sealed record DeriveRecipeRequest(int ParentRevisionNumber, RecipeContentRequest? Content, string? DerivationReason, Guid? SourceExecutionId);

/// <summary>Body for marking a favorite.</summary>
/// <param name="IsFavorite">Whether the recipe is a favorite.</param>
public sealed record RecipeFavoriteRequest(bool IsFavorite);

/// <summary>Body for archiving a recipe.</summary>
/// <param name="Archived">True to archive, false to bring it back to the library.</param>
public sealed record RecipeArchiveRequest(bool Archived);

/// <summary>A recipe's revisions and finished cooking sessions, newest first.</summary>
public sealed record RecipeHistoryResponse(IReadOnlyList<RecipeRevisionEntryResponse> Revisions, IReadOnlyList<RecipeCookingEntryResponse> Cooking);

/// <summary>One revision: <paramref name="Origin"/> is <c>generated</c>, <c>manual</c>, <c>edited</c>, or <c>derived</c>.</summary>
public sealed record RecipeRevisionEntryResponse(int RevisionNumber, string Name, string Origin, string? ChangeReason, Guid? SourceExecutionId, DateTimeOffset CreatedAt);

/// <summary>One finished cooking session: <paramref name="Status"/> is <c>Completed</c> or <c>Abandoned</c>.</summary>
public sealed record RecipeCookingEntryResponse(Guid ExecutionId, int RevisionNumber, int Servings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int? Rating, string? Notes);
