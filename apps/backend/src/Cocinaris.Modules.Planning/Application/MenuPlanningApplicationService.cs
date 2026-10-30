using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Ingredients;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Planning.Domain;
using Cocinaris.Modules.Recipes.Application;
using Cocinaris.Modules.Recipes.Domain;

namespace Cocinaris.Modules.Planning.Application;

/// <summary>Owner-scoped persistence port for menu plans, entries, and shopping check marks.</summary>
public interface IMenuPlanStore
{
    /// <summary>Lists the owner's plans, newest period first; archived plans only when asked for.</summary>
    Task<IReadOnlyList<MenuPlan>> ListAsync(Guid ownerUserId, bool archived, CancellationToken cancellationToken);

    /// <summary>Lists the owner's accepted plans whose period contains <paramref name="date"/>.</summary>
    Task<IReadOnlyList<MenuPlan>> ListAcceptedOnAsync(Guid ownerUserId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Lists the owner's accepted plans that end on or after <paramref name="from"/>.</summary>
    Task<IReadOnlyList<MenuPlan>> ListAcceptedFromAsync(Guid ownerUserId, DateOnly from, CancellationToken cancellationToken);

    /// <summary>Lists the owner's lot protections.</summary>
    Task<IReadOnlyList<LotProtection>> ListProtectionsAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Sets (inserts or replaces) the protection of one lot.</summary>
    Task SetProtectionAsync(LotProtection protection, CancellationToken cancellationToken);

    /// <summary>Removes the protection of one lot; removing an absent protection is a no-op.</summary>
    Task RemoveProtectionAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken);

    /// <summary>Finds one owned plan.</summary>
    Task<MenuPlan?> FindAsync(Guid ownerUserId, Guid planId, CancellationToken cancellationToken);

    /// <summary>Finds the plan a creation request made, by its key.</summary>
    Task<MenuPlan?> FindByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken);

    /// <summary>Lists the entries of owned plans.</summary>
    Task<IReadOnlyList<MenuEntry>> ListEntriesAsync(Guid ownerUserId, IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken);

    /// <summary>Finds the entry an add request made, by its key.</summary>
    Task<MenuEntry?> FindEntryByCreationKeyAsync(Guid ownerUserId, Guid creationKey, CancellationToken cancellationToken);

    /// <summary>Adds a plan. Returns false, persisting nothing, when the creation key was already used.</summary>
    Task<bool> TryAddAsync(MenuPlan plan, CancellationToken cancellationToken);

    /// <summary>Adds an entry and saves the plan's change instant. Returns false, persisting nothing, when the key was already used.</summary>
    Task<bool> TryAddEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken);

    /// <summary>Saves a plan's own fields.</summary>
    Task SaveAsync(MenuPlan plan, CancellationToken cancellationToken);

    /// <summary>Saves an entry and the plan's change instant.</summary>
    Task SaveEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken);

    /// <summary>Removes an entry; cooking sessions started from it keep their history and lose the link.</summary>
    Task RemoveEntryAsync(MenuPlan plan, MenuEntry entry, CancellationToken cancellationToken);

    /// <summary>Lists the shopping item keys checked off in a plan.</summary>
    Task<IReadOnlySet<string>> ListCheckedAsync(Guid ownerUserId, Guid planId, CancellationToken cancellationToken);

    /// <summary>Checks or unchecks a shopping item; repeating the same state is a no-op.</summary>
    Task SetCheckedAsync(Guid ownerUserId, Guid planId, string itemKey, bool isChecked, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Lists the package sizes the owner confirmed, by shopping item key.</summary>
    Task<IReadOnlyDictionary<string, PackageSize>> ListPackageSizesAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Sets (inserts or replaces) the package size of one item.</summary>
    Task SetPackageSizeAsync(PackageSize packageSize, CancellationToken cancellationToken);

    /// <summary>Removes the package size of one item; removing an absent size is a no-op.</summary>
    Task RemovePackageSizeAsync(Guid ownerUserId, string itemKey, CancellationToken cancellationToken);
}

/// <summary>Plan list row.</summary>
public sealed record MenuPlanSummaryView(Guid PlanId, string Name, DateOnly StartDate, DateOnly EndDate, string Status, int EntryCount, DateTimeOffset UpdatedAt);

/// <summary>
/// One entry with its recipe and derived state. <paramref name="Status"/> is <c>planned</c>,
/// <c>skipped</c>, <c>cooking</c> (a session started from it is in progress), or <c>cooked</c>.
/// <paramref name="Readiness"/> is present for meals still to cook that reserve ingredients: for an
/// accepted menu it reflects real reservations; for a draft it simulates the menu being accepted
/// after the menus already in use.
/// </summary>
public sealed record MenuEntryView(
    Guid EntryId,
    Guid RecipeId,
    string RecipeName,
    int RevisionNumber,
    bool RevisionPinned,
    DateOnly? PlannedDate,
    string? MealType,
    int Servings,
    string Status,
    Guid? ExecutionId,
    bool ReserveIngredients = true,
    MealReadinessView? Readiness = null);

