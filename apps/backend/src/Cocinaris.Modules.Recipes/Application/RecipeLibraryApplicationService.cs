using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Recipes.Domain;
using Cocinaris.Modules.Recipes.Thumbnails;

namespace Cocinaris.Modules.Recipes.Application;

/// <summary>Library filter: archived recipes are listed only when asked for, and never together with active ones.</summary>
/// <param name="Archived">List archived recipes instead of the active library.</param>
/// <param name="FavoritesOnly">List favorites only.</param>
/// <param name="Search">Case-insensitive fragment of the current name.</param>
public sealed record RecipeLibraryQuery(bool Archived, bool FavoritesOnly, string? Search);

/// <summary>One library row: current revision, organization flags, cooking statistics, and the current revision's visual descriptor.</summary>
public sealed record RecipeLibraryItem(
    Guid RecipeId,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    int RevisionNumber,
    RecipeOrigin Origin,
    bool IsFavorite,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TimesCooked,
    DateTimeOffset? LastCookedAt,
    int? LastRating,
    string ThumbnailVisualJson = "");

/// <summary>One stored revision with its provenance.</summary>
public sealed record RecipeRevisionContent(
    int RevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    RecipeRevisionOrigin Origin,
    string? ChangeReason,
    Guid? SourceExecutionId,
    DateTimeOffset CreatedAt);

/// <summary>One completed or abandoned cooking session of a recipe.</summary>
public sealed record RecipeCookingEntry(Guid ExecutionId, int RevisionNumber, int Servings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int? Rating, string? Notes);

/// <summary>Owner-scoped persistence port for the recipe library (PLAN-0038).</summary>
public interface IRecipeLibraryStore
{
    /// <summary>Lists the owner's recipes, most recently changed first.</summary>
    Task<IReadOnlyList<RecipeLibraryItem>> ListAsync(Guid ownerUserId, RecipeLibraryQuery query, CancellationToken cancellationToken);

    /// <summary>Finds one owned recipe aggregate, or null without disclosing another owner's recipe.</summary>
    Task<Recipe?> FindAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken);

    /// <summary>Finds the recipe a manual or derivation request created, by its idempotency key.</summary>
    Task<Recipe?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken);

    /// <summary>Finds one stored revision of an owned recipe.</summary>
    Task<RecipeRevisionContent?> FindRevisionAsync(Guid ownerUserId, Guid recipeId, int revisionNumber, CancellationToken cancellationToken);

    /// <summary>Lists every revision of an owned recipe, newest first.</summary>
    Task<IReadOnlyList<RecipeRevisionContent>> ListRevisionsAsync(Guid ownerUserId, Guid recipeId, CancellationToken cancellationToken);

    /// <summary>Lists finished cooking sessions of an owned recipe, newest first.</summary>
    Task<IReadOnlyList<RecipeCookingEntry>> ListCookingAsync(Guid ownerUserId, Guid recipeId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a new manual or derived recipe and its first revision. Returns false, persisting
    /// nothing, when the creation key was already used by this owner.
    /// </summary>
    Task<bool> TryAddAsync(Recipe recipe, RecipeRevision revision, CancellationToken cancellationToken);

    /// <summary>
    /// Appends an edited revision and advances the recipe in one transaction, only while the
    /// recipe's current revision is still <paramref name="expectedRevisionNumber"/>. Returns false,
    /// persisting nothing, when another revision was saved first.
    /// </summary>
    Task<bool> TryAppendRevisionAsync(Recipe recipe, RecipeRevision revision, int expectedRevisionNumber, CancellationToken cancellationToken);

    /// <summary>Saves the favorite and archive flags.</summary>
    Task SaveOrganizationAsync(Recipe recipe, CancellationToken cancellationToken);
}

