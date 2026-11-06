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

    /// <summary>Lists the newest accepted plans, including those being changed, to offer repeating them.</summary>
    Task<IReadOnlyList<ShoppingPlanSession>> ListFinishedAsync(Guid ownerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Finds the accepted plan that filled a menu, including one being changed, if any.</summary>
    Task<ShoppingPlanSession?> FindFinishedByMenuAsync(Guid ownerUserId, Guid menuPlanId, CancellationToken cancellationToken);

    /// <summary>Finds the plan of a menu in any state, if any.</summary>
    Task<ShoppingPlanSession?> FindByMenuAsync(Guid ownerUserId, Guid menuPlanId, CancellationToken cancellationToken);

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

    /// <summary>Deletes specific cards of the plan.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardIds">Cards.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task DeleteCardsByIdAsync(Guid ownerUserId, Guid sessionId, IReadOnlyCollection<Guid> cardIds, CancellationToken cancellationToken);

    /// <summary>Deletes the plan's cards in the given states.</summary>
    /// <param name="ownerUserId">Owner.</param>
    /// <param name="sessionId">Plan.</param>
    /// <param name="states">States to delete.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task DeleteCardsAsync(Guid ownerUserId, Guid sessionId, IReadOnlyCollection<ShoppingPlanCardState> states, CancellationToken cancellationToken);

    /// <summary>Deletes the owner's own abandoned plans older than the cutoff.</summary>
    Task DeleteAbandonedBeforeAsync(Guid ownerUserId, DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>What one request for dishes asks for (PLAN-0065).</summary>
/// <param name="MealType">The one meal type to suggest for.</param>
/// <param name="Request">A specific dish in the person's words, at most 120 characters; untrusted. It applies to this request only.</param>
/// <param name="LikeCardId">A card to offer dishes close to.</param>
/// <param name="ReplaceOffers">Whether earlier unseen AI and library dishes of this meal give way, as when a switch changes.</param>
public sealed record ShoppingPlanBatchAsk(string MealType, string? Request, Guid? LikeCardId, bool ReplaceOffers);

/// <summary>The switches above the options (PLAN-0065).</summary>
/// <param name="UsePreferences">Whether the profile's tastes count. Restrictions and dislikes always do.</param>
/// <param name="UsePantry">Whether to build on the pantry and the dishes already chosen.</param>
/// <param name="Goal"><c>quick</c>, <c>short</c>, <c>save</c>, or null.</param>
public sealed record ShoppingPlanPreferencesView(bool UsePreferences, bool UsePantry, string? Goal);

/// <summary>One meal type of a plan, with its options (PLAN-0065).</summary>
/// <param name="MealType">Meal type.</param>
/// <param name="Wanted">How many the person wants.</param>
/// <param name="Filled">How many are chosen.</param>
/// <param name="Options">The next options, best first, at most six; empty once nothing is missing.</param>
public sealed record ShoppingPlanMealGroupView(string MealType, int Wanted, int Filled, IReadOnlyList<ShoppingPlanCardView> Options);

/// <summary>The plan behind a menu, as the menu page needs it (PLAN-0065 Slice 4).</summary>
/// <param name="SessionId">The plan, or null for a menu built by hand.</param>
/// <param name="Status">The plan's state, or null.</param>
/// <param name="MealCounts">How many of each meal the plan wants; empty without a plan.</param>
public sealed record ShoppingPlanMenuLinkView(Guid? SessionId, string? Status, IReadOnlyDictionary<string, int> MealCounts);

/// <summary>The answers under "Quero outras opções" (PLAN-0064).</summary>
/// <param name="Pantry"><c>use</c>, <c>free</c>, or null.</param>
/// <param name="Similarity"><c>any</c>, <c>similar</c>, <c>different</c>, or null.</param>
/// <param name="ReferenceCardId">The card on screen, required to compare.</param>
/// <param name="Hint">Optional typed hint, at most 40 characters; untrusted.</param>
public sealed record ShoppingPlanDirection(string? Pantry, string? Similarity, Guid? ReferenceCardId, string? Hint);

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
/// <param name="MealCounts">How many of each meal (PLAN-0065). When given, style, goal, and preparation become optional.</param>
/// <param name="StartDate">First day of the period, when counts are given; today when null.</param>
public sealed record CreateShoppingPlanCommand(Guid? IdempotencyKey, string CorrelationId, string? RequestLanguage, DateOnly? Today, int Days, IReadOnlyList<string>? MealTypes, int MealsOut, string? CookingStyle, string? Priority, DateOnly? PrepDate, int? PrepMinutes, string? AnchorIngredient, int? People, Guid? FromMenuId = null, IReadOnlyDictionary<string, int>? MealCounts = null, DateOnly? StartDate = null);

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
/// <param name="LastPreferences">The switches of the last plan, as defaults for this one.</param>
public sealed record ShoppingPlanStartView(ShoppingPlanSetup? LastSetup, int People, IReadOnlyList<ShoppingPlanRestrictionView> Restrictions, IReadOnlyList<string> UnverifiableRestrictions, bool HasFreezer, ShoppingPlanCurrentView? Current, IReadOnlyList<ShoppingPlanRecentView> Recent, ShoppingPlanPreferencesView? LastPreferences = null);

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

/// <summary>What a base card makes and which accepted or offered dishes use it.</summary>
/// <param name="OutputName">Ingredient it makes.</param>
/// <param name="OutputQuantity">Quantity it makes.</param>
/// <param name="OutputUnit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="UsedBy">Dish names.</param>
public sealed record ShoppingPlanComponentView(string OutputName, decimal OutputQuantity, string OutputUnit, IReadOnlyList<string> UsedBy);

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
/// <param name="Component">Present when the card is a reusable base.</param>
/// <param name="RequestText">What the person typed, when this card answers a specific request.</param>
public sealed record ShoppingPlanCardView(Guid CardId, string Source, string Name, string Summary, string MealType, int Servings, int TotalMinutes, int ActiveMinutes, int NewItemCount, IReadOnlyList<string> ReusedNames, string? ReasonCode, string? ReasonIngredient, bool OverBudget, string? GuardCode, string? GuardIngredient, string? LeadIngredient, IReadOnlyList<ShoppingPlanCardIngredientView> Ingredients, ShoppingPlanComponentView? Component = null, string? RequestText = null);

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
/// <param name="IsComponent">Whether it is a base, which fills no meal.</param>
/// <param name="PlannedDate">The day the person gave it, if any.</param>
/// <param name="TotalMinutes">Total time of the dish.</param>
/// <param name="People">People each of its meals is for: the plan's people unless the person changed it for this dish.</param>
public sealed record ShoppingPlanAcceptedView(Guid CardId, string Name, string MealType, int Repeats, string? Mode, string ExpansionState, string? FailureCode, Guid? RecipeId, string? GuardCode, string? GuardIngredient, bool IsComponent = false, DateOnly? PlannedDate = null, int TotalMinutes = 0, int People = 0);

/// <summary>One dish of "Completar" (PLAN-0065).</summary>
/// <param name="CardId">An offered card.</param>
/// <param name="MealType">The meal it fills.</param>
public sealed record ShoppingPlanChoice(Guid CardId, string? MealType);

/// <summary>What "Completar" chose, and what it could not.</summary>
/// <param name="Plan">The plan after every choice.</param>
/// <param name="Failed">Cards that were not chosen, for example because their meal filled up meanwhile; the others stay chosen.</param>
public sealed record ShoppingPlanChoicesView(ShoppingPlanView Plan, IReadOnlyList<Guid> Failed);

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
/// <param name="Groups">Each meal type with what is wanted, chosen, and offered.</param>
/// <param name="Preferences">The switches in force.</param>
public sealed record ShoppingPlanView(Guid SessionId, Guid MenuId, string Status, ShoppingPlanSetup Setup, ShoppingPlanProgressView Progress, IReadOnlyList<ShoppingPlanCardView> Queue, IReadOnlyList<ShoppingPlanAcceptedView> Accepted, bool NeedsBatch, bool OfferMoreOptions, bool Exhausted, bool CanUndo, IReadOnlyList<ShoppingPlanMealGroupView>? Groups = null, ShoppingPlanPreferencesView? Preferences = null);

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
    private static readonly Counter<long> MealFitDropped = Meter.CreateCounter<long>("cocinaris.shopping_plan.meal_fit_dropped");
    /// <summary>Below this many usable dishes from a batch, one more call is worth its cost.</summary>
    private const int MinUsefulBatch = 3;
    /// <summary>Goal of a plan with no goal chosen: it adds nothing to the ranking.</summary>
    private const string NoPriority = "none";
    private const int OptionsPerMeal = 6;
    private static readonly string[] MealDisplayOrder = ["breakfast", "lunch", "dinner", "snack"];
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
            await ListRecentAsync(user.Id, cancellationToken),
            PreferencesOf(latest?.DeckPreference)));
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
            var dishes = entries.Where(item => item.MenuPlanId == session.MenuPlanId && item.Kind == MenuEntryKind.Meal && item.Status != MenuEntryStatus.Skipped && active.ContainsKey(item.RecipeId))
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
            command = command with { MealCounts = old.MealCounts, StartDate = null, Days = old.Days, MealTypes = old.MealTypes, MealsOut = old.MealsOut, CookingStyle = old.CookingStyle, Priority = old.Priority, PrepDate = null, PrepMinutes = old.PrepMinutes, AnchorIngredient = old.AnchorIngredient, People = old.People };
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
            // The switches start from the last plan's, or with tastes on and the pantry off (owner decision D2).
            DeckPreference = setup.MealCounts is null ? null : InitialPreference((await store.FindLatestAsync(user.Id, cancellationToken))?.DeckPreference),
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
    /// <param name="direction">The answers under "Quero outras opções", if any. New dishes then come first.</param>
    /// <param name="ask">A request for one meal type (PLAN-0065). New dishes then come first.</param>
    /// <param name="correlationId">Request correlation identifier.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> AddBatchAsync(Guid sessionId, Guid? key, IReadOnlyList<string>? dishFamilies, string? cuisine, string? time, ShoppingPlanDirection? direction, ShoppingPlanBatchAsk? ask, string correlationId, string? language, CancellationToken cancellationToken)
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

        // The new way of asking replaces the old answers; they stay accepted only until the screen moves.
        if (ask is not null)
        {
            direction = null;
        }

        var guidance = dishFamilies is { Count: > 0 } || cuisine is not null || time is not null
            ? new RecipeSuggestionGuidance(null, dishFamilies ?? [], cuisine, time, [], [])
            : null;
        if (guidance?.Validate() is { Count: > 0 } invalid)
        {
            return RecipeApplicationResult<ShoppingPlanView>.Failure("validation_failed", "The options are invalid.", invalid);
        }

        var existing = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        var (preference, invalidDirection) = ReadDirection(direction, existing);
        if (invalidDirection is not null)
        {
            return invalidDirection;
        }

        string? request = null;
        ShoppingPlanCard? like = null;
        if (ask is not null)
        {
            if (!session.Setup.MealTypes.Contains(ask.MealType))
            {
                return Failure<ShoppingPlanView>("validation_failed", "This meal is not part of the plan.", "mealType");
            }

            if (ask.Request?.Trim().Length > RecipePlanDeckService.MaxRequestLength)
            {
                return Failure<ShoppingPlanView>("validation_failed", "The request is too long.", "request");
            }

            request = RecipePlanDeckService.CleanRequest(ask.Request);
            // Only a card of this plan can be compared with, so another owner's dish name can never enter a prompt.
            like = ask.LikeCardId is { } likeId ? existing.FirstOrDefault(card => card.Id == likeId) : null;
            if (ask.LikeCardId is not null && like is null)
            {
                return Failure<ShoppingPlanView>("validation_failed", "The dish to compare with is not part of this plan.", "likeCardId");
            }
        }

        // Someone who asked for options must be told when there are none, never handed the same screen.
        var asked = guidance is not null || preference is not null || ask is not null;
        if (existing.Count >= ShoppingPlanSession.MaxCards || !await store.TryTakeBatchAsync(user.Id, session.Id, batchKey, cancellationToken))
        {
            return asked && session.LastBatchKey != batchKey ? NoMoreOptions() : await ResultAsync(user.Id, session, cancellationToken);
        }

        session.BatchCount++;
        if (asked)
        {
            // Answers without direction, such as a chip, keep the earlier answers and still come first.
            session.DeckPreference = preference ?? (session.DeckPreference ?? new ShoppingPlanDeckPreference(null, null, null, null, NextOrder(existing))) with { FromOrder = NextOrder(existing) };
        }

        var context = await LoadRankContextAsync(user.Id, session, existing, cancellationToken);
        var generated = await deck.GenerateBatchAsync(user.Id, BuildRequest(session, existing, context, guidance, language, null, ask, request, like), correlationId, cancellationToken);
        if (generated.Problem is { } problem)
        {
            await store.ReturnBatchAsync(user.Id, session.Id, cancellationToken);
            Batches.Add(1, new KeyValuePair<string, object?>("source", "ai"), new KeyValuePair<string, object?>("outcome", problem.ErrorCode));
            return RecipeApplicationResult<ShoppingPlanView>.Failure(problem.ErrorCode, problem.Detail, problem.Errors);
        }

        Batches.Add(1, new KeyValuePair<string, object?>("source", "ai"), new KeyValuePair<string, object?>("outcome", "succeeded"));
        var profile = await profiles.FindAsync(user.Id, cancellationToken);
        // What the person typed wins over the meal rules: asking for a pasta snack is their call.
        var checkFit = string.IsNullOrWhiteSpace(direction?.Hint) && request is null;
        var result = await AddCandidatesAsync(user.Id, session, existing, generated.Value!.Candidates, ShoppingPlanCardSource.Ai, profile, cancellationToken, generated.Value.Components, checkFit, request);
        if (request is not null && result.Added == 0 && result.Blocked.Count > 0)
        {
            // Never a way around: the person is told which restriction stands in the way.
            session.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveAsync(session, cancellationToken);
            return RecipeApplicationResult<ShoppingPlanView>.Failure("request_blocked", "This dish conflicts with a restriction.", new Dictionary<string, string[]> { ["restriction"] = result.Blocked.Distinct(StringComparer.Ordinal).ToArray() });
        }

        var known = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        if (result.Added < MinUsefulBatch && result.Unfit.Count > 0)
        {
            // Mostly the wrong kind of dish: one more try, telling the model which ones. A failure here
            // only leaves fewer dishes, so it is not reported.
            var retryContext = await LoadRankContextAsync(user.Id, session, known, cancellationToken);
            var retry = await deck.GenerateBatchAsync(user.Id, BuildRequest(session, known, retryContext, guidance, language, result.Unfit, ask, request, like), correlationId, cancellationToken);
            if (retry.Problem is null)
            {
                await AddCandidatesAsync(user.Id, session, known, retry.Value!.Candidates, ShoppingPlanCardSource.Ai, profile, cancellationToken, retry.Value.Components, checkFit, request);
                known = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
            }
        }

        if (asked)
        {
            session.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveAsync(session, cancellationToken);
            // Every new dish can repeat an old one or be blocked by a restriction.
            if (known.Count == existing.Count)
            {
                return NoMoreOptions();
            }

            // The answer replaces what was waiting, or the person refuses it all to get past it. The
            // person's own recipes stay behind the answer, and a base stays for the dish it serves.
            var superseded = known
                .Where(card => card.State == ShoppingPlanCardState.Unseen && card.Source != ShoppingPlanCardSource.Saved && card.Snapshot.Component is null && card.FirstSeenOrder < session.DeckPreference!.FromOrder)
                // A request for one meal touches only that meal, and only when it replaces what was on offer.
                .Where(card => ask is null || (ask.ReplaceOffers && ShoppingPlanSetup.GroupOf(card.Snapshot.MealType) == ShoppingPlanSetup.GroupOf(ask.MealType)))
                .Select(card => card.Id)
                .ToList();
            if (superseded.Count > 0)
            {
                await store.DeleteCardsByIdAsync(user.Id, session.Id, superseded, cancellationToken);
            }
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Goes back to choosing dishes from the review. Nothing else changes.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> ReopenAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsDraft(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        session.Status = ShoppingPlanStatus.Building;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    // Cards can be deleted, so a count would reuse an order and put old dishes among the new.
    private static int NextOrder(IReadOnlyList<ShoppingPlanCard> cards) => cards.Count == 0 ? 0 : cards.Max(card => card.FirstSeenOrder) + 1;

    private static RecipeApplicationResult<ShoppingPlanView> NoMoreOptions() =>
        Failure<ShoppingPlanView>("no_more_options", "No new dishes are available for this plan.");

    // Reads "Quero outras opções". The hint is untrusted, is cleaned here, and is never logged.
    private static (ShoppingPlanDeckPreference? Preference, RecipeApplicationResult<ShoppingPlanView>? Failure) ReadDirection(ShoppingPlanDirection? direction, IReadOnlyList<ShoppingPlanCard> cards)
    {
        if (direction is null || (direction.Pantry is null && direction.Similarity is null && string.IsNullOrWhiteSpace(direction.Hint)))
        {
            return (null, null);
        }

        if (direction.Pantry is not (null or "use" or "free"))
        {
            return (null, Failure<ShoppingPlanView>("validation_failed", "The pantry choice is not valid."));
        }

        if (direction.Similarity is not (null or "any" or "similar" or "different"))
        {
            return (null, Failure<ShoppingPlanView>("validation_failed", "The similarity choice is not valid."));
        }

        if (direction.Hint?.Trim().Length > ShoppingPlanDeckPreference.MaxHintLength)
        {
            return (null, Failure<ShoppingPlanView>("validation_failed", "The hint is too long."));
        }

        var compares = direction.Similarity is "similar" or "different";
        // Only a card of this plan can be compared with, so another owner's dish name can never enter a prompt.
        var reference = compares ? cards.FirstOrDefault(card => card.Id == direction.ReferenceCardId) : null;
        if (compares && reference is null)
        {
            return (null, Failure<ShoppingPlanView>("validation_failed", "The dish to compare with is not part of this plan."));
        }

        return (new ShoppingPlanDeckPreference(direction.Pantry, compares ? direction.Similarity : null, reference?.Snapshot.Name, RecipePlanDeckService.CleanHint(direction.Hint), NextOrder(cards)), null);
    }

    /// <summary>Accepts or rejects a card. Repeating a decision is a no-op.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="decision"><c>accept</c> or <c>reject</c>.</param>
    /// <param name="mode"><c>yield</c>, <c>again</c>, or null for one meal.</param>
    /// <param name="repeats">Meals to fill, 1 to 4.</param>
    /// <param name="mealType">The meal this dish fills; the card's own when null. Lunch and dinner are interchangeable.</param>
    /// <param name="plannedDate">An optional day inside the period. Most dishes have none.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> DecideAsync(Guid sessionId, Guid cardId, string? decision, string? mode, int? repeats, string? mealType, DateOnly? plannedDate, CancellationToken cancellationToken)
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

            if (card!.Snapshot.Component is not null && count > 1)
            {
                return Failure<ShoppingPlanView>("validation_failed", "A base is prepared once.", "repeats");
            }

            if (mode == "yield" && PeopleOf(card, session!.Setup) * count > MaxEntryServings)
            {
                return Failure<ShoppingPlanView>("validation_failed", "That is too many portions for one cooking.", "repeats");
            }

            // A base fills no meal, and plans from before the counts choose no meal per dish.
            if (card.State == ShoppingPlanCardState.Unseen && card.Snapshot.Component is null && (mealType is not null || plannedDate is not null || session!.Setup.MealCounts is not null))
            {
                var setup = session!.Setup;
                var filled = FilledByMeal(setup, await store.ListCardsAsync(user.Id, session.Id, cancellationToken));
                var own = card.Snapshot.MealType;
                var meal = mealType ?? (filled.GetValueOrDefault(own) < setup.Counts().GetValueOrDefault(own)
                    ? own
                    : setup.MealTypes.FirstOrDefault(other => ShoppingPlanSetup.GroupOf(other) == ShoppingPlanSetup.GroupOf(own) && filled.GetValueOrDefault(other) < setup.Counts().GetValueOrDefault(other)) ?? own);
                if (ShoppingPlanSetup.GroupOf(meal) != ShoppingPlanSetup.GroupOf(own) || !setup.MealTypes.Contains(meal))
                {
                    return Failure<ShoppingPlanView>("validation_failed", "This dish does not fit that meal.", "mealType");
                }

                if (filled.GetValueOrDefault(meal) >= setup.Counts().GetValueOrDefault(meal) || RoomInGroup(setup, filled, meal) < count)
                {
                    return Failure<ShoppingPlanView>("validation_failed", "That meal already has everything it asked for.", "mealType");
                }

                if (plannedDate is { } day && !await DayIsInPeriodAsync(user.Id, session, day, cancellationToken))
                {
                    return Failure<ShoppingPlanView>("validation_failed", "The day must fall inside the period.", "plannedDate");
                }

                card.Snapshot = card.Snapshot with { MealType = meal, PlannedDate = plannedDate };
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

    private async Task<bool> DayIsInPeriodAsync(Guid ownerUserId, ShoppingPlanSession session, DateOnly day, CancellationToken cancellationToken) =>
        await menus.FindAsync(ownerUserId, session.MenuPlanId, cancellationToken) is { } plan && plan.Contains(day);

    /// <summary>
    /// Chooses one of the person's saved recipes for a meal, found by typing its name (PLAN-0065). The
    /// recipe joins the plan as a saved card whatever meal types it was saved with: it is the person's
    /// own, so no meal rule applies. Restrictions still do.
    /// </summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="recipeId">An active recipe of the owner.</param>
    /// <param name="mealType">The meal it fills.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="plannedDate">Optional day, such as the day of a dish being swapped on a menu in use.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> ChooseRecipeAsync(Guid sessionId, Guid recipeId, string? mealType, CancellationToken cancellationToken, DateOnly? plannedDate = null)
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

        if (mealType is null || !session.Setup.MealTypes.Contains(mealType))
        {
            return Failure<ShoppingPlanView>("validation_failed", "This meal is not part of the plan.", "mealType");
        }

        var cards = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        // An offer of the same recipe for this meal is the same dish: choose it rather than add a copy.
        var offered = cards.FirstOrDefault(card => card.RecipeId == recipeId && card.State == ShoppingPlanCardState.Unseen && ShoppingPlanSetup.GroupOf(card.Snapshot.MealType) == ShoppingPlanSetup.GroupOf(mealType));
        if (offered is null)
        {
            // Another owner's recipe is not in this list, so it is simply not found.
            var detail = (await recipes.ListActiveAsync(user.Id, MaxActiveRecipes, cancellationToken)).FirstOrDefault(item => item.RecipeId == recipeId);
            if (detail is null)
            {
                return NotFound<ShoppingPlanView>();
            }

            var ingredients = RecipeIngredientReader.Read(detail.NormalizedRecipeJson).ToList();
            var keys = await matching.KeysAsync(user.Id, ingredients.Select(item => item.Name).ToList(), cancellationToken);
            var guard = RestrictionGuard.Default.Check(ingredients.Select(item => item.Name), keys, Restrictions(await profiles.FindAsync(user.Id, cancellationToken)));
            if (!CountGuard(guard))
            {
                return RecipeApplicationResult<ShoppingPlanView>.Failure("request_blocked", "This dish conflicts with a restriction.", new Dictionary<string, string[]> { ["restriction"] = guard.Code is { } code ? [code] : [] });
            }

            offered = new ShoppingPlanCard
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OwnerUserId = user.Id,
                Source = ShoppingPlanCardSource.Saved,
                RecipeId = recipeId,
                ExpansionKey = Guid.NewGuid(),
                FirstSeenOrder = NextOrder(cards),
                Snapshot = SavedSnapshot(detail, mealType, ingredients, guard)
            };
            await store.AddCardsAsync([offered], cancellationToken);
        }

        return await DecideAsync(sessionId, offered.Id, "accept", null, 1, mealType, plannedDate, cancellationToken);
    }

    /// <summary>Sets the switches above the options. The answers belong to this plan and never reach the profile.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="usePreferences">Whether the profile's tastes count.</param>
    /// <param name="usePantry">Whether to build on the pantry and the dishes already chosen.</param>
    /// <param name="goal"><c>quick</c>, <c>short</c>, <c>save</c>, or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetPreferencesAsync(Guid sessionId, bool usePreferences, bool usePantry, string? goal, CancellationToken cancellationToken)
    {
        if (goal is not (null or "quick" or "short" or "save"))
        {
            return Failure<ShoppingPlanView>("validation_failed", "The goal is not valid.", "goal");
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

        session.DeckPreference = new ShoppingPlanDeckPreference(usePantry ? "use" : "free", null, null, null, session.DeckPreference?.FromOrder ?? int.MaxValue, usePreferences, goal);
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Changes how many of each meal the plan wants. It never goes below what is chosen.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="counts">Meals by type.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetMealCountsAsync(Guid sessionId, IReadOnlyDictionary<string, int>? counts, CancellationToken cancellationToken)
    {
        if (counts is null || !ValidCounts(counts))
        {
            return Failure<ShoppingPlanView>("validation_failed", "Choose one to forty meals.", "counts");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsDraft(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        // Checked by group: two lunches chosen can become dinners, but two snacks cannot just go away.
        var kept = KeepCounts(counts);
        var changed = session.Setup with { MealCounts = kept, MealTypes = MealTypesOf(kept) };
        var cards = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        var filled = FilledByMeal(changed, cards);
        if (filled.Keys.Any(meal => RoomInGroup(changed, filled, meal) < 0))
        {
            return Failure<ShoppingPlanView>("meal_count_below_chosen", "Remove a dish before lowering this count.", "counts");
        }

        session.Setup = changed;
        session.UpdatedAt = timeProvider.GetUtcNow();
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Sets or clears the day of a chosen dish. Most dishes have none.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="plannedDate">A day inside the period, or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetCardDayAsync(Guid sessionId, Guid cardId, DateOnly? plannedDate, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindChosenCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var plan = await menus.FindAsync(user.Id, session!.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (plannedDate is { } day && !plan.Contains(day))
        {
            return Failure<ShoppingPlanView>("validation_failed", "The day must fall inside the period.", "plannedDate");
        }

        card!.Snapshot = card.Snapshot with { PlannedDate = plannedDate };
        await store.SaveCardAsync(card, cancellationToken);
        if (await menus.FindEntryByCreationKeyAsync(user.Id, EntryKey(card, 0), cancellationToken) is { } entry)
        {
            var now = timeProvider.GetUtcNow();
            entry.Change(plan, plannedDate, entry.MealType, entry.Servings, entry.Status, now);
            plan.Touch(now);
            await menus.SaveEntryAsync(plan, entry, cancellationToken);
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Sets how many meals one cooking of a chosen dish covers, scaling its portions.</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="meals">Meals covered, 1 to 4. They count toward the dish's meal type.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetCardYieldAsync(Guid sessionId, Guid cardId, int meals, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindChosenCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        if (meals is < 1 or > MaxRepeats || PeopleOf(card!, session!.Setup) * meals > MaxEntryServings)
        {
            return Failure<ShoppingPlanView>("validation_failed", "Choose a number of meals this one cooking can cover.", "meals");
        }

        if (card!.AcceptMode == "again")
        {
            return Failure<ShoppingPlanView>("domain_rule_violated", "This dish is cooked separately each time.");
        }

        var filled = FilledByMeal(session.Setup, await store.ListCardsAsync(user.Id, session.Id, cancellationToken));
        if (meals - card.Repeats > RoomInGroup(session.Setup, filled, card.Snapshot.MealType))
        {
            return Failure<ShoppingPlanView>("validation_failed", "That would pass what this meal asked for.", "meals");
        }

        var plan = await menus.FindAsync(user.Id, session.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        card.Repeats = meals;
        card.AcceptMode = meals > 1 ? "yield" : null;
        await store.SaveCardAsync(card, cancellationToken);
        if (await menus.FindEntryByCreationKeyAsync(user.Id, EntryKey(card, 0), cancellationToken) is { } entry)
        {
            var now = timeProvider.GetUtcNow();
            entry.ChangeCoverage(plan, meals, PeopleOf(card, session.Setup) * meals, now);
            plan.Touch(now);
            await menus.SaveEntryAsync(plan, entry, cancellationToken);
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>Sets how many people one chosen dish is for, without changing the plan's people (PLAN-0065).</summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="cardId">Card.</param>
    /// <param name="people">People, 1 to 12. With "Rende mais", the portions are people times meals, at most 24.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> SetCardPeopleAsync(Guid sessionId, Guid cardId, int people, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (session, card, failure) = await FindChosenCardAsync(user.Id, sessionId, cardId, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var meals = card!.AcceptMode == "yield" ? card.Repeats : 1;
        if (people is < 1 or > ShoppingPlanSetup.MaxPeople || people * meals > MaxEntryServings)
        {
            return Failure<ShoppingPlanView>("validation_failed", "Choose how many people this dish is for.", "people");
        }

        var plan = await menus.FindAsync(user.Id, session!.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        card.Snapshot = card.Snapshot with { People = people };
        await store.SaveCardAsync(card, cancellationToken);
        var now = timeProvider.GetUtcNow();
        for (var index = 0; index < MaxRepeats; index++)
        {
            if (await menus.FindEntryByCreationKeyAsync(user.Id, EntryKey(card, index), cancellationToken) is { } entry)
            {
                entry.Change(plan, entry.PlannedDate, entry.MealType, people * meals, entry.Status, now);
                plan.Touch(now);
                await menus.SaveEntryAsync(plan, entry, cancellationToken);
            }
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>
    /// Chooses several offered dishes at once, for "Completar" (PLAN-0065). Each is its own decision, so
    /// one that no longer fits, because its meal filled up or the card is gone, leaves the others chosen
    /// and is named in <see cref="ShoppingPlanChoicesView.Failed"/>. Repeating the call chooses nothing twice.
    /// </summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="choices">One to forty different cards, each with its meal.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanChoicesView>> ChooseManyAsync(Guid sessionId, IReadOnlyList<ShoppingPlanChoice>? choices, CancellationToken cancellationToken)
    {
        if (choices is null || choices.Count is 0 or > ShoppingPlanSetup.MaxMeals || choices.Select(choice => choice.CardId).Distinct().Count() != choices.Count)
        {
            return Failure<ShoppingPlanChoicesView>("validation_failed", "Choose one to forty different dishes.", "choices");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanChoicesView>();
        }

        if (!IsOpen(session))
        {
            return NotOpen<ShoppingPlanChoicesView>();
        }

        foreach (var choice in choices)
        {
            // A failure here is reported through the card's state below, not by stopping.
            await DecideAsync(sessionId, choice.CardId, "accept", null, 1, choice.MealType, null, cancellationToken);
        }

        var chosen = (await store.ListCardsAsync(user.Id, session.Id, cancellationToken))
            .Where(card => card.State == ShoppingPlanCardState.Accepted)
            .Select(card => card.Id)
            .ToHashSet();
        var view = await ResultAsync(user.Id, session, cancellationToken);
        return RecipeApplicationResult<ShoppingPlanChoicesView>.Succeeded(new ShoppingPlanChoicesView(view.Value!, choices.Where(choice => !chosen.Contains(choice.CardId)).Select(choice => choice.CardId).ToList()));
    }

    /// <summary>
    /// "Distribuir nos dias" (PLAN-0065): gives each undated dish a day, spreading each meal type across
    /// the period in the suggested order, so what spoils first comes first. Dishes that already have a
    /// day keep it. With <paramref name="clear"/>, every chosen dish loses its day instead. Only this
    /// draft changes; nothing reserves until the menu is accepted.
    /// </summary>
    /// <param name="sessionId">Plan.</param>
    /// <param name="clear">Whether to remove the days rather than give them.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> DistributeAsync(Guid sessionId, bool clear, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindAsync(user.Id, sessionId, cancellationToken);
        if (session is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (!IsDraft(session))
        {
            return NotOpen<ShoppingPlanView>();
        }

        var plan = await menus.FindAsync(user.Id, session.MenuPlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        var cards = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        var chosen = cards.Where(card => card.State == ShoppingPlanCardState.Accepted && card.Snapshot.Component is null).ToList();
        // The order needs the real recipes, which a dish still being prepared does not have yet.
        if (!clear && chosen.Any(card => card.ExpansionState == ShoppingPlanExpansionState.Pending))
        {
            return Failure<ShoppingPlanView>("expansions_pending", "Some recipes are still being prepared.");
        }

        var owners = new Dictionary<Guid, (ShoppingPlanCard Card, int Index)>();
        foreach (var card in chosen)
        {
            for (var index = 0; index < MaxRepeats; index++)
            {
                owners[EntryKey(card, index)] = (card, index);
            }
        }

        if (!clear)
        {
            await SequenceAsync(user.Id, session, cards, cancellationToken);
        }

        var entries = (await menus.ListEntriesAsync(user.Id, [session.MenuPlanId], cancellationToken))
            .Where(entry => entry.Kind == MenuEntryKind.Meal && entry.Status == MenuEntryStatus.Planned && owners.ContainsKey(entry.CreationKey))
            .ToList();
        var days = new Dictionary<Guid, DateOnly?>();
        if (clear)
        {
            foreach (var entry in entries.Where(entry => entry.PlannedDate is not null))
            {
                days[entry.Id] = null;
            }
        }
        else
        {
            var length = plan.EndDate.DayNumber - plan.StartDate.DayNumber + 1;
            foreach (var meal in entries.Where(entry => entry.PlannedDate is null).GroupBy(entry => entry.MealType ?? string.Empty))
            {
                var ordered = meal.OrderBy(entry => entry.SuggestedOrder ?? int.MaxValue).ThenBy(entry => entry.CreatedAt).ToList();
                for (var position = 0; position < ordered.Count; position++)
                {
                    days[ordered[position].Id] = plan.StartDate.AddDays(position * length / ordered.Count);
                }
            }
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in entries.Where(entry => days.ContainsKey(entry.Id)))
        {
            entry.Change(plan, days[entry.Id], entry.MealType, entry.Servings, entry.Status, now);
            plan.Touch(now);
            await menus.SaveEntryAsync(plan, entry, cancellationToken);
        }

        // The board shows the first meal's day of each dish; a failed dish has no meal but may have a day.
        foreach (var card in chosen)
        {
            var first = entries.FirstOrDefault(entry => owners[entry.CreationKey] == (card, 0));
            var day = first is not null && days.TryGetValue(first.Id, out var given) ? given : clear ? null : card.Snapshot.PlannedDate;
            if (day != card.Snapshot.PlannedDate)
            {
                card.Snapshot = card.Snapshot with { PlannedDate = day };
                await store.SaveCardAsync(card, cancellationToken);
            }
        }

        return await ResultAsync(user.Id, session, cancellationToken);
    }

    private async Task<(ShoppingPlanSession? Session, ShoppingPlanCard? Card, RecipeApplicationResult<ShoppingPlanView>? Failure)> FindChosenCardAsync(Guid ownerUserId, Guid sessionId, Guid cardId, CancellationToken cancellationToken)
    {
        var found = await FindCardAsync(ownerUserId, sessionId, cardId, cancellationToken);
        if (found.Failure is not null)
        {
            return found;
        }

        if (!IsOpen(found.Session!))
        {
            return (null, null, NotOpen<ShoppingPlanView>());
        }

        return found.Card!.State == ShoppingPlanCardState.Accepted && found.Card.Snapshot.Component is null
            ? found
            : (null, null, Failure<ShoppingPlanView>("domain_rule_violated", "Only a chosen dish can change this."));
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
        card.Snapshot = card.Snapshot with { PlannedDate = null, People = null };
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
            var anchor = session!.Setup.AnchorIngredient;
            var hint = card.Snapshot.PrivateHint ?? anchor;
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

        if (!IsDraft(session))
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

        if (!IsDraft(session))
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

        if (session.Status == ShoppingPlanStatus.Amending)
        {
            // The menu is already in use: closing the change keeps its order and only drops the options.
            await store.DeleteCardsAsync(user.Id, session.Id, [ShoppingPlanCardState.Unseen, ShoppingPlanCardState.Rejected], cancellationToken);
            session.Status = ShoppingPlanStatus.Finished;
            session.LastDecidedCardId = null;
            session.UpdatedAt = timeProvider.GetUtcNow();
            await store.SaveAsync(session, cancellationToken);
            return await ResultAsync(user.Id, session, cancellationToken);
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

        if (!IsDraft(session))
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

    /// <summary>
    /// Opens a menu in use for choosing dishes again (PLAN-0065 Slice 4): to fill a meal still missing,
    /// or to swap one dish. A menu built before plans existed gets a plan here, wanting what it already
    /// has. Every opening reads the menu as it is now, so a dish removed elsewhere stops filling its meal;
    /// a chosen dish whose recipe is still being prepared is kept. Swapping removes that one dish, and
    /// nothing else on the menu changes: dishes chosen here take the next places in its order.
    /// </summary>
    /// <param name="menuId">An accepted menu of the owner.</param>
    /// <param name="today">The owner's day; the server does not know the owner's time zone.</param>
    /// <param name="replaceEntryId">A dish still to cook, removed so another can take its meal.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <param name="language">Interface language, for ideas from the library.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanView>> OpenAmendmentAsync(Guid menuId, DateOnly? today, Guid? replaceEntryId, CancellationToken cancellationToken, string? language = null)
    {
        if (today is not { } day)
        {
            return Failure<ShoppingPlanView>("validation_failed", "The owner's day is required.", "today");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await menus.FindAsync(user.Id, menuId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingPlanView>();
        }

        if (plan.Status != MenuPlanStatus.Accepted)
        {
            return Failure<ShoppingPlanView>("domain_rule_violated", "Only a menu in use is changed this way; a draft is changed on its board.");
        }

        var now = timeProvider.GetUtcNow();
        var session = await store.FindByMenuAsync(user.Id, menuId, cancellationToken);
        if (session is { Status: ShoppingPlanStatus.Abandoned })
        {
            return NotOpen<ShoppingPlanView>();
        }

        if (session is null)
        {
            var entries = await menus.ListEntriesAsync(user.Id, [menuId], cancellationToken);
            var counts = entries.Where(entry => entry.Kind == MenuEntryKind.Meal && entry.Status != MenuEntryStatus.Skipped && entry.MealType is not null)
                .GroupBy(entry => entry.MealType!)
                .ToDictionary(group => group.Key, group => group.Sum(entry => entry.MealsCovered), StringComparer.Ordinal);
            var profile = await profiles.FindAsync(user.Id, cancellationToken);
            var people = profile?.Profile.DefaultServingCount is { } count ? Math.Clamp(count, 1, ShoppingPlanSetup.MaxPeople) : 2;
            var days = plan.EndDate.DayNumber - plan.StartDate.DayNumber + 1;
            var created = new ShoppingPlanSession
            {
                Id = Guid.NewGuid(),
                OwnerUserId = user.Id,
                MenuPlanId = plan.Id,
                CreationKey = DerivedKey(plan.Id, "amendment"),
                // It wants what the menu has, so only a swap leaves something missing.
                Setup = new ShoppingPlanSetup(day, days, MealTypesOf(counts), 0, "daily", NoPriority, null, null, null, people, counts, plan.StartDate),
                Status = ShoppingPlanStatus.Amending,
                PrivateDirection = false,
                DeckPreference = InitialPreference((await store.FindLatestAsync(user.Id, cancellationToken))?.DeckPreference),
                CreatedAt = now,
                UpdatedAt = now
            };
            session = await store.TryAddAsync(created, cancellationToken)
                ? created
                : await store.FindByMenuAsync(user.Id, menuId, cancellationToken) ?? throw new InvalidOperationException("A menu's plan conflicted but could not be reloaded.");
        }

        if (replaceEntryId is { } entryId)
        {
            var target = (await planning.GetAsync(menuId, cancellationToken)).Value?.Entries.FirstOrDefault(item => item.EntryId == entryId);
            var entry = (await menus.ListEntriesAsync(user.Id, [menuId], cancellationToken)).FirstOrDefault(item => item.Id == entryId);
            if (target is null || entry is null)
            {
                return NotFound<ShoppingPlanView>();
            }

            if (target.Status != "planned" || target.Kind != "meal" || target.MealType is null)
            {
                return Failure<ShoppingPlanView>("domain_rule_violated", "Only a dish still to cook, with its meal, can be swapped.");
            }

            plan.Touch(now);
            await menus.RemoveEntryAsync(plan, entry, cancellationToken);
        }

        await SyncAmendmentCardsAsync(user.Id, session, cancellationToken);
        // The person's own recipes and library ideas are there at once, so a swap works without AI.
        var current = await store.ListCardsAsync(user.Id, session.Id, cancellationToken);
        if (!current.Any(card => card.State == ShoppingPlanCardState.Unseen))
        {
            await AddInstantCardsAsync(user.Id, session, current, await profiles.FindAsync(user.Id, cancellationToken), language, cancellationToken);
        }

        session.Status = ShoppingPlanStatus.Amending;
        session.LastDecidedCardId = null;
        session.ConsecutiveRejections = 0;
        session.UpdatedAt = now;
        await store.SaveAsync(session, cancellationToken);
        return await ResultAsync(user.Id, session, cancellationToken);
    }

    /// <summary>
    /// The plan behind a menu, for the menu page: whether there is one and how many of each meal it
    /// wants. Another owner's menu reads as having none.
    /// </summary>
    /// <param name="menuId">Menu.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingPlanMenuLinkView>> GetMenuLinkAsync(Guid menuId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var session = await store.FindByMenuAsync(user.Id, menuId, cancellationToken);
        return RecipeApplicationResult<ShoppingPlanMenuLinkView>.Succeeded(new ShoppingPlanMenuLinkView(
            session?.Id,
            session is null ? null : Name(session.Status),
            session?.Setup.Counts() ?? new Dictionary<string, int>()));
    }

    // The menu speaks for itself: chosen cards are read again from its dishes, so what the person
    // removed or changed on the menu page counts as it is now. Only a dish still being prepared,
    // which has no entry yet, keeps its card.
    private async Task SyncAmendmentCardsAsync(Guid ownerUserId, ShoppingPlanSession session, CancellationToken cancellationToken)
    {
        var cards = await store.ListCardsAsync(ownerUserId, session.Id, cancellationToken);
        var stale = cards.Where(card => card.State == ShoppingPlanCardState.Rejected || (card.State == ShoppingPlanCardState.Accepted && card.ExpansionState != ShoppingPlanExpansionState.Pending)).ToList();
        if (stale.Count > 0)
        {
            await store.DeleteCardsByIdAsync(ownerUserId, session.Id, stale.Select(card => card.Id).ToList(), cancellationToken);
        }

        var active = (await recipes.ListActiveAsync(ownerUserId, MaxActiveRecipes, cancellationToken)).ToDictionary(item => item.RecipeId);
        var entries = (await menus.ListEntriesAsync(ownerUserId, [session.MenuPlanId], cancellationToken))
            .Where(entry => entry.Kind == MenuEntryKind.Meal && entry.Status != MenuEntryStatus.Skipped && entry.MealType is not null && active.ContainsKey(entry.RecipeId))
            .OrderBy(entry => entry.SuggestedOrder ?? int.MaxValue)
            .ThenBy(entry => entry.CreatedAt)
            .ToList();
        var ingredients = entries.Select(entry => entry.RecipeId).Distinct().ToDictionary(id => id, id => RecipeIngredientReader.Read(active[id].NormalizedRecipeJson).ToList());
        var keys = await matching.KeysAsync(ownerUserId, ingredients.Values.SelectMany(items => items.Select(item => item.Name)), cancellationToken);
        var restrictions = Restrictions(await profiles.FindAsync(ownerUserId, cancellationToken));
        var remaining = cards.Except(stale).ToList();
        var order = NextOrder(remaining);
        var now = timeProvider.GetUtcNow();
        var made = new List<ShoppingPlanCard>();
        foreach (var entry in entries)
        {
            var repeats = Math.Clamp(entry.MealsCovered, 1, MaxRepeats);
            var guard = RestrictionGuard.Default.Check(ingredients[entry.RecipeId].Select(item => item.Name), keys, restrictions);
            var snapshot = SavedSnapshot(active[entry.RecipeId], entry.MealType!, ingredients[entry.RecipeId], guard) with
            {
                PlannedDate = entry.PlannedDate,
                People = Math.Clamp(entry.Servings / repeats, 1, ShoppingPlanSetup.MaxPeople)
            };
            made.Add(new ShoppingPlanCard
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                OwnerUserId = ownerUserId,
                Source = ShoppingPlanCardSource.Saved,
                RecipeId = entry.RecipeId,
                ExpansionKey = DerivedKey(entry.Id, "amendment"),
                FirstSeenOrder = order++,
                Snapshot = snapshot,
                State = ShoppingPlanCardState.Accepted,
                AcceptMode = repeats > 1 ? "yield" : null,
                Repeats = repeats,
                ExpansionState = ShoppingPlanExpansionState.Ready,
                DecidedAt = now.AddMilliseconds(made.Count)
            });
        }

        if (made.Count > 0)
        {
            await store.AddCardsAsync(made, cancellationToken);
        }
    }

    /// <summary>Validates the setup answers; null when any is invalid.</summary>
    /// <param name="command">Answers.</param>
    /// <param name="defaultPeople">People when none were given.</param>
    public static ShoppingPlanSetup? ValidateSetup(CreateShoppingPlanCommand command, int defaultPeople)
    {
        if (command.MealCounts is not null)
        {
            return ValidateCounts(command, defaultPeople);
        }

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

    // A plan by counts asks only for the period and how many of each meal; the rest has fixed defaults.
    private static ShoppingPlanSetup? ValidateCounts(CreateShoppingPlanCommand command, int defaultPeople)
    {
        var people = command.People ?? defaultPeople;
        if (command.Today is not { } today || command.Days is < 1 or > ShoppingPlanSetup.MaxDays || people is < 1 or > ShoppingPlanSetup.MaxPeople || !ValidCounts(command.MealCounts!))
        {
            return null;
        }

        var start = command.StartDate ?? today;
        if (start < today || start > today.AddDays(ShoppingPlanSetup.MaxPrepDaysAhead))
        {
            return null;
        }

        var counts = KeepCounts(command.MealCounts!);
        return new ShoppingPlanSetup(today, command.Days, MealTypesOf(counts), 0, "mix", NoPriority, null, null, RecipePlanDeckService.CleanAnchor(command.AnchorIngredient), people, counts, start);
    }

    private static bool ValidCounts(IReadOnlyDictionary<string, int> counts) =>
        counts.All(item => MealTypes.Contains(item.Key) && item.Value is >= 0 and <= ShoppingPlanSetup.MaxMeals)
        && counts.Values.Sum() is >= 1 and <= ShoppingPlanSetup.MaxMeals;

    private static Dictionary<string, int> KeepCounts(IReadOnlyDictionary<string, int> counts) =>
        counts.Where(item => item.Value > 0).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static List<string> MealTypesOf(IReadOnlyDictionary<string, int> counts) =>
        MealDisplayOrder.Where(counts.ContainsKey).ToList();

    private static ShoppingPlanDeckPreference InitialPreference(ShoppingPlanDeckPreference? last) =>
        new(last?.Pantry == "use" ? "use" : "free", null, null, null, int.MaxValue, last?.UsePreferences ?? true, last?.Goal);

    private static ShoppingPlanPreferencesView PreferencesOf(ShoppingPlanDeckPreference? preference) =>
        new(preference?.UsePreferences ?? true, preference?.Pantry == "use", preference?.Goal);

    // Lunch and dinner are interchangeable: a dish that yields three covers both, so what passes one
    // counts toward the other instead of showing "3 of 2".
    private static Dictionary<string, int> FilledByMeal(ShoppingPlanSetup setup, IEnumerable<ShoppingPlanCard> cards)
    {
        var filled = cards.Where(card => card.State == ShoppingPlanCardState.Accepted && card.Snapshot.Component is null)
            .GroupBy(card => card.Snapshot.MealType)
            .ToDictionary(group => group.Key, group => group.Sum(card => card.Repeats), StringComparer.Ordinal);
        var counts = setup.Counts();
        foreach (var (meal, other) in new[] { ("lunch", "dinner"), ("dinner", "lunch") })
        {
            var extra = filled.GetValueOrDefault(meal) - counts.GetValueOrDefault(meal);
            if (extra > 0 && counts.GetValueOrDefault(other) > 0)
            {
                filled[meal] -= extra;
                filled[other] = filled.GetValueOrDefault(other) + extra;
            }
        }

        return filled;
    }

    // Filled and wanted across the meal's group, read from the setup's counts.
    private static int RoomInGroup(ShoppingPlanSetup setup, IReadOnlyDictionary<string, int> filled, string meal)
    {
        var group = ShoppingPlanSetup.GroupOf(meal);
        var counts = setup.Counts();
        return counts.Where(item => ShoppingPlanSetup.GroupOf(item.Key) == group).Sum(item => item.Value)
            - filled.Where(item => ShoppingPlanSetup.GroupOf(item.Key) == group).Sum(item => item.Value);
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
            .Where(item => item.Kind == MenuEntryKind.Meal && item.Status != MenuEntryStatus.Skipped && active.ContainsKey(item.RecipeId) && item.MealType is not null && setup.MealTypes.Contains(item.MealType))
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
    private async Task<(int Added, IReadOnlyList<string> Unfit, IReadOnlyList<string> Blocked)> AddCandidatesAsync(Guid ownerUserId, ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> existing, IReadOnlyList<SuggestCandidate> candidates, ShoppingPlanCardSource source, ProfileReadModel? profile, CancellationToken cancellationToken, IReadOnlyList<PlanDeckComponent>? components = null, bool checkFit = true, string? requestText = null)
    {
        var blocked = new List<string>();
        // The words stay with the dish, which also keeps it out of the shared idea library.
        var privateHint = requestText ?? session.DeckPreference?.Hint;
        var firstOrder = NextOrder(existing);
        var unfit = new List<string>();
        var knownNames = existing.Select(card => card.Snapshot.Name).ToList();
        var restrictions = Restrictions(profile);
        var seenNames = existing.Select(card => IngredientText.Normalize(card.Snapshot.Name)).ToHashSet(StringComparer.Ordinal);
        var seenPrints = existing.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
        var names = candidates.SelectMany(IngredientNames)
            .Concat((components ?? []).SelectMany(component => IngredientNames(component.Candidate).Append(component.Output.Name)))
            .ToList();
        var keys = await matching.KeysAsync(ownerUserId, names, cancellationToken);
        var cards = new List<ShoppingPlanCard>();
        var cardByCandidate = new Dictionary<string, (Guid Id, string Name)>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (existing.Count + cards.Count >= ShoppingPlanSession.MaxCards)
            {
                break;
            }

            if (!session.Setup.MealTypes.Contains(candidate.TargetMealType)
                || !seenNames.Add(IngredientText.Normalize(candidate.Name))
                || !seenPrints.Add(RecipeSuggestSemantics.Fingerprint(candidate))
                // Asked-for dishes are meant to be close to each other, and to what is already there.
                || (requestText is null && knownNames.Any(name => DishSimilarity.IsSameDish(name, candidate.Name))))
            {
                continue;
            }

            if (checkFit && !MealFit.Default.Fits(candidate.TargetMealType, candidate.Name, candidate.DishFormat, candidate.Time.TotalMinutes))
            {
                unfit.Add(candidate.Name);
                MealFitDropped.Add(1, new KeyValuePair<string, object?>("mealType", candidate.TargetMealType));
                continue;
            }

            knownNames.Add(candidate.Name);
            var guard = RestrictionGuard.Default.Check(IngredientNames(candidate), keys, restrictions);
            if (!CountGuard(guard))
            {
                if (guard.Code is { } code)
                {
                    blocked.Add(code);
                }

                continue;
            }

            cardByCandidate[candidate.CandidateId] = (Guid.NewGuid(), candidate.Name);
            cards.Add(new ShoppingPlanCard
            {
                Id = cardByCandidate[candidate.CandidateId].Id,
                SessionId = session.Id,
                OwnerUserId = ownerUserId,
                Source = source,
                CandidateJson = JsonSerializer.Serialize(candidate, SerializerOptions),
                ExpansionKey = Guid.NewGuid(),
                FirstSeenOrder = firstOrder + cards.Count,
                Snapshot = new ShoppingPlanCardSnapshot(
                    candidate.Name, candidate.Summary, candidate.TargetMealType, candidate.DishFormat, candidate.PrimaryTechnique, candidate.PrimaryIngredientRefs, candidate.Servings,
                    candidate.Time.ActiveMinutes, candidate.Time.TotalMinutes, candidate.Difficulty, CardIngredients(candidate), Unverified(guard)?.Code, Unverified(guard)?.Ingredient, PrivateHint: privateHint, RequestText: requestText)
            });
        }

        // A base stays out of sight until a dish that uses it is accepted, and only dishes that made
        // it onto the deck can call for it. It passes the same guard, including what it makes.
        foreach (var component in components ?? [])
        {
            var users = component.UsedBy.Where(cardByCandidate.ContainsKey).Select(id => cardByCandidate[id]).ToList();
            var candidate = component.Candidate;
            if (users.Count == 0 || existing.Count + cards.Count >= ShoppingPlanSession.MaxCards || !seenNames.Add(IngredientText.Normalize(candidate.Name)) || !seenPrints.Add(RecipeSuggestSemantics.Fingerprint(candidate)))
            {
                continue;
            }

            var guard = RestrictionGuard.Default.Check(IngredientNames(candidate).Append(component.Output.Name), keys, restrictions);
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
                FirstSeenOrder = firstOrder + cards.Count,
                Snapshot = new ShoppingPlanCardSnapshot(
                    candidate.Name, candidate.Summary, candidate.TargetMealType, candidate.DishFormat, candidate.PrimaryTechnique, candidate.PrimaryIngredientRefs, candidate.Servings,
                    candidate.Time.ActiveMinutes, candidate.Time.TotalMinutes, candidate.Difficulty, CardIngredients(candidate), Unverified(guard)?.Code, Unverified(guard)?.Ingredient,
                    PrivateHint: privateHint,
                    Component: new ShoppingPlanCardComponent(component.Output.Name, component.Output.Quantity, RecipeIngredientReader.ToLotUnit(component.Output.Unit), users.Select(user => user.Id).ToList(), users.Select(user => user.Name).ToList()))
            });
        }

        if (cards.Count > 0)
        {
            await store.AddCardsAsync(cards, cancellationToken);
        }

        return (cards.Count(card => card.Snapshot.Component is null), unfit, blocked);
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
        if (card.Snapshot.Component is { } component)
        {
            // A base has no meal of its own: it is prepared on the session day, at its own size.
            var baseKey = EntryKey(card, 0);
            if (await menus.FindEntryByCreationKeyAsync(ownerUserId, baseKey, cancellationToken) is null)
            {
                var at = timeProvider.GetUtcNow();
                var made = MenuEntry.Create(plan, baseKey, recipeId, null, setup.PrepDate, null, Math.Clamp(card.Snapshot.Servings, 1, MaxEntryServings), at, 1, new MenuEntryProduction(component.OutputName, component.OutputQuantity, component.OutputUnit));
                plan.Touch(at);
                await menus.TryAddEntryAsync(plan, made, cancellationToken);
            }

            return;
        }

        var yields = card.AcceptMode == "yield";
        var count = card.AcceptMode == "again" ? card.Repeats : 1;
        var plannedDate = card.Snapshot.PlannedDate ?? (yields && setup.CookingStyle != "daily" ? setup.PrepDate : null);
        for (var index = 0; index < count; index++)
        {
            var key = EntryKey(card, index);
            if (await menus.FindEntryByCreationKeyAsync(ownerUserId, key, cancellationToken) is not null)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            // "Fazer de novo" is another day by definition, so only the first one can have the chosen date.
            var people = PeopleOf(card, setup);
            var entry = MenuEntry.Create(plan, key, recipeId, null, index == 0 ? plannedDate : null, card.Snapshot.MealType, yields ? people * card.Repeats : people, now, yields ? card.Repeats : 1);
            plan.Touch(now);
            await menus.TryAddEntryAsync(plan, entry, cancellationToken);
        }

        if (session.Status == ShoppingPlanStatus.Amending)
        {
            await AppendOrderAsync(ownerUserId, session.MenuPlanId, cancellationToken);
        }
    }

    /// <summary>
    /// Gives dishes added to a menu in use the next places in its order, after every dish already there,
    /// so nothing else moves (PLAN-0065 Slice 4). A menu built by hand has no order and keeps none.
    /// </summary>
    private async Task AppendOrderAsync(Guid ownerUserId, Guid menuPlanId, CancellationToken cancellationToken)
    {
        var entries = await menus.ListEntriesAsync(ownerUserId, [menuPlanId], cancellationToken);
        var ordered = entries.Where(entry => entry.SuggestedOrder is not null).ToDictionary(entry => entry.Id, entry => entry.SuggestedOrder!.Value);
        var added = entries.Where(entry => entry.SuggestedOrder is null).OrderBy(entry => entry.CreatedAt).ToList();
        if (ordered.Count == 0 || added.Count == 0)
        {
            return;
        }

        var next = ordered.Values.Max() + 1;
        foreach (var entry in added)
        {
            ordered[entry.Id] = next++;
        }

        await menus.SetSuggestedOrdersAsync(ownerUserId, menuPlanId, ordered, timeProvider.GetUtcNow(), cancellationToken);
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
                return new SequenceEntry(entry.Id, entry.RecipeId, IngredientPerishability.NotPerishable, false, 0, entry.CreatedAt, entry.Kind == MenuEntryKind.Component);
            }

            var ranked = ToRankCard(card, session.Setup, keys, []);
            var rank = ranked.Ingredients
                .Where(item => frozen.GetValueOrDefault(item.Key) < item.Quantity)
                .Select(item => PerishabilityOf(item.Key))
                .DefaultIfEmpty(IngredientPerishability.NotPerishable)
                .Min();
            return new SequenceEntry(entry.Id, entry.RecipeId, rank, ranked.Ingredients.Any(item => attention.Contains(item.Key)), card.Snapshot.TotalMinutes, entry.CreatedAt, entry.Kind == MenuEntryKind.Component);
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
        var keys = await matching.KeysAsync(ownerUserId, cards.SelectMany(card => card.Snapshot.Ingredients.Select(item => item.Name)).Concat(cards.Select(card => card.Snapshot.Component?.OutputName).OfType<string>()).Concat(lots.Select(lot => lot.ProductName)), cancellationToken);
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
            .Select(card => new RankAccepted(ranked[card.Id], card.Repeats, card.Snapshot.Component is not null || session.Setup.CookingStyle == "once" || (session.Setup.CookingStyle == "mix" && card.AcceptMode == "yield")))
            .ToList();
        var rejected = cards.Where(card => card.State == ShoppingPlanCardState.Rejected)
            .Select(card => new RankRejected(ranked[card.Id], card.RejectReason, card.RejectIngredientKey))
            .ToList();
        var preference = session.DeckPreference;
        var pantryWeight = ShoppingPlanRanker.PantryWeight(preference?.Pantry, session.ConsecutiveRejections);
        var priority = preference?.Goal switch
        {
            "quick" => "time",
            "short" => "short",
            "save" => "save",
            // The pantry switch on means using what is there, whatever the plan said at the start.
            _ => preference?.Pantry == "use" && session.Setup.Priority == NoPriority ? "waste" : session.Setup.Priority
        };
        return new LoadedRank(new RankContext(priority, accepted, rejected, free, attention, session.Setup.PrepMinutes, pantryWeight, preference?.Goal is not null), ranked, attention);
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
        var scale = (decimal)PeopleOf(card, setup) / Math.Max(1, card.Snapshot.Servings);
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
        return new RankCard(card.Id, card.FirstSeenOrder, card.Snapshot.DishFormat, card.Snapshot.Technique, lead?.Key, card.Snapshot.ActiveMinutes, card.Snapshot.TotalMinutes, ingredients, RestrictionGuard.Default.DislikeMatches(card.Snapshot.Ingredients.Select(item => item.Name), keys, restrictions), card.Snapshot.Component is { } component ? keys.Key(component.OutputName) + "|" + component.OutputUnit : null, (card.Snapshot.Component?.OutputQuantity ?? 0) * scale);
    }

    private static ShoppingPlanView BuildView(ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, LoadedRank loaded)
    {
        var setup = session.Setup;
        var acceptedCards = cards.Where(card => card.State == ShoppingPlanCardState.Accepted).OrderBy(card => card.DecidedAt).ToList();
        var needs = new Dictionary<string, int>(setup.MealNeeds(), StringComparer.Ordinal);
        // A base fills no meal; it counts on the list and the session, not on the progress.
        foreach (var card in acceptedCards.Where(card => card.Snapshot.Component is null))
        {
            var group = ShoppingPlanSetup.GroupOf(card.Snapshot.MealType);
            needs[group] = needs.GetValueOrDefault(group) - card.Repeats;
        }

        var filled = acceptedCards.Where(card => card.Snapshot.Component is null).Sum(card => card.Repeats);
        var eligible = cards.Where(card => card.State == ShoppingPlanCardState.Unseen && card.Snapshot.Component is null && needs.GetValueOrDefault(ShoppingPlanSetup.GroupOf(card.Snapshot.MealType)) > 0).ToList();
        var acceptedIds = acceptedCards.Select(card => card.Id).ToHashSet();
        var bases = cards.Where(card => card.State == ShoppingPlanCardState.Unseen && card.Snapshot.Component is { } component && component.UsedByCardIds.Any(acceptedIds.Contains)).ToHashSet();
        var byId = cards.ToDictionary(card => card.Id);
        // The base a chosen dish calls for comes next, whatever its score.
        // Then the dishes that answer the person's latest request, or asking would change nothing on screen.
        var answerFrom = session.DeckPreference?.FromOrder ?? int.MaxValue;
        var ranked = ShoppingPlanRanker.Rank(eligible.Concat(bases).Select(card => loaded.ByCard[card.Id]), loaded.Context)
            .OrderByDescending(item => bases.Contains(byId[item.Card.Id]))
            .ThenByDescending(item => item.Card.Order >= answerFrom)
            // The model puts the dish that was asked for first, and no score may bury it.
            .ThenByDescending(item => byId[item.Card.Id].Snapshot.RequestText is not null)
            .ThenBy(item => byId[item.Card.Id].Snapshot.RequestText is not null ? item.Card.Order : 0);
        var inOrder = ranked.ToList();
        var queue = inOrder.Take(QueueSize).Select(item => ToCardView(byId[item.Card.Id], item, loaded.AttentionKeys)).ToList();
        var filledByMeal = FilledByMeal(setup, cards);
        var groups = setup.Counts().Where(item => item.Value > 0)
            .OrderBy(item => Array.IndexOf(MealDisplayOrder, item.Key))
            .Select(item => new ShoppingPlanMealGroupView(
                item.Key,
                item.Value,
                filledByMeal.GetValueOrDefault(item.Key),
                filledByMeal.GetValueOrDefault(item.Key) >= item.Value
                    ? []
                    : inOrder.Where(ranked => !bases.Contains(byId[ranked.Card.Id]) && ShoppingPlanSetup.GroupOf(byId[ranked.Card.Id].Snapshot.MealType) == ShoppingPlanSetup.GroupOf(item.Key))
                        .Take(OptionsPerMeal)
                        .Select(ranked => ToCardView(byId[ranked.Card.Id], ranked, loaded.AttentionKeys))
                        .ToList()))
            .ToList();
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
            acceptedCards.Select(card => new ShoppingPlanAcceptedView(card.Id, card.Snapshot.Name, card.Snapshot.MealType, card.Repeats, card.AcceptMode, card.ExpansionState.ToString().ToLowerInvariant(), card.Snapshot.FailureCode, card.RecipeId, card.Snapshot.GuardCode, card.Snapshot.GuardIngredient, card.Snapshot.Component is not null, card.Snapshot.PlannedDate, card.Snapshot.TotalMinutes, PeopleOf(card, setup))).ToList(),
            canAskBatch && eligible.Count < LowQueue,
            session.ConsecutiveRejections >= MoreOptionsAfterRejections,
            open && eligible.Count == 0 && !canAskBatch && filled < setup.TotalMeals,
            open && session.LastDecidedCardId is not null,
            groups,
            PreferencesOf(session.DeckPreference));
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
            snapshot.Ingredients.Where(item => !item.Assumed).Select(item => new ShoppingPlanCardIngredientView(item.Name, item.Quantity, item.Unit, item.Optional)).ToList(),
            snapshot.Component is { } component ? new ShoppingPlanComponentView(component.OutputName, component.OutputQuantity, component.OutputUnit, component.UsedByNames) : null,
            snapshot.RequestText);
    }

    private static PlanDeckRequest BuildRequest(ShoppingPlanSession session, IReadOnlyList<ShoppingPlanCard> cards, LoadedRank loaded, RecipeSuggestionGuidance? guidance, string? language, IReadOnlyList<string>? alsoRefused = null, ShoppingPlanBatchAsk? ask = null, string? request = null, ShoppingPlanCard? like = null)
    {
        var setup = session.Setup;
        var needs = new Dictionary<string, int>(setup.MealNeeds(), StringComparer.Ordinal);
        foreach (var card in cards.Where(card => card.State == ShoppingPlanCardState.Accepted && card.Snapshot.Component is null))
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
        var preference = session.DeckPreference;
        var minutesLeft = setup.PrepMinutes is { } minutes ? Math.Max(0, minutes - ShoppingPlanRanker.SessionMinutesUsed(loaded.Context.Accepted)) : (int?)null;
        // A request for one meal asks only for it, so six dishes come back for what the person is choosing.
        var mealTypes = ask is not null
            ? [ask.MealType]
            : setup.MealTypes.Where(meal => needs.GetValueOrDefault(ShoppingPlanSetup.GroupOf(meal)) > 0).DefaultIfEmpty(setup.MealTypes[0]).ToList();
        var mealNeeds = ask is not null
            ? new Dictionary<string, int> { [ShoppingPlanSetup.GroupOf(ask.MealType)] = Math.Max(1, needs.GetValueOrDefault(ShoppingPlanSetup.GroupOf(ask.MealType))) }
            : needs;
        // A base only makes sense for main meals: nobody prepares a base for snacks.
        var cookingStyle = mealTypes.Any(meal => ShoppingPlanSetup.GroupOf(meal) == "main") ? setup.CookingStyle : "daily";
        // "Ver outras" must not offer again what is already on screen.
        var onOffer = ask is not null && request is null
            ? cards.Where(card => card.State == ShoppingPlanCardState.Unseen && ShoppingPlanSetup.GroupOf(card.Snapshot.MealType) == ShoppingPlanSetup.GroupOf(ask.MealType)).Select(card => card.Snapshot.Name)
            : [];
        var brief = new PlanDeckBrief(
            setup.Days,
            cookingStyle,
            loaded.Context.Priority,
            mealNeeds,
            mealTypes,
            minutesLeft,
            cards.Where(card => card.State == ShoppingPlanCardState.Accepted).Select(card => card.Snapshot.Name).ToList(),
            preference?.Pantry == "free" ? [] : projection.ListNames.Distinct(StringComparer.Ordinal).ToList(),
            // The list is cut at thirty, so what matters most for this request goes first.
            (alsoRefused ?? []).Concat(onOffer).Concat(cards.Where(card => card.State == ShoppingPlanCardState.Rejected).OrderByDescending(card => card.DecidedAt).Select(card => card.Snapshot.Name)).ToList(),
            avoid,
            setup.AnchorIngredient,
            // Without an answer, the model stops favoring the pantry when the ranking does.
            preference?.Pantry ?? (loaded.Context.PantryWeight == 0 ? "free" : null),
            ask is not null ? (like is null ? null : "similar") : preference?.Similarity,
            ask is not null ? like?.Snapshot.Name : preference?.ReferenceDish,
            ask is not null ? null : preference?.Hint,
            preference?.UsePreferences ?? true,
            request,
            setup.MealCounts is not null);
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

    // A menu in use being changed (Amending) takes single-dish commands: choosing, undoing, removing a
    // card, and preparing its recipe.
    private static bool IsOpen(ShoppingPlanSession session) => session.Status is ShoppingPlanStatus.Building or ShoppingPlanStatus.Reviewing or ShoppingPlanStatus.Amending;

    // What changes the whole menu (counts, order, days, review, discarding) needs a draft.
    private static bool IsDraft(ShoppingPlanSession session) => session.Status is ShoppingPlanStatus.Building or ShoppingPlanStatus.Reviewing;

    private static string Name(ShoppingPlanStatus status) => status.ToString().ToLowerInvariant();

    private static int PeopleOf(ShoppingPlanCard card, ShoppingPlanSetup setup) => card.Snapshot.People ?? setup.People;

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