/// <summary>A plan with its entries, ordered by day (flexible last), meal, and creation.</summary>
public sealed record MenuPlanView(Guid PlanId, string Name, DateOnly StartDate, DateOnly EndDate, string Status, DateTimeOffset? AcceptedAt, DateTimeOffset UpdatedAt, IReadOnlyList<MenuEntryView> Entries);

/// <summary>A plan's shopping list from <paramref name="From"/> on.</summary>
/// <param name="PlanId">Plan.</param>
/// <param name="From">First day considered; earlier dated meals are treated as missed.</param>
/// <param name="MealCount">Meals included.</param>
/// <param name="Items">Shopping lines.</param>
public sealed record ShoppingListView(Guid PlanId, DateOnly From, int MealCount, IReadOnlyList<ShoppingItemView> Items);

/// <summary>An entry of an accepted plan relevant to one day.</summary>
public sealed record TodayMenuEntryView(Guid PlanId, string PlanName, MenuEntryView Entry);

/// <summary>Creates a draft plan.</summary>
public sealed record CreateMenuPlanCommand(Guid? IdempotencyKey, string? Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Renames a plan or changes its period.</summary>
public sealed record ChangeMenuPlanCommand(Guid PlanId, string? Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Adds a recipe to a plan.</summary>
/// <param name="PlanId">Plan.</param>
/// <param name="IdempotencyKey">Request key; a retry returns the entry already added.</param>
/// <param name="RecipeId">Owned recipe.</param>
/// <param name="PlannedDate">Day inside the period, or null for flexible.</param>
/// <param name="MealType">Meal, if any.</param>
/// <param name="Servings">Portions; the recipe's servings when null.</param>
/// <param name="PinRevision">Pin the recipe's current revision instead of following later edits.</param>
public sealed record AddMenuEntryCommand(Guid PlanId, Guid? IdempotencyKey, Guid RecipeId, DateOnly? PlannedDate, string? MealType, int? Servings, bool PinRevision);

/// <summary>Moves, re-portions, skips, or restores an entry, and optionally releases or restores its reservations.</summary>
public sealed record ChangeMenuEntryCommand(Guid PlanId, Guid EntryId, DateOnly? PlannedDate, string? MealType, int Servings, string? Status, bool? ReserveIngredients = null);

/// <summary>One lot with what is promised from it.</summary>
/// <param name="LotId">Lot.</param>
/// <param name="ProductName">Product name.</param>
/// <param name="Unit">Lot unit.</param>
/// <param name="Quantity">Physical quantity.</param>
/// <param name="ProtectedQuantity">Quantity the owner locked.</param>
/// <param name="ReservedQuantity">Quantity reserved for accepted menu meals.</param>
/// <param name="FreeQuantity">What is neither protected nor reserved.</param>
/// <param name="Protection">The lot's protection rule, if any.</param>
/// <param name="Reservations">Meals counting on the lot.</param>
public sealed record LotCommitmentView(Guid LotId, string ProductName, string Unit, decimal Quantity, decimal ProtectedQuantity, decimal ReservedQuantity, decimal FreeQuantity, LotProtectionView? Protection, IReadOnlyList<LotReservationView> Reservations);

/// <summary>A protection rule: <paramref name="Kind"/> is <c>whole</c> or <c>minimum</c>.</summary>
public sealed record LotProtectionView(string Kind, decimal? MinimumQuantity);

/// <summary>The owner's pantry commitments and the readiness of each reserving meal.</summary>
public sealed record ReservationsView(DateOnly From, IReadOnlyList<LotCommitmentView> Lots, IReadOnlyList<MealReadinessView> Meals);

/// <summary>Pre-meal reminders for the dated meals of menus in use from <paramref name="From"/>, for <paramref name="Days"/> days.</summary>
public sealed record PreparationView(DateOnly From, int Days, IReadOnlyList<PreparationReminderView> Reminders);

/// <summary>
/// Menu planning (PLAN-0029): draft and accepted plans, entries, the menu of the day for the home
/// page, and the shopping projection. Planning is optional and never mutates inventory, recipes,
/// or cooking sessions; it reads them through their application ports.
/// </summary>
public sealed class MenuPlanningApplicationService(
    ICurrentUserAccessor currentUser,
    IMenuPlanStore store,
    IRecipeLibraryStore recipes,
    IRecipeExecutionStore executions,
    IInventoryLotReadStore inventory,
    IIngredientMatching matching,
    TimeProvider timeProvider)
{
    private const int MaxPantryLots = 500;
    private const int MaxItemKeyLength = PackageSize.MaxItemKeyLength;
    private const int MaxReminderDays = 7;
    private static readonly string[] MealOrder = ["breakfast", "lunch", "snack", "dinner"];

    /// <summary>Lists the owner's plans.</summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<MenuPlanSummaryView>>> ListAsync(bool archived, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plans = await store.ListAsync(user.Id, archived, cancellationToken);
        var entries = await store.ListEntriesAsync(user.Id, plans.Select(plan => plan.Id).ToList(), cancellationToken);
        var counts = entries.GroupBy(entry => entry.MenuPlanId).ToDictionary(group => group.Key, group => group.Count());
        return RecipeApplicationResult<IReadOnlyList<MenuPlanSummaryView>>.Succeeded(plans
            .Select(plan => new MenuPlanSummaryView(plan.Id, plan.Name, plan.StartDate, plan.EndDate, Name(plan.Status), counts.GetValueOrDefault(plan.Id), plan.UpdatedAt))
            .ToList());
    }

    /// <summary>Gets one owned plan with its entries.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> GetAsync(Guid planId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, planId, cancellationToken);
        return plan is null ? NotFound<MenuPlanView>() : Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
    }

    /// <summary>Creates a draft plan; a retry with the same key returns it.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> CreateAsync(CreateMenuPlanCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<MenuPlanView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        if (Validate(command.Name, command.StartDate, command.EndDate) is { } invalid)
        {
            return invalid;
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (await store.FindByCreationKeyAsync(user.Id, key, cancellationToken) is { } existing)
        {
            return Succeeded(await ViewAsync(user.Id, existing, cancellationToken));
        }

        var plan = MenuPlan.Create(user.Id, key, command.Name, command.StartDate!.Value, command.EndDate!.Value, timeProvider.GetUtcNow());
        if (!await store.TryAddAsync(plan, cancellationToken))
        {
            plan = await store.FindByCreationKeyAsync(user.Id, key, cancellationToken)
                ?? throw new InvalidOperationException("A menu creation key conflicted but the menu could not be reloaded.");
            return Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
        }

        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken), RecipeApplicationSuccess.Created);
    }

    /// <summary>Renames a plan or changes its period; dated entries must still fit.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> ChangeAsync(ChangeMenuPlanCommand command, CancellationToken cancellationToken)
    {
        if (Validate(command.Name, command.StartDate, command.EndDate) is { } invalid)
        {
            return invalid;
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, command.PlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<MenuPlanView>();
        }

        if (plan.Status == MenuPlanStatus.Archived)
        {
            return Archived<MenuPlanView>();
        }

        var entries = await store.ListEntriesAsync(user.Id, [plan.Id], cancellationToken);
        if (entries.Any(entry => entry.PlannedDate is { } date && (date < command.StartDate || date > command.EndDate)))
        {
            return Failure<MenuPlanView>("domain_rule_violated", "Some meals are on days outside the new period. Move or remove them first.");
        }

        plan.Change(command.Name, command.StartDate!.Value, command.EndDate!.Value, timeProvider.GetUtcNow());
        await store.SaveAsync(plan, cancellationToken);
        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
    }

    /// <summary>Accepts a plan: its entries for the day appear on the home page. No inventory is reserved.</summary>
    public Task<RecipeApplicationResult<MenuPlanView>> AcceptAsync(Guid planId, CancellationToken cancellationToken) =>
        TransitionAsync(planId, plan => plan.Accept(timeProvider.GetUtcNow()), cancellationToken);

    /// <summary>Archives a plan; it becomes read-only.</summary>
    public Task<RecipeApplicationResult<MenuPlanView>> ArchiveAsync(Guid planId, CancellationToken cancellationToken) =>
        TransitionAsync(planId, plan => plan.Archive(timeProvider.GetUtcNow()), cancellationToken);

    /// <summary>Adds a recipe to a plan; a retry with the same key returns the plan unchanged.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> AddEntryAsync(AddMenuEntryCommand command, CancellationToken cancellationToken)
    {
        if (command.IdempotencyKey is not Guid key)
        {
            return Failure<MenuPlanView>("validation_failed", "A UUID Idempotency-Key header is required.");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, command.PlanId, cancellationToken);
        if (plan is null)
        {
            return NotFound<MenuPlanView>();
        }

        if (await store.FindEntryByCreationKeyAsync(user.Id, key, cancellationToken) is { } existing)
        {
            return existing.MenuPlanId == plan.Id
                ? Succeeded(await ViewAsync(user.Id, plan, cancellationToken))
                : Failure<MenuPlanView>("domain_rule_violated", "This Idempotency-Key was already used for another menu.");
        }

        if (plan.Status == MenuPlanStatus.Archived)
        {
            return Archived<MenuPlanView>();
        }

        var recipe = await recipes.FindAsync(user.Id, command.RecipeId, cancellationToken);
        if (recipe is null)
        {
            return Failure<MenuPlanView>("validation_failed", "The recipe was not found.", "recipeId");
        }

        if ((await store.ListEntriesAsync(user.Id, [plan.Id], cancellationToken)).Count >= MenuPlan.MaxEntries)
        {
            return Failure<MenuPlanView>("domain_rule_violated", "A menu holds at most 60 meals.");
        }

        var servings = command.Servings ?? (await recipes.FindRevisionAsync(user.Id, recipe.Id, recipe.CurrentRevisionNumber, cancellationToken))?.Servings ?? 2;
        if (EntryProblem(plan, command.PlannedDate, command.MealType, servings) is { } problem)
        {
            return problem;
        }

        var now = timeProvider.GetUtcNow();
        var entry = MenuEntry.Create(plan, key, recipe.Id, command.PinRevision ? recipe.CurrentRevisionNumber : null, command.PlannedDate, command.MealType, servings, now);
        plan.Touch(now);
        if (!await store.TryAddEntryAsync(plan, entry, cancellationToken))
        {
            var raced = await store.FindAsync(user.Id, plan.Id, cancellationToken) ?? plan;
            return Succeeded(await ViewAsync(user.Id, raced, cancellationToken));
        }

        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken), RecipeApplicationSuccess.Created);
    }

    /// <summary>Moves, re-portions, skips, or restores one entry; no other entry changes.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> ChangeEntryAsync(ChangeMenuEntryCommand command, CancellationToken cancellationToken)
    {
        var status = command.Status switch
        {
            null or "planned" => MenuEntryStatus.Planned,
            "skipped" => MenuEntryStatus.Skipped,
            _ => (MenuEntryStatus?)null
        };
        if (status is null)
        {
            return Failure<MenuPlanView>("validation_failed", "The status must be planned or skipped.", "status");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (plan, entry) = await FindEntryAsync(user.Id, command.PlanId, command.EntryId, cancellationToken);
        if (plan is null || entry is null)
        {
            return NotFound<MenuPlanView>();
        }

        if (plan.Status == MenuPlanStatus.Archived)
        {
            return Archived<MenuPlanView>();
        }

        if (EntryProblem(plan, command.PlannedDate, command.MealType, command.Servings) is { } problem)
        {
            return problem;
        }

        var now = timeProvider.GetUtcNow();
        entry.Change(plan, command.PlannedDate, command.MealType, command.Servings, status.Value, now, command.ReserveIngredients);
        plan.Touch(now);
        await store.SaveEntryAsync(plan, entry, cancellationToken);
        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
    }

    /// <summary>Removes one entry.</summary>
    public async Task<RecipeApplicationResult<MenuPlanView>> RemoveEntryAsync(Guid planId, Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var (plan, entry) = await FindEntryAsync(user.Id, planId, entryId, cancellationToken);
        if (plan is null || entry is null)
        {
            return NotFound<MenuPlanView>();
        }

        if (plan.Status == MenuPlanStatus.Archived)
        {
            return Archived<MenuPlanView>();
        }

        plan.Touch(timeProvider.GetUtcNow());
        await store.RemoveEntryAsync(plan, entry, cancellationToken);
        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
    }

    /// <summary>
    /// Projects the shopping list of the meals still to cook: not skipped, not cooked, and dated
    /// <paramref name="from"/> or later, or flexible. Works for drafts too, as a simulation.
    /// </summary>
    /// <param name="planId">Plan.</param>
    /// <param name="from">The owner's current day; the server does not know the owner's time zone.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<ShoppingListView>> GetShoppingAsync(Guid planId, DateOnly? from, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, planId, cancellationToken);
        if (plan is null)
        {
            return NotFound<ShoppingListView>();
        }

        var start = from ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var resolved = await ResolveEntriesAsync(user.Id, await store.ListEntriesAsync(user.Id, [plan.Id], cancellationToken), cancellationToken);
        var meals = resolved
            .Where(item => item.View.Status is "planned" or "cooking" && (item.View.PlannedDate is null || item.View.PlannedDate >= start) && item.Content is not null)
            .Select(item => new ShoppingMeal(item.View.EntryId, item.View.RecipeName, item.View.PlannedDate, item.View.Servings, item.Content!.Servings, item.Content.NormalizedRecipeJson))
            .ToList();
        // Stock this menu can use: physical quantity minus protections and what other menus in use reserve.
        var lots = await inventory.ListActiveAsync(user.Id, MaxPantryLots, cancellationToken);
        var others = await ProjectAsync(user.Id, start, lots, simulateDraft: null, excludePlanId: plan.Id, cancellationToken);
        var usable = lots.Select(lot => others.Projection.Lots.TryGetValue(lot.LotId, out var commitment) && lot.MeasuredValue is { } value
                ? lot with { MeasuredValue = Math.Max(0, value - commitment.ProtectedQuantity - commitment.Reservations.Sum(item => item.Quantity)) }
                : lot)
            .ToList();
        var keys = await MatchKeysAsync(user.Id, lots, meals.Select(meal => meal.NormalizedRecipeJson), cancellationToken);
        var items = ShoppingProjector.Build(meals, usable, await store.ListCheckedAsync(user.Id, plan.Id, cancellationToken), keys, await store.ListPackageSizesAsync(user.Id, cancellationToken));
        return RecipeApplicationResult<ShoppingListView>.Succeeded(new ShoppingListView(plan.Id, start, meals.Count, items));
    }

    /// <summary>Checks or unchecks a shopping line. Checking never changes the pantry.</summary>
    public async Task<RecipeApplicationResult<bool>> SetCheckedAsync(Guid planId, string? itemKey, bool isChecked, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > MaxItemKeyLength)
        {
            return Failure<bool>("validation_failed", "The item key is required and must be at most 200 characters.", "itemKey");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, planId, cancellationToken);
        if (plan is null)
        {
            return NotFound<bool>();
        }

        await store.SetCheckedAsync(user.Id, plan.Id, itemKey, isChecked, timeProvider.GetUtcNow(), cancellationToken);
        return RecipeApplicationResult<bool>.Succeeded(isChecked);
    }

    /// <summary>
    /// Records the package size the owner buys an item in, or forgets it when
    /// <paramref name="size"/> is null (PLAN-0029 Phase 4). The size applies to every menu's list
    /// and is in the unit of the item key. It never changes the pantry or a menu.
    /// </summary>
    /// <param name="itemKey">Shopping item key (catalog ingredient or normalized name, and unit).</param>
    /// <param name="size">Size of one package, or null to remove it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The size kept, or null when removed.</returns>
    public async Task<RecipeApplicationResult<decimal?>> SetPackageSizeAsync(string? itemKey, decimal? size, CancellationToken cancellationToken)
    {
        // An item key always ends in a canonical unit, so a size is never stored for an unknown unit.
        if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > MaxItemKeyLength || !(itemKey.EndsWith("|Gram", StringComparison.Ordinal) || itemKey.EndsWith("|Milliliter", StringComparison.Ordinal) || itemKey.EndsWith("|Unit", StringComparison.Ordinal)))
        {
            return Failure<decimal?>("validation_failed", "The item key from the shopping list is required.", "itemKey");
        }

        if (size is <= 0 or > PackageSize.MaxSize)
        {
            return Failure<decimal?>("validation_failed", "A package size must be greater than zero and at most 100,000.", "size");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        if (size is null)
        {
            await store.RemovePackageSizeAsync(user.Id, itemKey, cancellationToken);
            return RecipeApplicationResult<decimal?>.Succeeded(null);
        }

        await store.SetPackageSizeAsync(PackageSize.Create(user.Id, itemKey, size.Value, timeProvider.GetUtcNow()), cancellationToken);
        return RecipeApplicationResult<decimal?>.Succeeded(size);
    }

    /// <summary>
    /// Pre-meal readiness checks for the dated meals of menus in use between
    /// <paramref name="from"/> and the following <paramref name="days"/> − 1 days (PLAN-0029
    /// Phase 4): what to buy, what to thaw, and reserved lots whose date falls before the meal.
    /// Read-only and recomputed on every read; reminders are shown in the app only.
    /// </summary>
    /// <param name="from">The owner's current day; the server does not know the owner's time zone.</param>
    /// <param name="days">Days to check, 1 to 7; 2 (today and tomorrow) when omitted.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<RecipeApplicationResult<PreparationView>> GetPreparationAsync(DateOnly? from, int? days, CancellationToken cancellationToken)
    {
        if (days is < 1 or > MaxReminderDays)
        {
            return Failure<PreparationView>("validation_failed", "Check one to seven days.", "days");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var start = from ?? ConservativeToday();
        var span = days ?? 2;
        var end = start.AddDays(span);
        var lots = await inventory.ListActiveAsync(user.Id, MaxPantryLots, cancellationToken);
        var projected = await ProjectAsync(user.Id, start, lots, simulateDraft: null, excludePlanId: null, cancellationToken);
        var meals = projected.Meals
            .Where(item => item.View.PlannedDate is { } date && date >= start && date < end)
            .Select(item => new ReminderMeal(item.PlanId, item.View.EntryId, item.View.RecipeName, item.View.PlannedDate!.Value, item.View.MealType))
            .ToList();
        return RecipeApplicationResult<PreparationView>.Succeeded(new PreparationView(start, span, PreparationReminders.Build(meals, projected.Projection, lots, start)));
    }

    /// <summary>
    /// Entries of accepted plans relevant to <paramref name="date"/>: dated that day, or flexible
    /// in a period containing it. Skipped and cooked entries are left out.
    /// </summary>
    public async Task<RecipeApplicationResult<IReadOnlyList<TodayMenuEntryView>>> GetTodayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plans = await store.ListAcceptedOnAsync(user.Id, date, cancellationToken);
        var entries = (await store.ListEntriesAsync(user.Id, plans.Select(plan => plan.Id).ToList(), cancellationToken))
            .Where(entry => entry.PlannedDate is null || entry.PlannedDate == date)
            .ToList();
        var names = plans.ToDictionary(plan => plan.Id, plan => plan.Name);
        var resolved = await ResolveEntriesAsync(user.Id, entries, cancellationToken);
        return RecipeApplicationResult<IReadOnlyList<TodayMenuEntryView>>.Succeeded(resolved
            .Where(item => item.View.Status is "planned" or "cooking")
            // Meals dated today come before flexible ones.
            .OrderBy(item => item.View.PlannedDate is null)
            .Select(item => new TodayMenuEntryView(item.PlanId, names[item.PlanId], item.View))
            .ToList());
    }

    private async Task<RecipeApplicationResult<MenuPlanView>> TransitionAsync(Guid planId, Action<MenuPlan> transition, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var plan = await store.FindAsync(user.Id, planId, cancellationToken);
        if (plan is null)
        {
            return NotFound<MenuPlanView>();
        }

        try
        {
            transition(plan);
        }
        catch (InvalidOperationException)
        {
            return Archived<MenuPlanView>();
        }

        await store.SaveAsync(plan, cancellationToken);
        return Succeeded(await ViewAsync(user.Id, plan, cancellationToken));
    }

    private async Task<(MenuPlan? Plan, MenuEntry? Entry)> FindEntryAsync(Guid ownerUserId, Guid planId, Guid entryId, CancellationToken cancellationToken)
    {
        var plan = await store.FindAsync(ownerUserId, planId, cancellationToken);
        if (plan is null)
        {
            return (null, null);
        }

        var entry = (await store.ListEntriesAsync(ownerUserId, [plan.Id], cancellationToken)).FirstOrDefault(item => item.Id == entryId);
        return (plan, entry);
    }

    private async Task<MenuPlanView> ViewAsync(Guid ownerUserId, MenuPlan plan, CancellationToken cancellationToken)
    {
        var resolved = await ResolveEntriesAsync(ownerUserId, await store.ListEntriesAsync(ownerUserId, [plan.Id], cancellationToken), cancellationToken);
        var readiness = new Dictionary<Guid, MealReadinessView>();
        if (plan.Status != MenuPlanStatus.Archived)
        {
            var lots = await inventory.ListActiveAsync(ownerUserId, MaxPantryLots, cancellationToken);
            var projected = await ProjectAsync(ownerUserId, ConservativeToday(), lots, plan.Status == MenuPlanStatus.Draft ? plan : null, excludePlanId: null, cancellationToken);
            readiness = projected.Projection.Meals.ToDictionary();
        }

        return new MenuPlanView(plan.Id, plan.Name, plan.StartDate, plan.EndDate, Name(plan.Status), plan.AcceptedAt, plan.UpdatedAt, resolved
            .Select(item => item.View with { Readiness = readiness.GetValueOrDefault(item.View.EntryId) })
            .ToList());
    }

    /// <summary>
    /// The owner's pantry commitments from <paramref name="from"/>: protections and the reservations
    /// of accepted menus, plus each reserving meal's readiness. Read-only.
    /// </summary>
    public async Task<RecipeApplicationResult<ReservationsView>> GetReservationsAsync(DateOnly? from, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var start = from ?? ConservativeToday();
        var lots = await inventory.ListActiveAsync(user.Id, MaxPantryLots, cancellationToken);
        var projected = await ProjectAsync(user.Id, start, lots, simulateDraft: null, excludePlanId: null, cancellationToken);
        var protections = projected.Protections.ToDictionary(item => item.LotId);
        var views = lots
            .Where(lot => projected.Projection.Lots.ContainsKey(lot.LotId) || protections.ContainsKey(lot.LotId))
            .Select(lot =>
            {
                var commitment = projected.Projection.Lots.GetValueOrDefault(lot.LotId);
                var quantity = lot.MeasuredValue ?? 0;
                var reserved = commitment?.Reservations.Sum(item => item.Quantity) ?? 0;
                var protectedQuantity = commitment?.ProtectedQuantity ?? 0;
                var rule = protections.GetValueOrDefault(lot.LotId);
                return new LotCommitmentView(
                    lot.LotId,
                    lot.ProductName,
                    lot.MeasuredUnit ?? string.Empty,
                    quantity,
                    protectedQuantity,
                    reserved,
                    Math.Max(0, quantity - protectedQuantity - reserved),
                    rule is null ? null : new LotProtectionView(rule.Whole ? "whole" : "minimum", rule.MinimumQuantity),
                    commitment?.Reservations ?? []);
            })
            .OrderBy(item => item.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return RecipeApplicationResult<ReservationsView>.Succeeded(new ReservationsView(start, views, projected.Projection.Meals.Values.ToList()));
    }

    /// <summary>Protects a whole lot or a minimum quantity of it; replaces any earlier protection.</summary>
    public async Task<RecipeApplicationResult<ReservationsView>> ProtectAsync(Guid lotId, string? kind, decimal? minimumQuantity, CancellationToken cancellationToken)
    {
        if (kind is not ("whole" or "minimum") || (kind == "minimum") != minimumQuantity.HasValue || minimumQuantity is <= 0 or > 100_000m)
        {
            return Failure<ReservationsView>("validation_failed", "Protect the whole lot, or a minimum quantity greater than zero.", "minimumQuantity");
        }

        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var lot = await inventory.FindActiveAsync(user.Id, lotId, cancellationToken);
        if (lot is null)
        {
            return Failure<ReservationsView>("resource_not_found", "The pantry item was not found.");
        }

        if (kind == "minimum" && lot.MeasuredValue is null)
        {
            return Failure<ReservationsView>("validation_failed", "Only a measured item can keep a minimum quantity.", "minimumQuantity");
        }

        await store.SetProtectionAsync(LotProtection.Create(user.Id, lot.LotId, kind == "whole", minimumQuantity, timeProvider.GetUtcNow()), cancellationToken);
        return await GetReservationsAsync(null, cancellationToken);
    }

    /// <summary>Removes a lot's protection.</summary>
    public async Task<RecipeApplicationResult<ReservationsView>> UnprotectAsync(Guid lotId, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        await store.RemoveProtectionAsync(user.Id, lotId, cancellationToken);
        return await GetReservationsAsync(null, cancellationToken);
    }

    /// <summary>Commitments for the cooking proposal (<see cref="IPantryCommitmentLookup"/>).</summary>
    public async Task<IReadOnlyDictionary<Guid, LotCommitment>> GetCommitmentsAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var lots = await inventory.ListActiveAsync(ownerUserId, MaxPantryLots, cancellationToken);
        return (await ProjectAsync(ownerUserId, ConservativeToday(), lots, simulateDraft: null, excludePlanId: null, cancellationToken)).Projection.Lots;
    }

    // Reserving meals: entries of accepted menus (and of one simulated draft, after them) that are
    // still to cook, reserve ingredients, and are dated from `from` or flexible.
    private async Task<(ReservationProjection Projection, IReadOnlyList<LotProtection> Protections, IReadOnlyList<ResolvedEntry> Meals)> ProjectAsync(
        Guid ownerUserId, DateOnly from, IReadOnlyList<InventoryLotReadModel> lots, MenuPlan? simulateDraft, Guid? excludePlanId, CancellationToken cancellationToken)
    {
        var plans = (await store.ListAcceptedFromAsync(ownerUserId, from, cancellationToken)).Where(plan => plan.Id != excludePlanId).ToList();
        var accepted = plans.Select(plan => plan.Id).ToHashSet();
        if (simulateDraft is not null)
        {
            plans.Add(simulateDraft);
        }

        var entries = (await store.ListEntriesAsync(ownerUserId, plans.Select(plan => plan.Id).ToList(), cancellationToken))
            .Where(entry => entry.ReserveIngredients && (entry.PlannedDate is null || entry.PlannedDate >= from))
            .ToList();
        var resolved = await ResolveEntriesAsync(ownerUserId, entries, cancellationToken);
        var reserving = resolved
            .Where(item => item.View.Status is "planned" or "cooking" && item.Content is not null)
            // Menus in use reserve before a simulated draft; within each, the resolver's day and meal order holds.
            .OrderBy(item => !accepted.Contains(item.PlanId))
            .ToList();
        var meals = reserving
            .Select(item => new ReservingMeal(item.View.EntryId, item.View.RecipeName, item.View.PlannedDate, item.View.Servings, item.Content!.Servings, item.Content.NormalizedRecipeJson))
            .ToList();
        var protections = await store.ListProtectionsAsync(ownerUserId, cancellationToken);
        var keys = await MatchKeysAsync(ownerUserId, lots, meals.Select(meal => meal.NormalizedRecipeJson), cancellationToken);
        return (ReservationProjector.Build(meals, lots, protections, keys), protections, reserving.Where(item => accepted.Contains(item.PlanId)).ToList());
    }

    /// <summary>Matching keys for the pantry's product names and the meals' ingredient names (PLAN-0055).</summary>
    private Task<IngredientMatchKeys> MatchKeysAsync(Guid ownerUserId, IReadOnlyList<InventoryLotReadModel> lots, IEnumerable<string> recipeJson, CancellationToken cancellationToken) =>
        matching.KeysAsync(ownerUserId, lots.Select(lot => lot.ProductName).Concat(recipeJson.SelectMany(json => RecipeIngredientReader.Read(json).Select(ingredient => ingredient.Name))), cancellationToken);

    // The server does not know the owner's time zone. A meal keeps its reservation until its day
    // has ended everywhere (UTC-12), so a meal is never released early; one missed yesterday may
    // reserve for a few more hours.
    private DateOnly ConservativeToday() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime.AddHours(-12));

    private sealed record ResolvedEntry(Guid PlanId, MenuEntryView View, RecipeRevisionContent? Content);

    // Resolves each entry's recipe revision (current or pinned) and derives cooking state from the
    // sessions started from it. Revisions are cached per recipe and number within the request.
    private async Task<IReadOnlyList<ResolvedEntry>> ResolveEntriesAsync(Guid ownerUserId, IReadOnlyList<MenuEntry> entries, CancellationToken cancellationToken)
    {
        var sessions = (await executions.ListForMenuEntriesAsync(ownerUserId, entries.Select(entry => entry.Id).ToList(), cancellationToken))
            .ToLookup(execution => execution.MenuEntryId!.Value);
        var recipeCache = new Dictionary<Guid, Recipe?>();
        var revisionCache = new Dictionary<(Guid, int), RecipeRevisionContent?>();
        var resolved = new List<ResolvedEntry>();
        foreach (var entry in entries
            .OrderBy(item => item.PlannedDate is null)
            .ThenBy(item => item.PlannedDate)
            .ThenBy(item => item.MealType is null ? MealOrder.Length : Array.IndexOf(MealOrder, item.MealType))
            .ThenBy(item => item.CreatedAt))
        {
            if (!recipeCache.TryGetValue(entry.RecipeId, out var recipe))
            {
                recipeCache[entry.RecipeId] = recipe = await recipes.FindAsync(ownerUserId, entry.RecipeId, cancellationToken);
            }

            var number = entry.PinnedRevisionNumber ?? recipe?.CurrentRevisionNumber ?? 1;
            if (!revisionCache.TryGetValue((entry.RecipeId, number), out var content))
            {
                revisionCache[(entry.RecipeId, number)] = content = recipe is null ? null : await recipes.FindRevisionAsync(ownerUserId, entry.RecipeId, number, cancellationToken);
            }

            // Only a session of the same recipe counts; the latest one decides.
            var session = sessions[entry.Id].Where(item => item.RecipeId == entry.RecipeId).OrderByDescending(item => item.StartedAt).FirstOrDefault();
            var status = entry.Status == MenuEntryStatus.Skipped
                ? "skipped"
                : session?.Status switch
                {
                    RecipeExecutionStatus.Completed => "cooked",
                    RecipeExecutionStatus.InProgress => "cooking",
                    _ => "planned"
                };
            resolved.Add(new ResolvedEntry(entry.MenuPlanId, new MenuEntryView(
                entry.Id,
                entry.RecipeId,
                content?.Name ?? string.Empty,
                number,
                entry.PinnedRevisionNumber is not null,
                entry.PlannedDate,
                entry.MealType,
                entry.Servings,
                status,
                session?.Status is RecipeExecutionStatus.Completed or RecipeExecutionStatus.InProgress ? session.Id : null,
                entry.ReserveIngredients), content));
        }

        return resolved;
    }

    private static RecipeApplicationResult<MenuPlanView>? Validate(string? name, DateOnly? startDate, DateOnly? endDate)
    {
        if (name?.Trim().Length > 80)
        {
            return Failure<MenuPlanView>("validation_failed", "The name must be at most 80 characters.", "name");
        }

        if (startDate is null || endDate is null || !MenuPlan.IsValidPeriod(startDate.Value, endDate.Value))
        {
            return Failure<MenuPlanView>("validation_failed", "A menu covers one to 31 days.", "endDate");
        }

        return null;
    }

    private static RecipeApplicationResult<MenuPlanView>? EntryProblem(MenuPlan plan, DateOnly? plannedDate, string? mealType, int servings)
    {
        if (plannedDate is { } date && !plan.Contains(date))
        {
            return Failure<MenuPlanView>("validation_failed", "The day must fall inside the menu's period.", "plannedDate");
        }

        if (mealType is not null && !MenuEntry.MealTypes.Contains(mealType))
        {
            return Failure<MenuPlanView>("validation_failed", "The meal must be breakfast, lunch, dinner, or snack.", "mealType");
        }

        return servings is < 1 or > 24 ? Failure<MenuPlanView>("validation_failed", "Servings must be between 1 and 24.", "servings") : null;
    }

    private static string Name(MenuPlanStatus status) => status.ToString().ToLowerInvariant();

    private static RecipeApplicationResult<T> Succeeded<T>(T value, RecipeApplicationSuccess success = RecipeApplicationSuccess.Succeeded) =>
        RecipeApplicationResult<T>.Succeeded(value, success);

    private static RecipeApplicationResult<T> NotFound<T>() => Failure<T>("resource_not_found", "The menu or meal was not found.");

    private static RecipeApplicationResult<T> Archived<T>() => Failure<T>("domain_rule_violated", "An archived menu cannot change.");

    private static RecipeApplicationResult<T> Failure<T>(string code, string detail, string? field = null) =>
        RecipeApplicationResult<T>.Failure(code, detail, field is null ? null : new Dictionary<string, string[]> { [field] = [detail] });
}