/// <summary>A recipe's revision with library state; the shape every library read and mutation returns.</summary>
public sealed record RecipeLibraryDetailView(
    Guid RecipeId,
    int RevisionNumber,
    int CurrentRevisionNumber,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    string NormalizedRecipeJson,
    string ThumbnailVisualJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Origin,
    bool IsFavorite,
    DateTimeOffset? ArchivedAt,
    Guid? ParentRecipeId,
    int? ParentRevisionNumber,
    string? DerivationReason,
    string RevisionOrigin,
    string? ChangeReason,
    RecipeThumbnailView? Thumbnail = null);

/// <summary>Library row projection.</summary>
public sealed record RecipeSummaryView(
    Guid RecipeId,
    string Name,
    IReadOnlyList<string> MealTypes,
    int Servings,
    int RevisionNumber,
    string Origin,
    bool IsFavorite,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TimesCooked,
    DateTimeOffset? LastCookedAt,
    int? LastRating,
    RecipeThumbnailView? Thumbnail = null);

/// <summary>One revision in a recipe's history.</summary>
public sealed record RecipeRevisionEntryView(int RevisionNumber, string Name, string Origin, string? ChangeReason, Guid? SourceExecutionId, DateTimeOffset CreatedAt);

/// <summary>One finished cooking session in a recipe's history.</summary>
public sealed record RecipeCookingEntryView(Guid ExecutionId, int RevisionNumber, int Servings, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int? Rating, string? Notes);

/// <summary>A recipe's revisions and cooking sessions.</summary>
public sealed record RecipeHistoryView(IReadOnlyList<RecipeRevisionEntryView> Revisions, IReadOnlyList<RecipeCookingEntryView> Cooking);

/// <summary>Writes a recipe from scratch.</summary>
public sealed record CreateRecipeCommand(Guid? IdempotencyKey, RecipeContentDraft? Content);

/// <summary>Saves an edit as the recipe's next revision.</summary>
/// <param name="RecipeId">Recipe to revise.</param>
/// <param name="ExpectedRevisionNumber">Revision the edit started from; a newer revision makes the edit stale.</param>
/// <param name="Content">Edited content.</param>
/// <param name="ChangeReason">Optional reason shown in the history.</param>
/// <param name="SourceExecutionId">Completed execution of this recipe whose adjustments the edit saves.</param>
public sealed record ReviseRecipeCommand(Guid RecipeId, int ExpectedRevisionNumber, RecipeContentDraft? Content, string? ChangeReason, Guid? SourceExecutionId);

/// <summary>Creates a new recipe derived from a revision of an owned recipe.</summary>
/// <param name="RecipeId">Parent recipe.</param>
/// <param name="IdempotencyKey">Request key; a retry returns the recipe already derived.</param>
/// <param name="ParentRevisionNumber">Revision the derivation started from.</param>
/// <param name="Content">Content of the derived recipe.</param>
/// <param name="DerivationReason">Required reason, for example "air-fryer version".</param>
/// <param name="SourceExecutionId">Completed execution of the parent whose changes the derivation saves.</param>
public sealed record DeriveRecipeCommand(Guid RecipeId, Guid? IdempotencyKey, int ParentRevisionNumber, RecipeContentDraft? Content, string? DerivationReason, Guid? SourceExecutionId);

