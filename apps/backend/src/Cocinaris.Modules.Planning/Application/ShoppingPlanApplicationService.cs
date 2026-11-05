using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Profiles.Domain;
using Cocinaris.Modules.Recipes.Ai;
using Cocinaris.Modules.Recipes.Application;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>Owner-scoped persistence port for shopping plans. Another owner's identifier behaves as absent.</summary>
public interface IShoppingPlanStore
{
    /// <summary>Finds one plan.</summary>
    Task<ShoppingPlanSession?> FindAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Finds the plan a creation request made, by its key.</summary>
    Task<ShoppingPlanSession?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken);

    /// <summary>Finds the plan in progress, if any.</summary>
    Task<ShoppingPlanSession?> FindCurrentAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Finds the latest plan, to reuse its answers as defaults.</summary>
    Task<ShoppingPlanSession?> FindLatestAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Lists the newest accepted plans, to offer repeating them.</summary>
    Task<IReadOnlyList<ShoppingPlanSession>> ListFinishedAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Finds the accepted plan that filled a menu, if any.</summary>
    Task<ShoppingPlanSession?> FindFinishedByMenuAsync(Guid ownerUserId, Guid menuPlanId, CancellationToken cancellationToken);

    /// <summary>Adds a plan. Returns false, persisting nothing, when its key was used or another plan is in progress.</summary>
    Task<bool> TryAddAsync(ShoppingPlanSession session, CancellationToken cancellationToken);

    /// <summary>Saves a plan's own fields.</summary>
    Task SaveAsync(ShoppingPlanSession session, CancellationToken cancellationToken);

    /// <summary>Atomically takes one AI batch. Returns false when the key was the last one used or no batch is left.</summary>
    Task<bool> TryTakeBatchAsync(Guid ownerUserId, Guid sessionId, Guid key, CancellationToken cancellationToken);

    /// <summary>Gives back a batch whose call failed, so a failure does not use up the plan's allowance.</summary>
    Task ReturnBatchAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Lists a plan's cards in the order they joined the deck.</summary>
    Task<IReadOnlyList<ShoppingPlanCard>> ListCardsAsync(Guid ownerUserId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Adds cards.</summary>
    Task AddCardsAsync(IReadOnlyList<ShoppingPlanCard> cards, CancellationToken cancellationToken);

    /// <summary>Saves one card.</summary>
    Task SaveCardAsync(ShoppingPlanCard card, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a decision only while the card is unseen. Returns false when another request already
    /// decided it, which makes repeating a decision a no-op.
    /// </summary>
    Task<bool> TryDecideCardAsync(ShoppingPlanCard card, CancellationToken cancellationToken);

    /// <summary>Deletes a plan's cards in the given states.</summary>
    Task DeleteCardsAsync(Guid ownerUserId, Guid sessionId, IReadOnlyCollection<ShoppingPlanCardState> states, CancellationToken cancellationToken);

    /// <summary>Deletes the owner's own abandoned plans older than the cutoff.</summary>
    Task DeleteAbandonedBeforeAsync(Guid ownerUserId, DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>Creates a shopping plan from the setup screen.</summary>
/// <param name="IdempotencyKey">Request key.</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="RequestLanguage">Interface language.</param>
/// <param name="Today">The owner's local day.</param>
/// <param name="Days">Days covered.</param>
/// <param name="MealTypes">Meals to plan.</param>
/// <param name="MealsOut">Meals eaten out.</param>
/// <param name="CookingStyle"><c>once</c>, <c>mix</c>, or <c>daily</c>.</param>
/// <param name="Priority"><c>waste</c>, <c>short</c>, <c>save</c>, or <c>time</c>.</param>
/// <param name="PrepDate">Session day; ignored for <c>daily</c>.</param>
/// <param name="PrepMinutes">Session length; ignored for <c>daily</c>.</param>
/// <param name="AnchorIngredient">Optional typed ingredient; untrusted.</param>
/// <param name="People">People to feed; the profile's default when null.</param>
/// <param name="FromMenuId">Menu of an accepted plan to repeat. Its answers then replace these, except <paramref name="Today"/>.</param>
public sealed record CreateShoppingPlanCommand(Guid? IdempotencyKey, string CorrelationId, string? RequestLanguage, DateOnly? Today, int Days, IReadOnlyList<string>? MealTypes, int MealsOut, string? CookingStyle, string? Priority, DateOnly? PrepDate, int? PrepMinutes, string? AnchorIngredient, int? People, Guid? FromMenuId = null);

/// <summary>A restriction the setup screen shows.</summary>
/// <param name="Code">Stable code.</param>
/// <param name="Category">Profile category.</param>
public sealed record ShoppingPlanRestrictionView(string Code, string Category);

/// <summary>A plan in progress, so entry points can offer to continue it.</summary>
/// <param name="SessionId">Plan.</param>
/// <param name="MenuId">Its draft menu.</param>
/// <param name="Status"><c>building</c> or <c>reviewing</c>.</param>
public sealed record ShoppingPlanCurrentView(Guid SessionId, Guid MenuId, string Status);

/// <summary>An accepted plan that can be repeated.</summary>
/// <param name="MenuId">Its menu, which the person may name to find it again.</param>
/// <param name="Name">The menu's name; empty until the person gives one.</param>
/// <param name="StartDate">First day of the menu.</param>
/// <param name="EndDate">Last day of the menu.</param>
/// <param name="MealsTotal">Meals the plan covered.</param>
/// <param name="Dishes">Dishes whose recipe still exists.</param>
public sealed record ShoppingPlanRecentView(Guid MenuId, string Name, DateOnly StartDate, DateOnly EndDate, int MealsTotal, IReadOnlyList<string> Dishes);

/// <summary>Defaults for the setup screen.</summary>
/// <param name="LastSetup">Answers of the latest plan, if any.</param>
/// <param name="People">The profile's default people.</param>
/// <param name="Restrictions">Confirmed restrictions, excluding plain preferences.</param>
/// <param name="UnverifiableRestrictions">Codes the guard cannot check, which the screen must say.</param>
/// <param name="HasFreezer">Whether the profile lists a freezer.</param>
/// <param name="Current">The plan in progress, if any.</param>
/// <param name="Recent">The newest accepted plans that still have a dish, at most five.</param>
public sealed record ShoppingPlanStartView(ShoppingPlanSetup? LastSetup, int People, IReadOnlyList<ShoppingPlanRestrictionView> Restrictions, IReadOnlyList<string> UnverifiableRestrictions, bool HasFreezer, ShoppingPlanCurrentView? Current, IReadOnlyList<ShoppingPlanRecentView> Recent);

/// <summary>How far the plan has come.</summary>
/// <param name="MealsTotal">Meals to fill.</param>
/// <param name="MealsFilled">Meals filled.</param>
/// <param name="ListItemCount">Estimated lines to buy.</param>
/// <param name="SessionMinutesUsed">Active minutes planned for the session.</param>
/// <param name="SessionMinutesBudget">Time for the session, if any.</param>
public sealed record ShoppingPlanProgressView(int MealsTotal, int MealsFilled, int ListItemCount, int SessionMinutesUsed, int? SessionMinutesBudget);

/// <summary>One ingredient on a card.</summary>
/// <param name="Name">Name.</param>
/// <param name="Quantity">Quantity for the card's servings.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
public sealed record ShoppingPlanCardIngredientView(string Name, decimal Quantity, string Unit, bool Optional);

/// <summary>A card to decide on. <paramref name="NewItemCount"/> is an estimate until the recipe is expanded.</summary>
/// <param name="CardId">Card.</param>
/// <param name="Source"><c>saved</c>, <c>idea</c>, or <c>ai</c>.</param>
/// <param name="Name">Dish name.</param>
/// <param name="Summary">Description.</param>
/// <param name="MealType">Target meal.</param>
/// <param name="Servings">Portions the ingredients are for.</param>
/// <param name="TotalMinutes">Total minutes.</param>
/// <param name="ActiveMinutes">Hands-on minutes.</param>
/// <param name="NewItemCount">Lines it would add.</param>
/// <param name="ReusedNames">At most three ingredients already on the list or at home.</param>
/// <param name="ReasonCode"><c>uses_attention_item</c>, <c>reuses_list_items</c>, or null.</param>
/// <param name="ReasonIngredient">Food behind <c>uses_attention_item</c>.</param>
/// <param name="OverBudget">Whether it passes the session's time.</param>
/// <param name="GuardCode">Restriction that could not be verified, if any.</param>
/// <param name="GuardIngredient">Ingredient that could not be verified, if any.</param>
/// <param name="LeadIngredient">Main ingredient, offered by "tired of".</param>
/// <param name="Ingredients">Ingredients.</param>
public sealed record ShoppingPlanCardView(Guid CardId, string Source, string Name, string Summary, string MealType, int Servings, int TotalMinutes, int ActiveMinutes, int NewItemCount, IReadOnlyList<string> ReusedNames, string? ReasonCode, string? ReasonIngredient, bool OverBudget, string? GuardCode, string? GuardIngredient, string? LeadIngredient, IReadOnlyList<ShoppingPlanCardIngredientView> Ingredients);

/// <summary>An accepted card.</summary>
/// <param name="CardId">Card.</param>
/// <param name="Name">Dish name.</param>
/// <param name="MealType">Meal.</param>
/// <param name="Repeats">Meals it fills.</param>
/// <param name="Mode"><c>yield</c>, <c>again</c>, or null.</param>
/// <param name="ExpansionState"><c>none</c>, <c>pending</c>, <c>ready</c>, or <c>failed</c>.</param>
/// <param name="FailureCode">Why an expansion failed, if it did.</param>
/// <param name="RecipeId">Recipe, once it exists.</param>
/// <param name="GuardCode">Restriction that could not be verified, if any.</param>
/// <param name="GuardIngredient">Ingredient that could not be verified, if any.</param>
public sealed record ShoppingPlanAcceptedView(Guid CardId, string Name, string MealType, int Repeats, string? Mode, string ExpansionState, string? FailureCode, Guid? RecipeId, string? GuardCode, string? GuardIngredient);

/// <summary>The state every plan route returns.</summary>
/// <param name="SessionId">Plan.</param>
/// <param name="MenuId">Its menu.</param>
/// <param name="Status"><c>building</c>, <c>reviewing</c>, <c>finished</c>, or <c>abandoned</c>.</param>
/// <param name="Setup">Setup answers.</param>
/// <param name="Progress">Progress.</param>
/// <param name="Queue">The next cards, best first, at most five.</param>
/// <param name="Accepted">Accepted cards.</param>
/// <param name="NeedsBatch">Whether the browser should ask for more AI cards.</param>
/// <param name="OfferMoreOptions">Whether to highlight "more options", after three straight rejections.</param>
/// <param name="Exhausted">Whether no card is left and no batch can be asked for.</param>
/// <param name="CanUndo">Whether undo has a decision to restore.</param>
public sealed record ShoppingPlanView(Guid SessionId, Guid MenuId, string Status, ShoppingPlanSetup Setup, ShoppingPlanProgressView Progress, IReadOnlyList<ShoppingPlanCardView> Queue, IReadOnlyList<ShoppingPlanAcceptedView> Accepted, bool NeedsBatch, bool OfferMoreOptions, bool Exhausted, bool CanUndo);

/// <summary>
/// Shopping plans (PLAN-0062): a setup screen and dish cards that fill a draft menu. Restrictions are
/// checked here on every card, and again on the expanded recipe, because model output is untrusted.
/// Nothing reserves pantry stock or reaches the home page until the menu is accepted, and answers never
/// update the profile.
/// </summary>
/// <remarks>Expansion is requested by the browser, one card at a time: the current user comes from the
/// request, and the app has no job queue yet (PLAN-0039).</remarks>
public sealed class ShoppingPlanApplicationService(
    ICurrentUserAccessor currentUser,
    IShoppingPlanStore store,
    IMenuPlanStore menus,
    MenuPlanningApplicationService planning,
    RecipePlanDeckService deck,
    IRecipeStore recipes,
    IRecipeLibraryStore library,
    IInventoryLotReadStore inventory,
    IIngredientMatching matching,
    IProfileReadStore profiles,
    TimeProvider timeProvider)
{
    private const int MaxPantryLots = 500;
    private const int MaxSavedCards = 12;
    private const int MaxIdeaCards = 6;
    private const int QueueSize = 5;
    private const int LowQueue = 3;
    private const int MoreOptionsAfterRejections = 3;
    private const int MaxRepeats = 4;
    private const int MaxEntryServings = 24;
    private const int AbandonedRetentionDays = 30;
    private const int MaxRecentPlans = 5;
    private const int MaxActiveRecipes = 200;
    private static readonly IReadOnlySet<string> MealTypes = MenuEntry.MealTypes;
    private static readonly HashSet<string> CostlyCategories = new(StringComparer.Ordinal) { "meat", "poultry", "fish", "seafood", "nut" };
    private static readonly HashSet<string> RejectReasons = new(StringComparer.Ordinal) { "dislike", "too_much_work", "tired_of" };
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("Cocinaris.ShoppingPlans", "1.0.0");
    private static readonly Counter<long> Started = Meter.CreateCounter<long>("cocinaris.shopping_plan.started");
    private static readonly Counter<long> AcceptedPlans = Meter.CreateCounter<long>("cocinaris.shopping_plan.accepted");
    private static readonly Counter<long> Decisions = Meter.CreateCounter<long>("cocinaris.shopping_plan.card_decision");
    private static readonly Counter<long> Batches = Meter.CreateCounter<long>("cocinaris.shopping_plan.batch");
    private static readonly Counter<long> GuardBlocked = Meter.CreateCounter<long>("cocinaris.shopping_plan.restriction_blocked");
    private static readonly Counter<long> GuardUnverified = Meter.CreateCounter<long>("cocinaris.shopping_plan.restriction_unverified");

    /// <summary>Reads the setup screen's defaults.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanStartView>> GetStartAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var profile = await profiles.FindAsync(user.Id, cancellationToken);
        var restrictions = Restrictions(profile);
        var current = await store.FindCurrentAsync(user.Id, cancellationToken);
        var latest = await store.FindLatestAsync(user.Id, cancellationToken);
        return RecipeApplicationResult<ShoppingPlanStartView>.Succeeded(new ShoppingPlanStartView(
            latest?.Setup,
            profile?.Profile.DefaultServingCount is { } people ? Math.Clamp(people, 1, ShoppingPlanSetup.MaxPeople) : 2,
            restrictions.Where(item => item.Category != nameof(PreferenceCategory.Dislike)).Select(item => new ShoppingPlanRestrictionView(item.Code, item.Category)).ToList(),
            RestrictionGuard.Default.UnverifiableCodes(restrictions),
            profile?.Equipment.Any(item => !item.IsRemoved && item.StableCode.Value == "freezer") == true,
            current is null ? null : new ShoppingPlanCurrentView(current.Id, current.MenuPlanId, Name(current.Status)),
            await ListRecentAsync(user.Id, cancellationToken)));
    }

    private async Task<IReadOnlyList<ShoppingPlanRecentView>> ListRecentAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var finished = await store.ListFinishedAsync(ownerUserId, MaxRecentPlans, cancellationToken);
        if (finished.Count == 0)
        {
            return [];
        }

        var active = (await recipes.ListActiveAsync(ownerUserId, MaxActiveRecipes, cancellationToken)).ToDictionary(item => item.RecipeId);
        var entries = await menus.ListEntriesAsync(ownerUserId, finished.Select(item => item.MenuPlanId).ToList(), cancellationToken);
        var recent = new List<ShoppingPlanRecentView>();
        foreach (var session in finished)
        {
            var dishes = entries.Where(item => item.MenuPlanId == session.MenuPlanId && item.Status != MenuEntryStatus.Skipped && active.ContainsKey(item.RecipeId))
                .Select(item => active[item.RecipeId].Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (dishes.Count > 0 && await menus.FindAsync(ownerUserId, session.MenuPlanId, cancellationToken) is { } menu)
            {
                recent.Add(new ShoppingPlanRecentView(menu.Id, menu.Name, menu.StartDate, menu.EndDate, session.Setup.TotalMeals, dishes));
            }
        }

        return recent;
    }

    /// <summary>Creates the draft menu and the plan, then adds the cards that need no AI.</summary>
    /// <param name="command">Setup answers.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> CreateAsync(CreateShoppingPlanCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<ShoppingPlanView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (await store.FindByCreationKeyAsync(user.Id, key, cancellationToken) is { } replay)
        {
            return await ResultAsync(user.Id, replay, cancellationToken);
        }

        ShoppingPlanSession? source = null;
        if (command.FromMenuId is { } fromMenuId)
        {
            source = await store.FindFinishedByMenuAsync(user.Id, fromMenuId, cancellationToken);
            if (source is null)
            {
                return NotFound<ShoppingPlanView>();
            }

            // The answers come from the plan being repeated. Its dates have passed, so the new plan starts today.
            var old = source.Setup;
            command = command with { Days = old.Days, MealTypes = old.MealTypes, MealsOut = old.MealsOut, CookingStyle = old.CookingStyle, Priority = old.Priority, PrepDate = null, PrepMinutes = old.PrepMinutes, AnchorIngredient = old.AnchorIngredient, People = old.People };
        }

        var profile = await profiles.FindAsync(user.Id, cancellationToken);
        var defaultPeople = profile?.Profile.DefaultServingCount is { } count ? Math.Clamp(count, 1, ShoppingPlanSetup.MaxPeople) : 2;
        if (ValidateSetup(command, defaultPeople) is not { } setup)
        {
            return Failure<ShoppingPlanView>("validation_failed", "The plan's answers are invalid.");
        }

        if (await store.FindCurrentAsync(user.Id, cancellationToken) is { } current)
        {
            return InProgress(current);
        }

        var now = timeProvider.GetUtcNow();
        await store.DeleteAbandonedBeforeAsync(user.Id, now.AddDays(-AbandonedRetentionDays), cancellationToken);
        var menu = MenuPlan.Create(user.Id, DerivedKey(key, "menu"), string.Empty, setup.StartDate, setup.EndDate, now);
        if (!await menus.TryAddAsync(menu, cancellationToken))
        {
            menu = await menus.FindByCreationKeyAsync(user.Id, DerivedKey(key, "menu"), cancellationToken)
                ?? throw new InvalidOperationException("A menu key conflicted but the menu could not be reloaded.");
        }

        var session = new ShoppingPlanSession
        {
            Id = Guid.NewGuid(),
            OwnerUserId = user.Id,
            MenuPlanId = menu.Id,
            CreationKey = key,
            Setup = setup,
            PrivateDirection = setup.AnchorIngredient is not null,
            CreatedAt = now,
            UpdatedAt = now
        };
        if (!await store.TryAddAsync(session, cancellationToken))
        {
            // Lost a race: either the same request retried, or another plan began. The draft menu made
            // for this attempt must not linger in the person's list.
            if (await store.FindByCreationKeyAsync(user.Id, key, cancellationToken) is { } raced)
            {
                return await ResultAsync(user.Id, raced, cancellationToken);
            }

            menu.Archive(now);
            await menus.SaveAsync(menu, cancellationToken);
            return await store.FindCurrentAsync(user.Id, cancellationToken) is { } other
                ? InProgress(other)
                : Failure<ShoppingPlanView>("domain_rule_violated", "The plan could not be created.");
        }

        Started.Add(1);
        var repeated = source is null ? [] : await AddRepeatedCardsAsync(user.Id, session, source.MenuPlanId, profile, cancellationToken);
        await AddInstantCardsAsync(user.Id, session, repeated, profile, command.RequestLanguage, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken, RecipeApplicationSuccess.Created);
    }

    /// <summary>Gets one plan.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        return session is null ? NotFound<ShoppingPlanView>() : await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Asks the model for more cards. With a repeated key or no batch left, it returns the plan as it is.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="key">Request key.</param>
    /// <param name="dishFamilies">"More options" families; closed codes.</param>
    /// <param name="cuisine">"More options" cuisine; closed code.</param>
    /// <param name="time">"More options" time; closed code.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> AddBatchAsync(Guid sessionId, Guid? key, IReadOnlyList<string>? dishFamilies, string? cuisine, string? time, string correlationId, string? language, CancellationToken cancellationToken)
    {
        if (key is not Guid batchKey)
        {
            return Failure<ShoppingPlanView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var guidance = dishFamilies is { Count: > 0 } || cuisine is not null || time is not null
            ? new RecipeSuggestionGuidance(null, dishFamilies ?? [], cuisine, time, [], [])
            : null;
        if (guidance?.Validate() is { Count: > 0 } invalid)
        {
            return RecipeApplicationResult<ShoppingPlanView>.Failure("validation_failed", "The options are invalid.", invalid);
        }

        var existing = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        if (existing.Count >= ShoppingPlanSession.MaxCards || !await store.TryTakeBatchAsync(user.Id, session.Id, batchKey, cancellationToken))
        {
            return await ResultAsync(user.Id, session, cancellationToken);
        }

        session.BatchCount++;
        var context = await LoadRankContextAsync(user.Id, session, existing, cancellationToken);
        var generated = await deck.GenerateBatchAsync(user.Id, BuildRequest(session, existing, context, guidance, language), correlationId, cancellationToken);
        if (generated.Problem is { } problem)
        {
            await store.ReturnBatchAsync(user.Id, session.Id, cancellationToken);
            Batches.Add(1, new KeyValuePair<string, object?>("source", "ai"), new KeyValuePair<string, object?>("outcome", problem.ErrorCode));
            return RecipeApplicationResult<ShoppingPlanView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
        }

        Batches.Add(1, new KeyValuePair<string, object?>("source", "ai"), new KeyValuePair<string, object?>("outcome", "succeeded"));
        await AddCandidatesAsync(user.Id, session, existing, generated.Value!, ShoppingPlanCardSource.Ai, await profiles.FindAsync(user.Id, cancellationToken), cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Accepts or rejects a card. Repeating a decision is a no-op.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="decision"><c>accept</c> or <c>reject</c>.</param>
    /// <param name="mode"><c>yield</c>, <c>again</c>, or null for one meal.</param>
    /// <param name="repeats">Meals to fill, 1 to 4.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> DecideAsync(Guid sessionId, Guid cardId, string? decision, string? mode, int? repeats, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!IsOpen(session!))
        {
            return NotOpen<ShoppingPlanView>();
        }

        if (decision is not ("accept" or "reject"))
        {
            return Failure<ShoppingPlanView>("validation_failed", "The decision must be accept or reject.", "decision");
        }

        var count = repeats ?? 1;
        if (decision == "accept")
        {
            if (count is < 1 or > MaxRepeats || (count > 1 && mode is not ("yield" or "again")))
            {
                return Failure<ShoppingPlanView>("validation_failed", "Choose one to four meals, and how a repeat is cooked.", "repeats");
            }

            if (mode == "yield" && session!.Setup.People * count > MaxEntryServings)
            {
                return Failure<ShoppingPlanView>("validation_failed", "That is too many portions for one cooking.", "repeats");
            }
        }

        if (card!.State != ShoppingPlanCardState.Unseen)
        {
            return await ResultAsync(user.Id, session!, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        card.DecidedAt = now;
        if (decision == "reject")
        {
            card.State = ShoppingPlanCardState.Rejected;
        }
        else
        {
            card.State = ShoppingPlanCardState.Accepted;
            card.Repeats = count;
            card.AcceptMode = count > 1 ? mode : null;
            card.ExpansionState = card.RecipeId is null ? ShoppingPlanExpansionState.Pending : ShoppingPlanExpansionState.Ready;
        }

        if (!await store.TryDecideCardAsync(card, cancellationToken))
        {
            return await ResultAsync(user.Id, session!, cancellationToken);
        }

        Decisions.Add(1, new KeyValuePair<string, object?>("decision", decision), new KeyValuePair<string, object?>("reason", "none"));
        session!.ConsecutiveRejections = decision == "reject" ? session.ConsecutiveRejections + 1 : 0;
        session.LastDecidedCardId = card.Id;
        session.UpdatedAt = now;
        await store.SaveAsync(session, cancellationToken);
        if (card.State == ShoppingPlanCardState.Accepted && card.RecipeId is { } recipeId)
        {
            await CreateEntriesAsync(user.Id, session, card, recipeId, cancellationToken);
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Records why a card was refused. The reason affects only this plan's ranking and never the profile.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="reason"><c>dislike</c>, <c>too_much_work</c>, or <c>tired_of</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetReasonAsync(Guid sessionId, Guid cardId, string? reason, CancellationToken cancellationToken)
    {
        if (reason is null || !RejectReasons.Contains(reason))
        {
            return Failure<ShoppingPlanView>("validation_failed", "The reason is not known.", "reason");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (card!.State != ShoppingPlanCardState.Rejected)
        {
            return Failure<ShoppingPlanView>("domain_rule_violated", "Only a refused dish can have a reason.");
        }

        var keys = await matching.KeysAsync(user.Id, card.Snapshot.Ingredients.Select(item => item.Name), cancellationToken);
        card.RejectReason = reason;
        card.RejectIngredientKey = reason == "tired_of" ? ToRankCard(card, session!.Setup, keys, []).LeadKey : null;
        await store.SaveCardAsync(card, cancellationToken);
        Decisions.Add(1, new KeyValuePair<string, object?>("decision", "reject"), new KeyValuePair<string, object?>("reason", reason));
        return await ResultAsync(user.Id, session!, cancellationToken);
    }

    /// <summary>Undoes the last decision, once. A recipe already created stays in the library.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> UndoAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var card = (await store.ListCardsAsync(user.Id, session.Id, cancellationToken)).FirstOrDefault(item => item.Id == session.LastDecidedCardId);
        if (card is null)
        {
            return await ResultAsync(user.Id, session, cancellationToken);
        }

        if (card.State == ShoppingPlanCardState.Accepted)
        {
            await RemoveCardEntriesAsync(user.Id, session, card, cancellationToken);
        }
        else if (session.ConsecutiveRejections > 0)
        {
            session.ConsecutiveRejections--;
        }

        card.State = ShoppingPlanCardState.Unseen;
        card.RejectReason = null;
        card.RejectIngredientKey = null;
        card.AcceptMode = null;
        card.Repeats = 1;
        card.ExpansionState = ShoppingPlanExpansionState.None;
        card.DecidedAt = null;
        await store.SaveCardAsync(card, cancellationToken);
        session.LastDecidedCardId = null;
        session.Status = ShoppingPlanStatus.Building;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>
    /// Expands an accepted card and puts its meals on the menu. Every step is idempotent, so the browser
    /// may retry. If the card was undone meanwhile, the recipe is kept and no meal is created.
    /// </summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> ExpandAsync(Guid sessionId, Guid cardId, string correlationId, string? language, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!IsOpen(session!))
        {
            return NotOpen<ShoppingPlanView>();
        }

        if (card!.State != ShoppingPlanCardState.Accepted || card.CandidateJson is null || card.ExpansionState == ShoppingPlanExpansionState.Ready)
        {
            return await ResultAsync(user.Id, session!, cancellationToken);
        }

        if (card.RecipeId is null)
        {
            var candidate = JsonSerializer.Deserialize<SuggestCandidate>(card.CandidateJson, SerializerOptions)!;
            var hint = session!.Setup.AnchorIngredient;
            var key = AttemptKey(card);
            var expanded = await deck.ExpandCardAsync(user.Id, candidate, key, hint, correlationId, language, cancellationToken);
            if (expanded.Problem?.ErrorCode == "precondition_failed")
            {
                // A generation session lasts two hours; someone resuming later gets a fresh one.
                card.Snapshot = card.Snapshot with { ExpansionAttempt = card.Snapshot.ExpansionAttempt + 1 };
                await store.SaveCardAsync(card, cancellationToken);
                expanded = await deck.ExpandCardAsync(user.Id, candidate, AttemptKey(card), hint, correlationId, language, cancellationToken);
            }

            if (expanded.Problem is { } problem)
            {
                // A conflict means the person has other AI work running: the card stays pending for a retry.
                if (problem.ErrorCode != "ai_operation_conflict")
                {
                    await FailCardAsync(card, problem.ErrorCode, cancellationToken);
                }

                return RecipeApplicationResult<ShoppingPlanView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
            }

            var detail = expanded.Value!;
            var ingredients = RecipeIngredientReader.Read(detail.NormalizedRecipeJson).ToList();
            var keys = await matching.KeysAsync(user.Id, ingredients.Select(item => item.Name), cancellationToken);
            var profile = await profiles.FindAsync(user.Id, cancellationToken);
            var guard = RestrictionGuard.Default.Check(ingredients.Select(item => item.Name), keys, Restrictions(profile));
            card.RecipeId = detail.RecipeId;
            // From here the card shows the real recipe, which also drives the real shopping list.
            card.Snapshot = card.Snapshot with
            {
                Servings = detail.Servings,
                Ingredients = ingredients.Select(ToCardIngredient).ToList(),
                GuardCode = guard.Status == RestrictionGuardStatus.Unverified ? guard.Code : null,
                GuardIngredient = guard.Status == RestrictionGuardStatus.Unverified ? guard.Ingredient : null
            };
            if (guard.Status == RestrictionGuardStatus.Blocked)
            {
                GuardBlocked.Add(1, new KeyValuePair<string, object?>("stage", "expansion"));
                await FailCardAsync(card, "restriction_blocked", cancellationToken);
                return await ResultAsync(user.Id, session, cancellationToken);
            }
        }

        // The person may have undone this card while the model was working.
        var latest = (await store.ListCardsAsync(user.Id, session!.Id, cancellationToken)).FirstOrDefault(item => item.Id == card.Id);
        if (latest?.State != ShoppingPlanCardState.Accepted)
        {
            if (latest is not null)
            {
                latest.RecipeId = card.RecipeId;
                latest.Snapshot = card.Snapshot;
                await store.SaveCardAsync(latest, cancellationToken);
            }

            return await ResultAsync(user.Id, session, cancellationToken);
        }

        card.ExpansionState = ShoppingPlanExpansionState.Ready;
        card.Snapshot = card.Snapshot with { FailureCode = null };
        await store.SaveCardAsync(card, cancellationToken);
        await CreateEntriesAsync(user.Id, session, card, card.RecipeId!.Value, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Removes an accepted card that has no meal, such as one whose expansion failed.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> RemoveCardAsync(Guid sessionId, Guid cardId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (!IsOpen(session!))
        {
            return NotOpen<ShoppingPlanView>();
        }

        if (card!.State == ShoppingPlanCardState.Accepted)
        {
            await RemoveCardEntriesAsync(user.Id, session!, card, cancellationToken);
            card.State = ShoppingPlanCardState.Rejected;
            card.ExpansionState = ShoppingPlanExpansionState.None;
            await store.SaveCardAsync(card, cancellationToken);
        }

        return await ResultAsync(user.Id, session!, cancellationToken);
    }

    /// <summary>Moves to review and computes the suggested order. Refused while an expansion is pending.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> ReviewAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var cards = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        if (cards.Any(card => card.State == ShoppingPlanCardState.Accepted && card.ExpansionState == ShoppingPlanExpansionState.Pending))
        {
            return Failure<ShoppingPlanView>("expansions_pending", "Some recipes are still being prepared.");
        }

        await SequenceAsync(user.Id, session, cards, cancellationToken);
        session.Status = ShoppingPlanStatus.Reviewing;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Removes one meal from the plan so the person can choose another, back on the cards.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="entryId">Menu entry to replace.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> ReplaceAsync(Guid sessionId, Guid? entryId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var entry = (await menus.ListEntriesAsync(user.Id, [session.MenuPlanId], cancellationToken)).FirstOrDefault(item => item.Id == entryId);
        var plan = await menus.FindAsync(user.Id, session.MenuPlanId, cancellationToken);
        if (entry is null || plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        plan.Touch(timeProvider.GetUtcNow());
        await menus.RemoveEntryAsync(plan, entry, cancellationToken);
        var card = (await store.ListCardsAsync(user.Id, session.Id, cancellationToken)).FirstOrDefault(item => item.State == ShoppingPlanCardState.Accepted && item.RecipeId == entry.RecipeId);
        if (card is not null)
        {
            if (card.AcceptMode == "again" && card.Repeats > 1)
            {
                card.Repeats--;
            }
            else
            {
                card.State = ShoppingPlanCardState.Rejected;
                card.ExpansionState = ShoppingPlanExpansionState.None;
            }

            await store.SaveCardAsync(card, cancellationToken);
        }

        session.Status = ShoppingPlanStatus.Building;
        session.LastDecidedCardId = null;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Accepts the menu: only now do meals reach the home page and reserve the pantry. Unused cards are deleted.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> AcceptAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (session.Status == ShoppingPlanStatus.Finished)
        {
            return await ResultAsync(user.Id, session, cancellationToken);
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var cards = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        if (cards.Any(card => card.State == ShoppingPlanCardState.Accepted && card.ExpansionState == ShoppingPlanExpansionState.Pending))
        {
            return Failure<ShoppingPlanView>("expansions_pending", "Some recipes are still being prepared.");
        }

        if ((await menus.ListEntriesAsync(user.Id, [session.MenuPlanId], cancellationToken)).Count == 0)
        {
            return Failure<ShoppingPlanView>("domain_rule_violated", "Choose at least one dish first.");
        }

        await SequenceAsync(user.Id, session, cards, cancellationToken);
        var accepted = await planning.AcceptAsync(session.MenuPlanId, cancellationToken);
        if (accepted.Problem is { } problem)
        {
            return RecipeApplicationResult<ShoppingPlanView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
        }

        await store.DeleteCardsAsync(user.Id, session.Id, [ShoppingPlanCardState.Unseen, ShoppingPlanCardState.Rejected], cancellationToken);
        session.Status = ShoppingPlanStatus.Finished;
        session.LastDecidedCardId = null;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        AcceptedPlans.Add(1);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Discards the plan: the menu is archived and the cards deleted. Recipes already created stay in the library.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> AbandonAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (session.Status == ShoppingPlanStatus.Abandoned)
        {
            return await ResultAsync(user.Id, session, cancellationToken);
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        await planning.ArchiveAsync(session.MenuPlanId, cancellationToken);
        await store.DeleteCardsAsync(user.Id, session.Id, [ShoppingPlanCardState.Unseen, ShoppingPlanCardState.Accepted, ShoppingPlanCardState.Rejected], cancellationToken);
        session.Status = ShoppingPlanStatus.Abandoned;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Validates the setup answers; null when any is invalid.</summary>
    /// <param name="command">Answers.</param>
    /// <param name="defaultPeople">People when none were given.</param>
    public static ShoppingPlanSetup? ValidateSetup(CreateShoppingPlanCommand command, int defaultPeople)
    {
        var meals = command.MealTypes?.Distinct(StringComparer.Ordinal).ToList() ?? [];
        var people = command.People ?? defaultPeople;
        if (command.Today is not { } today || command.Days is < 1 or > ShoppingPlanSetup.MaxDays || meals.Count == 0 || meals.Any(meal => !MealTypes.Contains(meal))
            || command.CookingStyle is not { } style || !ShoppingPlanSetup.CookingStyles.Contains(style)
            || command.Priority is not { } priority || !ShoppingPlanSetup.Priorities.Contains(priority)
            || people is < 1 or > ShoppingPlanSetup.MaxPeople)
        {
            return null;
        }

        var total = command.Days * meals.Count;
        if (command.MealsOut < 0 || command.MealsOut >= total || total - command.MealsOut > ShoppingPlanSetup.MaxMeals)
        {
            return null;
        }

        DateOnly? prepDate = null;
        int? prepMinutes = null;
        if (style != "daily")
        {
            prepDate = command.PrepDate ?? today;
            prepMinutes = command.PrepMinutes ?? 120;
            if (prepDate < today || prepDate > today.AddDays(ShoppingPlanSetup.MaxPrepDaysAhead) || !ShoppingPlanSetup.PrepLengths.Contains(prepMinutes.Value))
            {
                return null;
            }
        }

        return new ShoppingPlanSetup(today, command.Days, meals, command.MealsOut, style, priority, prepDate, prepMinutes, RecipePlanDeckService.CleanAnchor(command.AnchorIngredient), people);
    }

    /// <summary>
    /// Repeats a plan's dishes as accepted cards, with their meals on the new draft menu. It reads the
    /// old menu rather than the old cards, because the person may have changed the menu after accepting
    /// it. Deleted recipes are left out, and the guard runs again, because restrictions may have changed.
    /// </summary>
    private async Task<IReadOnlyList<ShoppingPlanCard>> AddRepeatedCardsAsync(Guid ownerUserId, ShoppingPlanSession session, Guid sourceMenuId, ProfileReadModel? profile, CancellationToken cancellationToken)
    {
        var setup = session.Setup;
        var active = (await recipes.ListActiveAsync(ownerUserId, MaxActiveRecipes, cancellationToken)).ToDictionary(item => item.RecipeId);
        var entries = (await menus.ListEntriesAsync(ownerUserId, [sourceMenuId], cancellationToken))
            .Where(item => item.Status != MenuEntryStatus.Skipped && active.ContainsKey(item.RecipeId) && item.MealType is not null && setup.MealTypes.Contains(item.MealType))
            .OrderBy(item => item.SuggestedOrder ?? int.MaxValue)
            .ThenBy(item => item.CreatedAt)
            .ToList();
        var ingredients = entries.Select(item => item.RecipeId).Distinct().ToDictionary(id => id, id => RecipeIngredientReader.Read(active[id].NormalizedRecipeJson).ToList());
        var keys = await matching.KeysAsync(ownerUserId, ingredients.Values.SelectMany(items => items.Select(item => item.Name)), cancellationToken);
        var restrictions = Restrictions(profile);
        var needs = new Dictionary<string, int>(setup.MealNeeds(), StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();
        var cards = new List<ShoppingPlanCard>();
        var repeatedDishes = entries.Where(item => item.MealsCovered == 1).GroupBy(item => (item.RecipeId, item.MealType!))
            .Select(group => (Entry: group.First(), Mode: group.Count() > 1 ? "again" : null, Meals: group.Count()));
        var yieldingDishes = entries.Where(item => item.MealsCovered > 1).Select(item => (Entry: item, Mode: (string?)"yield", Meals: item.MealsCovered));
        foreach (var (entry, mode, meals) in yieldingDishes.Concat(repeatedDishes).OrderBy(item => entries.IndexOf(item.Entry)))
        {
            var detail = active[entry.RecipeId];
            var guard = RestrictionGuard.Default.Check(ingredients[entry.RecipeId].Select(item => item.Name), keys, restrictions);
            if (!CountGuard(guard))
            {
                continue;
            }

            // The old menu may hold more than the plan's answers allow, after later edits by hand.
            var group = ShoppingPlanSetup.GroupOf(entry.MealType!);
            var count = Math.Min(Math.Min(meals, MaxRepeats), needs.GetValueOrDefault(group));
            if (mode == "yield")
            {
                count = Math.Min(count, MaxEntryServings / setup.People);
            }

            if (count < 1)
            {
                continue;
            }

            needs[group] -= count;
            cards.Add(new ShoppingPlanCard
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OwnerUserId = ownerUserId,
                Source = ShoppingPlanCardSource.Saved,
                RecipeId = entry.RecipeId,
                ExpansionKey = Guid.NewGuid(),
                FirstSeenOrder = cards.Count,
                Snapshot = SavedSnapshot(detail, entry.MealType!, ingredients[entry.RecipeId], guard),
                State = ShoppingPlanCardState.Accepted,
                AcceptMode = count > 1 ? mode : null,
                Repeats = count,
                ExpansionState = ShoppingPlanExpansionState.Ready,
                // Ranking reads accepted cards in decision order, so each needs its own instant.
                DecidedAt = now.AddMilliseconds(cards.Count)
            });
        }

        if (cards.Count > 0)
        {
            await store.AddCardsAsync(cards, cancellationToken);
            foreach (var card in cards)
            {
                await CreateEntriesAsync(ownerUserId, session, card, card.RecipeId!.Value, cancellationToken);
            }
        }

        return cards;
    }

    private static ShoppingPlanCardSnapshot SavedSnapshot(RecipeDetail detail, string meal, IReadOnlyList<RecipeIngredient> ingredients, RestrictionGuardResult guard)
    {
        var (activeMinutes, total) = Minutes(detail.NormalizedRecipeJson);
        var id = detail.RecipeId.ToString("n");
        return new ShoppingPlanCardSnapshot(detail.Name, string.Empty, meal, id, id, [], detail.Servings, activeMinutes, total, "medium", ingredients.Select(ToCardIngredient).ToList(), Unverified(guard)?.Code, Unverified(guard)?.Ingredient);
    }

    private async Task AddInstantCardsAsync(Guid ownerUserId, ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> repeated, ProfileReadModel? profile, string? language, CancellationToken cancellationToken)
    {
        var restrictions = Restrictions(profile);
        var active = (await recipes.ListActiveAsync(ownerUserId, MaxActiveRecipes, cancellationToken)).ToDictionary(item => item.RecipeId);
        var chosen = repeated.Select(card => card.RecipeId).ToHashSet();
        var items = (await library.ListAsync(ownerUserId, new RecipeLibraryQuery(false, false, null), cancellationToken))
            .Where(item => active.ContainsKey(item.RecipeId) && !chosen.Contains(item.RecipeId))
            .OrderByDescending(item => item.IsFavorite)
            .ThenByDescending(item => item.LastCookedAt)
            .ToList();
        var names = items.SelectMany(item => RecipeIngredientReader.Read(active[item.RecipeId].NormalizedRecipeJson).Select(ingredient => ingredient.Name)).ToList();
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var cards = new List<ShoppingPlanCard>();
        foreach (var item in items)
        {
            if (cards.Count == MaxSavedCards)
            {
                break;
            }

            var meal = item.MealTypes.FirstOrDefault(session.Setup.MealTypes.Contains);
            if (meal is null)
            {
                continue;
            }

            var detail = active[item.RecipeId];
            var ingredients = RecipeIngredientReader.Read(detail.NormalizedRecipeJson).ToList();
            var guard = RestrictionGuard.Default.Check(ingredients.Select(ingredient => ingredient.Name), keys, restrictions);
            if (!CountGuard(guard))
            {
                continue;
            }

            cards.Add(new ShoppingPlanCard
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OwnerUserId = ownerUserId,
                Source = ShoppingPlanCardSource.Saved,
                RecipeId = item.RecipeId,
                ExpansionKey = Guid.NewGuid(),
                FirstSeenOrder = repeated.Count + cards.Count,
                Snapshot = SavedSnapshot(detail, meal, ingredients, guard)
            });
        }

        if (cards.Count > 0)
        {
            await store.AddCardsAsync(cards, cancellationToken);
        }

        var existing = await store.ListCardsAsync(ownerUserId, session.Id, cancellationToken);
        var context = await LoadRankContextAsync(ownerUserId, session, existing, cancellationToken);
        var ideas = await deck.FindIdeasAsync(ownerUserId, BuildRequest(session, existing, context, null, language), MaxIdeaCards, cancellationToken);
        Batches.Add(1, new KeyValuePair<string, object?>("source", "instant"), new KeyValuePair<string, object?>("outcome", "succeeded"));
        await AddCandidatesAsync(ownerUserId, session, existing, ideas, ShoppingPlanCardSource.Idea, profile, cancellationToken);
    }

    // Cards are checked before anyone sees them: a blocked dish is dropped, whatever the model said.
    private async Task AddCandidatesAsync(Guid ownerUserId, ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> existing, IReadOnlyList<SuggestCandidate> candidates, ShoppingPlanCardSource source, ProfileReadModel? profile, CancellationToken cancellationToken)
    {
        var restrictions = Restrictions(profile);
        var seenNames = existing.Select(card => IngredientText.Normalize(card.Snapshot.Name)).ToHashSet(StringComparer.Ordinal);
        var seenPrints = existing.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
        var names = candidates.SelectMany(IngredientNames).ToList();
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var cards = new List<ShoppingPlanCard>();
        foreach (var candidate in candidates)
        {
            if (existing.Count + cards.Count >= ShoppingPlanSession.MaxCards)
            {
                break;
            }

            if (!session.Setup.MealTypes.Contains(candidate.TargetMealType)
                || !seenNames.Add(IngredientText.Normalize(candidate.Name))
                || !seenPrints.Add(RecipeSuggestSemantics.Fingerprint(candidate)))
            {
                continue;
            }

            var guard = RestrictionGuard.Default.Check(IngredientNames(candidate), keys, restrictions);
            if (!CountGuard(guard))
            {
                continue;
            }

            cards.Add(new ShoppingPlanCard
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OwnerUserId = ownerUserId,
                Source = source,
                CandidateJson = JsonSerializer.Serialize(candidate, SerializerOptions),
                ExpansionKey = Guid.NewGuid(),
                FirstSeenOrder = existing.Count + cards.Count,
                Snapshot = new ShoppingPlanCardSnapshot(
                    candidate.Name, candidate.Summary, candidate.TargetMealType, candidate.DishFormat, candidate.PrimaryTechnique, candidate.PrimaryIngredientRefs, candidate.Servings,
                    candidate.Time.ActiveMinutes, candidate.Time.TotalMinutes, candidate.Difficulty, CardIngredients(candidate), Unverified(guard)?.Code, Unverified(guard)?.Ingredient)
            });
        }

        if (cards.Count > 0)
        {
            await store.AddCardsAsync(cards, cancellationToken);
        }
    }

    private static bool CountGuard(RestrictionGuardResult guard)
    {
        if (guard.Status == RestrictionGuardStatus.Blocked)
        {
            GuardBlocked.Add(1, new KeyValuePair<string, object?>("stage", "card"));
            return false;
        }

        if (guard.Status == RestrictionGuardStatus.Unverified)
        {
            GuardUnverified.Add(1);
        }

        return true;
    }

    private static RestrictionGuardResult? Unverified(RestrictionGuardResult guard) => guard.Status == RestrictionGuardStatus.Unverified ? guard : null;

    private static IEnumerable<string> IngredientNames(SuggestCandidate candidate) =>
        candidate.InventoryUses.Select(use => use.UserName).Concat(candidate.AdditionalIngredients.Select(item => item.Name)).Concat(candidate.AssumptionsUsed);

    private static IReadOnlyList<ShoppingPlanCardIngredient> CardIngredients(SuggestCandidate candidate) =>
        candidate.InventoryUses.Select(use => new ShoppingPlanCardIngredient(use.UserName, use.RequiredQuantity, RecipeIngredientReader.ToLotUnit(use.Unit), false, false))
            .Concat(candidate.AdditionalIngredients.Select(item => new ShoppingPlanCardIngredient(item.Name, item.RequiredQuantity, RecipeIngredientReader.ToLotUnit(item.Unit), item.Optional, false)))
            .ToList();

    private static ShoppingPlanCardIngredient ToCardIngredient(RecipeIngredient ingredient) =>
        new(ingredient.Name, ingredient.Quantity, RecipeIngredientReader.ToLotUnit(ingredient.Unit), ingredient.Optional, ingredient.SourceType == "assumption");

    private static string Fingerprint(ShoppingPlanCard card) =>
        string.Join('|', new[] { card.Snapshot.DishFormat, card.Snapshot.Technique }.Concat(card.Snapshot.PrimaryRefs));

    private static (int Active, int Total) Minutes(string normalizedRecipeJson)
    {
        try
        {
            using var document = JsonDocument.Parse(normalizedRecipeJson);
            var root = document.RootElement;
            if (root.TryGetProperty("recipe", out var recipe))
            {
                root = recipe;
            }

            if (!root.TryGetProperty("stages", out var stages) || stages.ValueKind != JsonValueKind.Array)
            {
                return (0, 0);
            }

            var active = 0;
            var passive = 0;
            foreach (var stage in stages.EnumerateArray())
            {
                active += stage.TryGetProperty("activeMinutes", out var value) && value.TryGetInt32(out var minutes) ? minutes : 0;
                passive += stage.TryGetProperty("passiveMinutes", out var other) && other.TryGetInt32(out var more) ? more : 0;
            }

            return (active, active + passive);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }

    private static IReadOnlyList<RestrictionCode> Restrictions(ProfileReadModel? profile) =>
        profile?.Preferences
            .Where(item => item.Presence == ProfileFieldPresence.Confirmed && item.Category != PreferenceCategory.Preference)
            .Select(item => new RestrictionCode(item.StableCode.Value, item.Category.ToString()))
            .ToList() ?? [];

    private async Task CreateEntriesAsync(Guid ownerUserId, ShoppingPlanSession session, ShoppingPlanCard card, Guid recipeId, CancellationToken cancellationToken)
    {
        var plan = await menus.FindAsync(ownerUserId, session.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return;
        }

        var setup = session.Setup;
        var yields = card.AcceptMode == "yield";
        var count = card.AcceptMode == "again" ? card.Repeats : 1;
        var plannedDate = yields && setup.CookingStyle != "daily" ? setup.PrepDate : null;
        for (var index = 0; index < count; index++)
        {
            var key = EntryKey(card, index);
            if (await menus.FindEntryByCreationKeyAsync(ownerUserId, key, cancellationToken) is not null)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var entry = MenuEntry.Create(plan, key, recipeId, null, plannedDate, card.Snapshot.MealType, yields ? setup.People * card.Repeats : setup.People, now, yields ? card.Repeats : 1);
            plan.Touch(now);
            await menus.TryAddEntryAsync(plan, entry, cancellationToken);
        }
    }

    private async Task RemoveCardEntriesAsync(Guid ownerUserId, ShoppingPlanSession session, ShoppingPlanCard card, CancellationToken cancellationToken)
    {
        var plan = await menus.FindAsync(ownerUserId, session.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return;
        }

        for (var index = 0; index < MaxRepeats; index++)
        {
            if (await menus.FindEntryByCreationKeyAsync(ownerUserId, EntryKey(card, index), cancellationToken) is { } entry)
            {
                plan.Touch(timeProvider.GetUtcNow());
                await menus.RemoveEntryAsync(plan, entry, cancellationToken);
            }
        }
    }

    private async Task SequenceAsync(Guid ownerUserId, ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, CancellationToken cancellationToken)
    {
        var entries = await menus.ListEntriesAsync(ownerUserId, [session.MenuPlanId], cancellationToken);
        if (entries.Count == 0)
        {
            return;
        }

        var byRecipe = cards.Where(card => card.RecipeId is not null && card.State == ShoppingPlanCardState.Accepted).GroupBy(card => card.RecipeId!.Value).ToDictionary(group => group.Key, group => group.First());
        var lots = await inventory.ListActiveAsync(ownerUserId, MaxPantryLots, cancellationToken);
        var names = byRecipe.Values.SelectMany(card => card.Snapshot.Ingredients.Select(item => item.Name)).Concat(lots.Select(lot => lot.ProductName)).ToList();
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var frozen = lots.Where(lot => lot.StorageLocation == "Freezer" && lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null)
            .GroupBy(lot => keys.Key(lot.ProductName) + "|" + lot.MeasuredUnit)
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.MeasuredValue!.Value), StringComparer.Ordinal);
        var attention = AttentionKeys(lots, keys, session.Setup.Today);
        var sequence = entries.Select(entry =>
        {
            byRecipe.TryGetValue(entry.RecipeId, out var card);
            if (card is null)
            {
                return new SequenceEntry(entry.Id, entry.RecipeId, IngredientPerishability.NotPerishable, false, 0, entry.CreatedAt);
            }

            var ranked = ToRankCard(card, session.Setup, keys, []);
            var rank = ranked.Ingredients
                .Where(item => frozen.GetValueOrDefault(item.Key) < item.Quantity)
                .Select(item => PerishabilityOf(item.Key))
                .DefaultIfEmpty(IngredientPerishability.NotPerishable)
                .Min();
            return new SequenceEntry(entry.Id, entry.RecipeId, rank, ranked.Ingredients.Any(item => attention.Contains(item.Key)), card.Snapshot.TotalMinutes, entry.CreatedAt);
        }).ToList();
        var ordered = ShoppingPlanSequencer.Order(sequence);
        await menus.SetSuggestedOrdersAsync(ownerUserId, session.MenuPlanId, ordered.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index + 1), timeProvider.GetUtcNow(), cancellationToken);
    }

    private static int PerishabilityOf(string key) =>
        IngredientPerishability.Rank(IngredientPerishability.ItemOfKey(key[..key.LastIndexOf('|')], IngredientCatalog.Default));

    private async Task<RecipeApplicationResult<ShoppingPlanView>> ResultAsync(Guid ownerUserId, ShoppingPlanSession session, CancellationToken cancellationToken, RecipeApplicationSuccess success = RecipeApplicationSuccess.Succeeded)
    {
        var fresh = await store.FindAsync(ownerUserId, session.Id, cancellationToken) ?? session;
        var cards = await store.ListCardsAsync(ownerUserId, fresh.Id, cancellationToken);
        return RecipeApplicationResult<ShoppingPlanView>.Succeeded(BuildView(fresh, cards, await LoadRankContextAsync(ownerUserId, fresh, cards, cancellationToken)), success);
    }

    private sealed record LoadedRank(RankContext Context, IReadOnlyDictionary<Guid, RankCard> ByCard, IReadOnlySet<string> AttentionKeys);

    private async Task<LoadedRank> LoadRankContextAsync(Guid ownerUserId, ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, CancellationToken cancellationToken)
    {
        var lots = await inventory.ListActiveAsync(ownerUserId, MaxPantryLots, cancellationToken);
        var profile = await profiles.FindAsync(ownerUserId, cancellationToken);
        var restrictions = Restrictions(profile);
        var keys = await matching.KeysAsync(ownerUserId, cards.SelectMany(card => card.Snapshot.Ingredients.Select(item => item.Name)).Concat(lots.Select(lot => lot.ProductName)), cancellationToken);
        var committed = await planning.GetCommitmentsAsync(ownerUserId, cancellationToken);
        var free = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var lot in lots.Where(lot => lot.MeasuredValue is > 0 && lot.MeasuredUnit is not null))
        {
            var taken = committed.TryGetValue(lot.LotId, out var commitment) ? commitment.ProtectedQuantity + commitment.Reservations.Sum(item => item.Quantity) : 0;
            var key = keys.Key(lot.ProductName) + "|" + lot.MeasuredUnit;
            free[key] = free.GetValueOrDefault(key) + Math.Max(0, lot.MeasuredValue!.Value - taken);
        }

        var attention = AttentionKeys(lots, keys, session.Setup.Today);
        var ranked = cards.ToDictionary(card => card.Id, card => ToRankCard(card, session.Setup, keys, restrictions));
        var accepted = cards.Where(card => card.State == ShoppingPlanCardState.Accepted)
            .OrderBy(card => card.DecidedAt)
            .Select(card => new RankAccepted(ranked[card.Id], card.Repeats, session.Setup.CookingStyle == "once" || (session.Setup.CookingStyle == "mix" && card.AcceptMode == "yield")))
            .ToList();
        var rejected = cards.Where(card => card.State == ShoppingPlanCardState.Rejected)
            .Select(card => new RankRejected(ranked[card.Id], card.RejectReason, card.RejectIngredientKey))
            .ToList();
        return new LoadedRank(new RankContext(session.Setup.Priority, accepted, rejected, free, attention, session.Setup.PrepMinutes), ranked, attention);
    }

    // Advisory only: this orders cards and explains why. It is evaluated on the person's own day.
    private static IReadOnlySet<string> AttentionKeys(IReadOnlyList<InventoryLotReadModel> lots, IngredientMatchKeys keys, DateOnly today) =>
        lots.Where(lot => lot.MeasuredUnit is not null)
            .Where(lot => InventoryAttentionAdvisor.Evaluate(
                new InventoryAttentionFacts(lot.PrintedExpirationDate, lot.Lifecycle?.PreparedShelfLifeDate, lot.StorageLocation, lot.PackageState, lot.Lifecycle?.State ?? "AsAcquired", lot.Lifecycle?.FrozenAt, lot.Lifecycle?.ThawStartedAt, lot.Lifecycle?.ThawedAt),
                today, TimeZoneInfo.Utc).State is InventoryAttentionState.UseSoon or InventoryAttentionState.UsePriority or InventoryAttentionState.CheckCondition)
            .Select(lot => keys.Key(lot.ProductName) + "|" + lot.MeasuredUnit)
            .ToHashSet(StringComparer.Ordinal);

    private static RankCard ToRankCard(ShoppingPlanCard card, ShoppingPlanSetup setup, IngredientMatchKeys keys, IReadOnlyList<RestrictionCode> restrictions)
    {
        var scale = (decimal)setup.People / Math.Max(1, card.Snapshot.Servings);
        var ingredients = new List<RankIngredient>();
        foreach (var item in card.Snapshot.Ingredients.Where(item => !item.Optional && !item.Assumed && !HouseholdBasics.Is(item.Name)))
        {
            var matchKey = keys.Key(item.Name);
            var catalog = IngredientPerishability.ItemOfKey(matchKey, IngredientCatalog.Default);
            ingredients.Add(new RankIngredient(matchKey + "|" + item.Unit, item.Name, RecipeIngredientReader.Scale(item.Quantity, item.Unit, scale), IngredientPerishability.IsPerishable(catalog), catalog is not null && CostlyCategories.Contains(catalog.Category)));
        }

        // The main ingredient by weight or volume comes first, because a count of units cannot be
        // compared with grams, and the model's primary references are not stable across batches.
        var lead = ingredients.Where(item => !item.Key.EndsWith("|Unit", StringComparison.Ordinal)).OrderByDescending(item => item.Quantity).FirstOrDefault() ?? ingredients.FirstOrDefault();
        return new RankCard(card.Id, card.FirstSeenOrder, card.Snapshot.DishFormat, card.Snapshot.Technique, lead?.Key, card.Snapshot.ActiveMinutes, card.Snapshot.TotalMinutes, ingredients, RestrictionGuard.Default.DislikeMatches(card.Snapshot.Ingredients.Select(item => item.Name), keys, restrictions));
    }

    private static ShoppingPlanView BuildView(ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, LoadedRank loaded)
    {
        var setup = session.Setup;
        var acceptedCards = cards.Where(card => card.State == ShoppingPlanCardState.Accepted).OrderBy(card => card.DecidedAt).ToList();
        var needs = new Dictionary<string, int>(setup.MealNeeds(), StringComparer.Ordinal);
        foreach (var card in acceptedCards)
        {
            var group = ShoppingPlanSetup.GroupOf(card.Snapshot.MealType);
            needs[group] = needs.GetValueOrDefault(group) - card.Repeats;
        }

        var filled = acceptedCards.Sum(card => card.Repeats);
        var eligible = cards.Where(card => card.State == ShoppingPlanCardState.Unseen && needs.GetValueOrDefault(ShoppingPlanSetup.GroupOf(card.Snapshot.MealType)) > 0).ToList();
        var byId = cards.ToDictionary(card => card.Id);
        var ranked = ShoppingPlanRanker.Rank(eligible.Select(card => loaded.ByCard[card.Id]), loaded.Context);
        var queue = ranked.Take(QueueSize).Select(item => ToCardView(byId[item.Card.Id], item, loaded.AttentionKeys)).ToList();
        var projection = new ShoppingPlanProjection(loaded.Context.Accepted, loaded.Context.FreeStock);
        var open = IsOpen(session);
        var canAskBatch = open && session.BatchCount < ShoppingPlanSession.MaxBatches && cards.Count < ShoppingPlanSession.MaxCards && filled < setup.TotalMeals;
        return new ShoppingPlanView(
            session.Id,
            session.MenuPlanId,
            Name(session.Status),
            setup,
            new ShoppingPlanProgressView(setup.TotalMeals, filled, projection.ListItemCount, ShoppingPlanRanker.SessionMinutesUsed(loaded.Context.Accepted), setup.PrepMinutes),
            queue,
            acceptedCards.Select(card => new ShoppingPlanAcceptedView(card.Id, card.Snapshot.Name, card.Snapshot.MealType, card.Repeats, card.AcceptMode, card.ExpansionState.ToString().ToLowerInvariant(), card.Snapshot.FailureCode, card.RecipeId, card.Snapshot.GuardCode, card.Snapshot.GuardIngredient)).ToList(),
            canAskBatch && eligible.Count < LowQueue,
            session.ConsecutiveRejections >= MoreOptionsAfterRejections,
            open && eligible.Count == 0 && !canAskBatch && filled < setup.TotalMeals,
            open && session.LastDecidedCardId is not null);
    }

    private static ShoppingPlanCardView ToCardView(ShoppingPlanCard card, RankedCard ranked, IReadOnlySet<string> attention)
    {
        var snapshot = card.Snapshot;
        var reason = ranked.AttentionHits > 0 ? "uses_attention_item" : ranked.Reused.Count >= 2 ? "reuses_list_items" : null;
        var lead = ranked.Card.Ingredients.FirstOrDefault(item => item.Key == ranked.Card.LeadKey)?.DisplayName;
        return new ShoppingPlanCardView(
            card.Id,
            card.Source.ToString().ToLowerInvariant(),
            snapshot.Name,
            snapshot.Summary,
            snapshot.MealType,
            snapshot.Servings,
            snapshot.TotalMinutes,
            snapshot.ActiveMinutes,
            ranked.NewItems.Count,
            ranked.Reused.Select(item => item.DisplayName).Take(3).ToList(),
            reason,
            reason == "uses_attention_item" ? ranked.Card.Ingredients.First(item => attention.Contains(item.Key)).DisplayName : null,
            ranked.OverBudget,
            snapshot.GuardCode,
            snapshot.GuardIngredient,
            lead,
            snapshot.Ingredients.Where(item => !item.Assumed).Select(item => new ShoppingPlanCardIngredientView(item.Name, item.Quantity, item.Unit, item.Optional)).ToList());
    }

    private static PlanDeckRequest BuildRequest(ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, LoadedRank loaded, RecipeSuggestionGuidance? guidance, string? language)
    {
        var setup = session.Setup;
        var needs = new Dictionary<string, int>(setup.MealNeeds(), StringComparer.Ordinal);
        foreach (var card in cards.Where(card => card.State == ShoppingPlanCardState.Accepted))
        {
            var group = ShoppingPlanSetup.GroupOf(card.Snapshot.MealType);
            needs[group] = Math.Max(0, needs.GetValueOrDefault(group) - card.Repeats);
        }

        var projection = new ShoppingPlanProjection(loaded.Context.Accepted, loaded.Context.FreeStock);
        // A dish the person is tired of or dislikes in this plan must not come back from the model.
        var avoid = cards.Where(card => card.RejectReason is "tired_of" or "dislike")
            .Select(card => loaded.ByCard[card.Id])
            .Select(card => card.Ingredients.FirstOrDefault(item => item.Key == card.LeadKey)?.DisplayName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var minutesLeft = setup.PrepMinutes is { } minutes ? Math.Max(0, minutes - ShoppingPlanRanker.SessionMinutesUsed(loaded.Context.Accepted)) : (int?)null;
        var brief = new PlanDeckBrief(
            setup.Days,
            setup.CookingStyle,
            setup.Priority,
            needs,
            setup.MealTypes.Where(meal => needs.GetValueOrDefault(ShoppingPlanSetup.GroupOf(meal)) > 0).DefaultIfEmpty(setup.MealTypes[0]).ToList(),
            minutesLeft,
            cards.Where(card => card.State == ShoppingPlanCardState.Accepted).Select(card => card.Snapshot.Name).ToList(),
            projection.ListNames.Distinct(StringComparer.Ordinal).ToList(),
            cards.Where(card => card.State == ShoppingPlanCardState.Rejected).Select(card => card.Snapshot.Name).ToList(),
            avoid,
            setup.AnchorIngredient);
        return new PlanDeckRequest(setup.People, brief, guidance, language);
    }

    private async Task FailCardAsync(ShoppingPlanCard card, string code, CancellationToken cancellationToken)
    {
        card.ExpansionState = ShoppingPlanExpansionState.Failed;
        card.Snapshot = card.Snapshot with { FailureCode = code };
        await store.SaveCardAsync(card, cancellationToken);
    }

    private async Task<(ShoppingPlanSession? Session, ShoppingPlanCard? Card, RecipeApplicationResult<ShoppingPlanView>? Failure)> FindCardAsync(Guid ownerUserId, Guid sessionId, Guid cardId, CancellationToken cancellationToken)
    {
        var session = await store.FindAsync(ownerUserId, sessionId, cancellationToken);
        var card = session is null ? null : (await store.ListCardsAsync(ownerUserId, session.Id, cancellationToken)).FirstOrDefault(item => item.Id == cardId);
        return card is null ? (null, null, NotFound<ShoppingPlanView>()) : (session, card, null);
    }

    private static bool IsOpen(ShoppingPlanSession session) => session.Status is ShoppingPlanStatus.Building or ShoppingPlanStatus.Reviewing;

    private static string Name(ShoppingPlanStatus status) => status.ToString().ToLowerInvariant();

    private static Guid EntryKey(ShoppingPlanCard card, int index) => DerivedKey(card.ExpansionKey, $"entry-{index}");

    private static Guid AttemptKey(ShoppingPlanCard card) => card.Snapshot.ExpansionAttempt == 0 ? card.ExpansionKey : DerivedKey(card.ExpansionKey, $"attempt-{card.Snapshot.ExpansionAttempt}");

    /// <summary>The same inputs always give the same key, so a retry finds what the first attempt made instead of adding a copy.</summary>
    private static Guid DerivedKey(Guid key, string purpose) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{key:n}:{purpose}")).AsSpan(0, 16));

    private static RecipeApplicationResult<ShoppingPlanView> InProgress(ShoppingPlanSession current) =>
        RecipeApplicationResult<ShoppingPlanView>.Failure("plan_in_progress", "Another plan is in progress.", new Dictionary<string, string[]> { ["sessionId"] = [current.Id.ToString()] });

    private static RecipeApplicationResult<T> NotFound<T>() => Failure<T>("resource_not_found", "The plan or card was not found.");

    private static RecipeApplicationResult<T> NotOpen<T>() => Failure<T>("domain_rule_violated", "This plan is closed.");

    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, string? field = null) =>
        RecipeApplicationResult<T>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [detail] });
}
