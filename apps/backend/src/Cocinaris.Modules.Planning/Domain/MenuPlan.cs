namespace Cocinaris.Modules.Planning.Domain;

/// <summary>Lifecycle of a menu plan.</summary>
public enum MenuPlanStatus
{
    /// <summary>Editable candidate; its shopping list is a simulation and it is not shown on the home page.</summary>
    Draft,
    /// <summary>The operating plan: its entries for the day appear on the home page. It stays editable entry by entry.</summary>
    Accepted,
    /// <summary>Finished or abandoned; read-only and hidden from the default list.</summary>
    Archived
}

/// <summary>Whether the owner still intends to cook a menu entry. Cooking and cooked are derived from executions.</summary>
public enum MenuEntryStatus
{
    /// <summary>Still intended.</summary>
    Planned,
    /// <summary>The owner decided not to cook it.</summary>
    Skipped
}

/// <summary>
/// A user-owned menu for a period of up to 31 days (PLAN-0029). Menu planning is optional: a plan
/// is an intention and a source of shopping, never an obligation, and accepting it creates no
/// inventory reservations in this phase.
/// </summary>
public sealed class MenuPlan
{
    /// <summary>Longest period a plan may cover, in days.</summary>
    public const int MaxDays = 31;
    /// <summary>Maximum entries per plan.</summary>
    public const int MaxEntries = 60;
    private const int MaxNameLength = 80;

    private MenuPlan()
    {
        Name = string.Empty;
    }

    /// <summary>Gets the plan identifier.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the owner.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the owner's name for the plan; may be empty.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the first day of the period, in the owner's calendar.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Gets the last day of the period, inclusive.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Gets the lifecycle status.</summary>
    public MenuPlanStatus Status { get; private set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Gets the UTC instant of the last change to the plan or its entries.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the UTC instant the plan was accepted.</summary>
    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>Gets the idempotency key of the creating request.</summary>
    public Guid CreationKey { get; private init; }

    /// <summary>Creates a draft plan.</summary>
    /// <exception cref="ArgumentException">The name is too long or the period is empty or longer than <see cref="MaxDays"/>.</exception>
    public static MenuPlan Create(Guid ownerUserId, Guid creationKey, string? name, DateOnly startDate, DateOnly endDate, DateTimeOffset now)
    {
        var plan = new MenuPlan
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            CreationKey = creationKey,
            Status = MenuPlanStatus.Draft,
            CreatedAt = now
        };
        plan.Change(name, startDate, endDate, now);
        return plan;
    }

    /// <summary>Restores a persisted plan.</summary>
    public static MenuPlan Restore(Guid id, Guid ownerUserId, Guid creationKey, string name, DateOnly startDate, DateOnly endDate, MenuPlanStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset? acceptedAt) => new()
    {
        Id = id,
        OwnerUserId = ownerUserId,
        CreationKey = creationKey,
        Name = name,
        StartDate = startDate,
        EndDate = endDate,
        Status = status,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        AcceptedAt = acceptedAt
    };

    /// <summary>Validates a period without creating a plan.</summary>
    public static bool IsValidPeriod(DateOnly startDate, DateOnly endDate) =>
        endDate >= startDate && endDate.DayNumber - startDate.DayNumber < MaxDays;

    /// <summary>Renames the plan or changes its period. Callers check that dated entries still fit.</summary>
    /// <exception cref="ArgumentException">The name or period is invalid.</exception>
    /// <exception cref="InvalidOperationException">The plan is archived.</exception>
    public void Change(string? name, DateOnly startDate, DateOnly endDate, DateTimeOffset now)
    {
        EnsureEditable();
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException("A menu name must be at most 80 characters.", nameof(name));
        }

        if (!IsValidPeriod(startDate, endDate))
        {
            throw new ArgumentException("A menu covers one to 31 days.", nameof(endDate));
        }