/// <summary>
/// Recipe library: listing and organization (favorites, archive), manual recipes, edits saved as
/// new immutable revisions, derived recipes, and history (PLAN-0038). Content is composed and
/// validated deterministically by <see cref="RecipeContentComposer"/>; no model is involved, and no
/// revision is ever rewritten.
/// </summary>
public sealed class RecipeLibraryApplicationService(
    ICurrentUserAccessor currentUser,
    IRecipeLibraryStore store,
    IRecipeExecutionStore executions,
    TimeProvider timeProvider,
    RecipeThumbnailService? thumbnails = null)
{
    private const int CookingHistoryLimit = 20;
    private const int MaxSearchLength = 80;

    /// <summary>Lists the owner's library.</summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<RecipeSummaryView>>> ListAsync(RecipeLibraryQuery query, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search is { Length: > MaxSearchLength })
        {
            return Failure<IReadOnlyList<RecipeSummaryView>>("validation_failed", "The search must be at most 80 characters.", "search");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var items = await store.ListAsync(user.Id, query with { Search = search }, cancellationToken);
        var images = thumbnails is null
            ? new Dictionary<Guid, RecipeThumbnailView>()
            : await thumbnails.ResolveAsync(user.Id, items.ToDictionary(item => item.RecipeId, item => item.ThumbnailVisualJson), cancellationToken);
        return RecipeApplicationResult<IReadOnlyList<RecipeSummaryView>>.Succeeded(items.Select(item => new RecipeSummaryView(
            item.RecipeId,
            item.Name,
            item.MealTypes,
            item.Servings,
            item.RevisionNumber,
            Name(item.Origin),
            item.IsFavorite,
            item.ArchivedAt,
            item.CreatedAt,
            item.UpdatedAt,
            item.TimesCooked,
            item.LastCookedAt,
            item.LastRating,
            images.GetValueOrDefault(item.RecipeId, RecipeThumbnailView.None))).ToList());
    }

    /// <summary>Gets an owned recipe at its current revision, or at <paramref name="revisionNumber"/>.</summary>
    public async Task<RecipeApplicationResult<RecipeLibraryDetailView>> GetAsync(Guid recipeId, int? revisionNumber, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await store.FindAsync(user.Id, recipeId, cancellationToken);
        if (recipe is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        var revision = await store.FindRevisionAsync(user.Id, recipeId, revisionNumber ?? recipe.CurrentRevisionNumber, cancellationToken);
        if (revision is null)
        {
            return Failure<RecipeLibraryDetailView>("resource_not_found", "The recipe revision was not found.");
        }

        var image = thumbnails is null
            ? RecipeThumbnailView.None
            : (await thumbnails.ResolveAsync(user.Id, new Dictionary<int, string> { [revision.RevisionNumber] = revision.ThumbnailVisualJson }, cancellationToken))[revision.RevisionNumber];
        return RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(View(recipe, revision) with { Thumbnail = image });
    }

    /// <summary>
    /// Opens the image of an owned recipe's revision (the current one when null). Ownership is
    /// checked first, so another owner's recipe is indistinguishable from a missing one.
    /// </summary>
    public async Task<(Stream Content, string MediaType)?> OpenThumbnailAsync(Guid recipeId, int? revisionNumber, CancellationToken cancellationToken)
    {
        if (thumbnails is null)
        {
            return null;
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await store.FindAsync(user.Id, recipeId, cancellationToken);
        if (recipe is null)
        {
            return null;
        }

        var revision = await store.FindRevisionAsync(user.Id, recipeId, revisionNumber ?? recipe.CurrentRevisionNumber, cancellationToken);
        return revision is null ? null : await thumbnails.OpenAsync(revision.ThumbnailVisualJson, cancellationToken);
    }

    /// <summary>Gets an owned recipe's revisions and finished cooking sessions.</summary>
    public async Task<RecipeApplicationResult<RecipeHistoryView>> GetHistoryAsync(Guid recipeId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (await store.FindAsync(user.Id, recipeId, cancellationToken) is null)
        {
            return NotFound<RecipeHistoryView>();
        }

        var revisions = await store.ListRevisionsAsync(user.Id, recipeId, cancellationToken);
        var cooking = await store.ListCookingAsync(user.Id, recipeId, CookingHistoryLimit, cancellationToken);
        return RecipeApplicationResult<RecipeHistoryView>.Succeeded(new RecipeHistoryView(
            revisions.Select(item => new RecipeRevisionEntryView(item.RevisionNumber, item.Name, Name(item.Origin), item.ChangeReason, item.SourceExecutionId, item.CreatedAt)).ToList(),
            cooking.Select(item => new RecipeCookingEntryView(item.ExecutionId, item.RevisionNumber, item.Servings, item.Status, item.StartedAt, item.FinishedAt, item.Rating, item.Notes)).ToList()));
    }

    /// <summary>Creates a recipe the owner wrote. A retry with the same key returns the same recipe.</summary>
    public async Task<RecipeApplicationResult<RecipeLibraryDetailView>> CreateAsync(CreateRecipeCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (command.Content is null)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "The recipe content is required.", "content");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (await ReplayAsync(user.Id, key, parentRecipeId: null, cancellationToken) is { } replay)
        {
            return replay;
        }

        var now = timeProvider.GetUtcNow();
        var recipe = Recipe.CreateManual(user.Id, key, now);
        var composition = RecipeContentComposer.Compose(null, command.Content, recipe.Id.ToString("n"), 1);
        if (!composition.IsValid)
        {
            return Invalid<RecipeLibraryDetailView>(composition);
        }

        var revision = RecipeRevision.CreateAuthored(recipe.Id, user.Id, 1, RecipeRevisionOrigin.Manual, command.Content.Name.Trim(), Distinct(command.Content.MealTypes), command.Content.Servings, composition.NormalizedRecipeJson!, "{}", null, null, now);
        return await AddAsync(recipe, revision, key, parentRecipeId: null, cancellationToken);
    }

    /// <summary>Saves an edit as the next immutable revision; a stale base revision fails with <c>precondition_failed</c>.</summary>
    public async Task<RecipeApplicationResult<RecipeLibraryDetailView>> ReviseAsync(ReviseRecipeCommand command, CancellationToken cancellationToken)
    {
        if (command.Content is null)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "The recipe content is required.", "content");
        }

        if (command.ChangeReason is { Length: > Recipe.MaxReasonLength })
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "The change reason must be at most 200 characters.", "changeReason");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await store.FindAsync(user.Id, command.RecipeId, cancellationToken);
        if (recipe is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        if (recipe.CurrentRevisionNumber != command.ExpectedRevisionNumber)
        {
            return Failure<RecipeLibraryDetailView>("precondition_failed", "The recipe has a newer revision. Reload it before saving.");
        }

        if (await CheckExecutionAsync(user.Id, recipe.Id, command.SourceExecutionId, cancellationToken) is { } executionProblem)
        {
            return executionProblem;
        }

        var current = await store.FindRevisionAsync(user.Id, recipe.Id, recipe.CurrentRevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The current revision of a stored recipe is missing.");
        var next = recipe.CurrentRevisionNumber + 1;
        var composition = RecipeContentComposer.Compose(current.NormalizedRecipeJson, command.Content, recipe.Id.ToString("n"), next);
        if (!composition.IsValid)
        {
            return Invalid<RecipeLibraryDetailView>(composition);
        }

        var now = timeProvider.GetUtcNow();
        var revision = RecipeRevision.CreateAuthored(recipe.Id, user.Id, next, RecipeRevisionOrigin.Edited, command.Content.Name.Trim(), Distinct(command.Content.MealTypes), command.Content.Servings, composition.NormalizedRecipeJson!, current.ThumbnailVisualJson, command.ChangeReason, command.SourceExecutionId, now);
        var expected = recipe.CurrentRevisionNumber;
        recipe.AdvanceToRevision(next, now);
        if (!await store.TryAppendRevisionAsync(recipe, revision, expected, cancellationToken))
        {
            return Failure<RecipeLibraryDetailView>("precondition_failed", "The recipe has a newer revision. Reload it before saving.");
        }

        return RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(await WithThumbnailAsync(user.Id, View(recipe, Content(revision)), cancellationToken), RecipeApplicationSuccess.Created);
    }

    /// <summary>Creates a new recipe derived from a revision of an owned recipe. The parent is not changed.</summary>
    public async Task<RecipeApplicationResult<RecipeLibraryDetailView>> DeriveAsync(DeriveRecipeCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (command.Content is null)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "The recipe content is required.", "content");
        }

        if (string.IsNullOrWhiteSpace(command.DerivationReason) || command.DerivationReason.Trim().Length > Recipe.MaxReasonLength)
        {
            return Failure<RecipeLibraryDetailView>("validation_failed", "Say how the new recipe differs, in at most 200 characters.", "derivationReason");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var parent = await store.FindAsync(user.Id, command.RecipeId, cancellationToken);
        if (parent is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        if (await ReplayAsync(user.Id, key, parent.Id, cancellationToken) is { } replay)
        {
            return replay;
        }

        var parentRevision = await store.FindRevisionAsync(user.Id, parent.Id, command.ParentRevisionNumber, cancellationToken);
        if (parentRevision is null)
        {
            return Failure<RecipeLibraryDetailView>("resource_not_found", "The parent revision was not found.");
        }

        if (await CheckExecutionAsync(user.Id, parent.Id, command.SourceExecutionId, cancellationToken) is { } executionProblem)
        {
            return executionProblem;
        }

        var now = timeProvider.GetUtcNow();
        var recipe = Recipe.CreateDerived(user.Id, key, parent.Id, parentRevision.RevisionNumber, command.DerivationReason, now);
        var composition = RecipeContentComposer.Compose(parentRevision.NormalizedRecipeJson, command.Content, recipe.Id.ToString("n"), 1);
        if (!composition.IsValid)
        {
            return Invalid<RecipeLibraryDetailView>(composition);
        }

        var revision = RecipeRevision.CreateAuthored(recipe.Id, user.Id, 1, RecipeRevisionOrigin.Derived, command.Content.Name.Trim(), Distinct(command.Content.MealTypes), command.Content.Servings, composition.NormalizedRecipeJson!, parentRevision.ThumbnailVisualJson, null, command.SourceExecutionId, now);
        return await AddAsync(recipe, revision, key, parent.Id, cancellationToken);
    }

    /// <summary>Marks or unmarks an owned recipe as a favorite.</summary>
    public Task<RecipeApplicationResult<RecipeLibraryDetailView>> SetFavoriteAsync(Guid recipeId, bool isFavorite, CancellationToken cancellationToken) =>
        OrganizeAsync(recipeId, recipe => recipe.SetFavorite(isFavorite), cancellationToken);

    /// <summary>Archives an owned recipe, or brings it back to the library. History and executions are kept.</summary>
    public Task<RecipeApplicationResult<RecipeLibraryDetailView>> SetArchivedAsync(Guid recipeId, bool archived, CancellationToken cancellationToken) =>
        OrganizeAsync(recipeId, recipe => recipe.SetArchived(archived, timeProvider.GetUtcNow()), cancellationToken);

    private async Task<RecipeApplicationResult<RecipeLibraryDetailView>> OrganizeAsync(Guid recipeId, Action<Recipe> change, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var recipe = await store.FindAsync(user.Id, recipeId, cancellationToken);
        if (recipe is null)
        {
            return NotFound<RecipeLibraryDetailView>();
        }

        change(recipe);
        await store.SaveOrganizationAsync(recipe, cancellationToken);
        var revision = await store.FindRevisionAsync(user.Id, recipe.Id, recipe.CurrentRevisionNumber, cancellationToken)
            ?? throw new InvalidOperationException("The current revision of a stored recipe is missing.");
        return RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(await WithThumbnailAsync(user.Id, View(recipe, revision), cancellationToken));
    }

    private async Task<RecipeApplicationResult<RecipeLibraryDetailView>> AddAsync(Recipe recipe, RecipeRevision revision, Guid key, Guid? parentRecipeId, CancellationToken cancellationToken)
    {
        if (await store.TryAddAsync(recipe, revision, cancellationToken))
        {
            return RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(await WithThumbnailAsync(recipe.OwnerUserId, View(recipe, Content(revision)), cancellationToken), RecipeApplicationSuccess.Created);
        }

        // A concurrent retry with the same key won the insert; return what it created.
        return await ReplayAsync(recipe.OwnerUserId, key, parentRecipeId, cancellationToken)
            ?? throw new InvalidOperationException("A recipe creation key conflicted but the recipe could not be reloaded.");
    }

    private async Task<RecipeApplicationResult<RecipeLibraryDetailView>?> ReplayAsync(Guid ownerUserId, Guid key, Guid? parentRecipeId, CancellationToken cancellationToken)
    {
        var existing = await store.FindByCreationKeyAsync(ownerUserId, key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.ParentRecipeId != parentRecipeId)
        {
            return Failure<RecipeLibraryDetailView>("domain_rule_violated", "This Idempotency-Key was already used for another recipe.");
        }

        var revision = await store.FindRevisionAsync(ownerUserId, existing.Id, 1, cancellationToken)
            ?? throw new InvalidOperationException("The first revision of a stored recipe is missing.");
        return RecipeApplicationResult<RecipeLibraryDetailView>.Succeeded(await WithThumbnailAsync(ownerUserId, View(existing, revision), cancellationToken));
    }

    // Only a finished session of the same recipe can be cited as the source of a change.
    private async Task<RecipeApplicationResult<RecipeLibraryDetailView>?> CheckExecutionAsync(Guid ownerUserId, Guid recipeId, Guid? executionId, CancellationToken cancellationToken)
    {
        if (executionId is not Guid id)
        {
            return null;
        }

        var execution = await executions.FindAsync(ownerUserId, id, cancellationToken);
        return execution is null || execution.RecipeId != recipeId || execution.Status != RecipeExecutionStatus.Completed
            ? Failure<RecipeLibraryDetailView>("validation_failed", "The cooking session must be a completed session of this recipe.", "sourceExecutionId")
            : null;
    }

    // Every detail a library call returns carries its image, so a favorite or an edit keeps it on screen.
    private async Task<RecipeLibraryDetailView> WithThumbnailAsync(Guid ownerUserId, RecipeLibraryDetailView view, CancellationToken cancellationToken) =>
        thumbnails is null
            ? view
            : view with { Thumbnail = (await thumbnails.ResolveAsync(ownerUserId, new Dictionary<int, string> { [view.RevisionNumber] = view.ThumbnailVisualJson }, cancellationToken))[view.RevisionNumber] };

    private static RecipeLibraryDetailView View(Recipe recipe, RecipeRevisionContent revision) => new(
        recipe.Id,
        revision.RevisionNumber,
        recipe.CurrentRevisionNumber,
        revision.Name,
        revision.MealTypes,
        revision.Servings,
        revision.NormalizedRecipeJson,
        revision.ThumbnailVisualJson,
        recipe.CreatedAt,
        recipe.UpdatedAt,
        Name(recipe.Origin),
        recipe.IsFavorite,
        recipe.ArchivedAt,
        recipe.ParentRecipeId,
        recipe.ParentRevisionNumber,
        recipe.DerivationReason,
        Name(revision.Origin),
        revision.ChangeReason);

    private static RecipeRevisionContent Content(RecipeRevision revision) => new(
        revision.RevisionNumber,
        revision.Name,
        revision.MealTypes,
        revision.Servings,
        revision.NormalizedRecipeJson,
        revision.ThumbnailVisualJson,
        revision.Origin,
        revision.ChangeReason,
        revision.SourceExecutionId,
        revision.CreatedAt);

    private static List<string> Distinct(IReadOnlyList<string> mealTypes) => mealTypes.Distinct(StringComparer.Ordinal).ToList();

    private static string Name(RecipeOrigin origin) => origin.ToString().ToLowerInvariant();

    private static string Name(RecipeRevisionOrigin origin) => origin.ToString().ToLowerInvariant();

    private static RecipeApplicationResult<T> NotFound<T>() => Failure<T>("resource_not_found", "The recipe was not found.");

    private static RecipeApplicationResult<T> Invalid<T>(RecipeComposition composition) =>
        RecipeApplicationResult<T>.Failure("validation_failed", "The recipe content is invalid.", composition.Errors);

    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, string? field = null) =>
        RecipeApplicationResult<T>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [detail] });
}