        Name = trimmed;
        StartDate = startDate;
        EndDate = endDate;
        UpdatedAt = now;
    }

    /// <summary>Makes the plan the operating plan. Accepting again is a no-op.</summary>
    /// <exception cref="InvalidOperationException">The plan is archived.</exception>
    public void Accept(DateTimeOffset now)
    {
        EnsureEditable();
        if (Status == MenuPlanStatus.Accepted)
        {
            return;
        }

        Status = MenuPlanStatus.Accepted;
        AcceptedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Archives the plan; its entries and shopping checks are kept read-only.</summary>
    public void Archive(DateTimeOffset now)
    {
        Status = MenuPlanStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Records that an entry changed.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>Whether a day falls inside the period.</summary>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>Throws when the plan can no longer change.</summary>
    /// <exception cref="InvalidOperationException">The plan is archived.</exception>
    public void EnsureEditable()
    {
        if (Status == MenuPlanStatus.Archived)
        {
            throw new InvalidOperationException("An archived menu cannot change.");
        }
    }
}

/// <summary>
/// One intended meal in a plan: a recipe, an optional day (no day means "some time in the
/// period"), an optional meal, and the portions. It follows the recipe's current revision unless
/// a revision was pinned explicitly.
/// </summary>
public sealed class MenuEntry
{
    /// <summary>Meal types an entry may name.</summary>
    public static readonly IReadOnlySet<string> MealTypes = new HashSet<string>(StringComparer.Ordinal) { "breakfast", "lunch", "dinner", "snack" };

    private MenuEntry()
    {
    }

    /// <summary>Gets the entry identifier.</summary>
    public Guid Id { get; private init; }

    /// <summary>Gets the plan.</summary>
    public Guid MenuPlanId { get; private init; }

    /// <summary>Gets the owner.</summary>
    public Guid OwnerUserId { get; private init; }

    /// <summary>Gets the recipe to cook.</summary>
    public Guid RecipeId { get; private init; }

    /// <summary>Gets the pinned revision; <see langword="null"/> follows the current revision.</summary>
    public int? PinnedRevisionNumber { get; private set; }

    /// <summary>Gets the intended day; <see langword="null"/> is flexible within the period.</summary>
    public DateOnly? PlannedDate { get; private set; }

    /// <summary>Gets the intended meal (<c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c>), if any.</summary>
    public string? MealType { get; private set; }

    /// <summary>Gets the portions to cook, 1 to 24.</summary>
    public int Servings { get; private set; }

    /// <summary>Gets whether the owner still intends to cook it.</summary>
    public MenuEntryStatus Status { get; private set; }

    /// <summary>Gets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Gets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the idempotency key of the creating request.</summary>
    public Guid CreationKey { get; private init; }

    /// <summary>Adds an entry to an editable plan.</summary>
    /// <exception cref="ArgumentException">The day is outside the period, or the meal or portions are invalid.</exception>
    /// <exception cref="InvalidOperationException">The plan is archived.</exception>
    public static MenuEntry Create(MenuPlan plan, Guid creationKey, Guid recipeId, int? pinnedRevisionNumber, DateOnly? plannedDate, string? mealType, int servings, DateTimeOffset now)
    {
        var entry = new MenuEntry
        {
            Id = Guid.NewGuid(),
            MenuPlanId = plan.Id,
            OwnerUserId = plan.OwnerUserId,
            RecipeId = recipeId,
            PinnedRevisionNumber = pinnedRevisionNumber,
            CreationKey = creationKey,
            CreatedAt = now
        };
        entry.Change(plan, plannedDate, mealType, servings, MenuEntryStatus.Planned, now);
        return entry;
    }

    /// <summary>Restores a persisted entry.</summary>
    public static MenuEntry Restore(Guid id, Guid menuPlanId, Guid ownerUserId, Guid creationKey, Guid recipeId, int? pinnedRevisionNumber, DateOnly? plannedDate, string? mealType, int servings, MenuEntryStatus status, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
    {
        Id = id,
        MenuPlanId = menuPlanId,
        OwnerUserId = ownerUserId,
        CreationKey = creationKey,
        RecipeId = recipeId,
        PinnedRevisionNumber = pinnedRevisionNumber,
        PlannedDate = plannedDate,
        MealType = mealType,
        Servings = servings,
        Status = status,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };

    /// <summary>Moves, re-portions, skips, or restores the entry. Changing one entry never touches another.</summary>
    /// <exception cref="ArgumentException">The day is outside the period, or the meal or portions are invalid.</exception>
    /// <exception cref="InvalidOperationException">The plan is archived.</exception>
    public void Change(MenuPlan plan, DateOnly? plannedDate, string? mealType, int servings, MenuEntryStatus status, DateTimeOffset now)
    {
        plan.EnsureEditable();
        if (plannedDate is { } date && !plan.Contains(date))
        {
            throw new ArgumentException("The day must fall inside the menu's period.", nameof(plannedDate));
        }

        if (mealType is not null && !MealTypes.Contains(mealType))
        {
            throw new ArgumentException("The meal must be breakfast, lunch, dinner, or snack.", nameof(mealType));
        }

        if (servings is < 1 or > 24)
        {
            throw new ArgumentException("Servings must be between 1 and 24.", nameof(servings));
        }

        PlannedDate = plannedDate;
        MealType = mealType;
        Servings = servings;
        Status = status;
        UpdatedAt = now;
    }
}
