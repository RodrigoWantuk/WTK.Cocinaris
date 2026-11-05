namespace Cocinaris.Modules.Planning.Domain;

/// <summary>
/// The answers of a shopping plan's setup screen (PLAN-0062). They belong to this plan and never
/// update the profile; the next plan reuses them only as defaults.
/// </summary>
/// <param name="Today">The owner's local day when the plan started; the server does not know the owner's time zone.</param>
/// <param name="Days">Days covered, 1 to 14.</param>
/// <param name="MealTypes">Meals to plan: <c>breakfast</c>, <c>lunch</c>, <c>dinner</c>, <c>snack</c>.</param>
/// <param name="MealsOut">Meals eaten away from home, taken off the total.</param>
/// <param name="CookingStyle"><c>once</c>, <c>mix</c>, or <c>daily</c>.</param>
/// <param name="Priority"><c>waste</c>, <c>short</c>, <c>save</c>, or <c>time</c>.</param>
/// <param name="PrepDate">Day of the preparation session; null for <c>daily</c>.</param>
/// <param name="PrepMinutes">Time for the session (60, 120, or 240); null for <c>daily</c>.</param>
/// <param name="AnchorIngredient">Typed starting ingredient, already cleaned; untrusted and private.</param>
/// <param name="People">People to feed in this plan, 1 to 12.</param>
public sealed record ShoppingPlanSetup(DateOnly Today, int Days, IReadOnlyList<string> MealTypes, int MealsOut, string CookingStyle, string Priority, DateOnly? PrepDate, int? PrepMinutes, string? AnchorIngredient, int People)
{
    /// <summary>Longest period a shopping plan covers, in days.</summary>
    public const int MaxDays = 14;

    /// <summary>Most meals a shopping plan fills.</summary>
    public const int MaxMeals = 40;

    /// <summary>Most people in one plan.</summary>
    public const int MaxPeople = 12;

    /// <summary>Longest wait before the session, in days.</summary>
    public const int MaxPrepDaysAhead = 7;

    /// <summary>Accepted cooking styles.</summary>
    public static readonly IReadOnlySet<string> CookingStyles = new HashSet<string>(StringComparer.Ordinal) { "once", "mix", "daily" };

    /// <summary>Accepted goals.</summary>
    public static readonly IReadOnlySet<string> Priorities = new HashSet<string>(StringComparer.Ordinal) { "waste", "short", "save", "time" };

    /// <summary>Accepted session lengths, in minutes. "3 h or more" is stored as 240.</summary>
    public static readonly IReadOnlySet<int> PrepLengths = new HashSet<int> { 60, 120, 240 };

    /// <summary>Meals the plan must fill.</summary>
    public int TotalMeals => Days * MealTypes.Count - MealsOut;

    /// <summary>First day of the menu: the session day when there is one, else today.</summary>
    public DateOnly StartDate => PrepDate ?? Today;

    /// <summary>Last day of the menu, inclusive.</summary>
    public DateOnly EndDate => StartDate.AddDays(Days - 1);

    /// <summary>
    /// Meals to fill per group. Lunch and dinner are one interchangeable <c>main</c> group; meals out
    /// come from it first, because people mostly eat those out.
    /// </summary>
    public IReadOnlyDictionary<string, int> MealNeeds()
    {
        var needs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var meal in MealTypes)
        {
            var group = GroupOf(meal);
            needs[group] = needs.GetValueOrDefault(group) + Days;
        }

        var left = MealsOut;
        foreach (var group in new[] { "main", "snack", "breakfast" })
        {
            if (needs.TryGetValue(group, out var count))
            {
                var taken = Math.Min(count, left);
                needs[group] = count - taken;
                left -= taken;
            }
        }

        return needs;
    }

    /// <summary>Group of a meal type.</summary>
    /// <param name="mealType">Meal type.</param>
    public static string GroupOf(string mealType) => mealType is "lunch" or "dinner" ? "main" : mealType;
}

/// <summary>A background wait on a preparation session.</summary>
/// <param name="TaskKey">Task whose passive part is running.</param>
/// <param name="EndsAt">When it ends. It is a target time, so it stays right after a reload.</param>
public sealed record ShoppingPlanRouteTimer(string TaskKey, DateTimeOffset EndsAt);

/// <summary>How far a preparation session has come (PLAN-0062 Slice 6). It holds identifiers and times, no food text.</summary>
/// <param name="Done">Tasks whose hands-on part is finished.</param>
/// <param name="Skipped">Tasks the person chose not to do.</param>
/// <param name="Timers">Background waits still shown.</param>
public sealed record ShoppingPlanRouteProgress(IReadOnlyList<string> Done, IReadOnlyList<string> Skipped, IReadOnlyList<ShoppingPlanRouteTimer> Timers);

/// <summary>Lifecycle of a shopping plan.</summary>
public enum ShoppingPlanStatus
{
    /// <summary>The person is choosing cards.</summary>
    Building,
    /// <summary>The person is reviewing the dishes.</summary>
    Reviewing,
    /// <summary>The menu was accepted.</summary>
    Finished,
    /// <summary>The plan was discarded and its menu archived.</summary>
    Abandoned
}

/// <summary>One shopping plan: a guided way to fill a draft menu (PLAN-0062). Deck state lives here; meals live in the menu.</summary>
public sealed class ShoppingPlanSession
{
    /// <summary>Most AI batches per plan.</summary>
    public const int MaxBatches = 5;

    /// <summary>Most cards per plan.</summary>
    public const int MaxCards = 60;

    /// <summary>Gets the identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the owner.</summary>
    public required Guid OwnerUserId { get; init; }
    /// <summary>Gets the draft menu this plan fills.</summary>
    public required Guid MenuPlanId { get; init; }
    /// <summary>Gets the idempotency key of the creating request.</summary>
    public required Guid CreationKey { get; init; }
    /// <summary>Gets the setup answers.</summary>
    public required ShoppingPlanSetup Setup { get; init; }
    /// <summary>Gets or sets the lifecycle status.</summary>
    public ShoppingPlanStatus Status { get; set; } = ShoppingPlanStatus.Building;
    /// <summary>Gets or sets the AI batches used.</summary>
    public int BatchCount { get; set; }
    /// <summary>Gets or sets the key of the last batch, so a retried request does not spend another.</summary>
    public Guid? LastBatchKey { get; set; }
    /// <summary>Gets or sets the rejections since the last acceptance.</summary>
    public int ConsecutiveRejections { get; set; }
    /// <summary>Gets or sets the card undo would restore; cleared after one undo.</summary>
    public Guid? LastDecidedCardId { get; set; }
    /// <summary>Gets whether a typed anchor shaped this plan, which keeps its recipes out of the shared library.</summary>
    public required bool PrivateDirection { get; init; }
    /// <summary>Gets or sets the preparation session's progress; null until it starts.</summary>
    public ShoppingPlanRouteProgress? RouteProgress { get; set; }
    /// <summary>Gets the UTC creation instant.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Where a card came from.</summary>
public enum ShoppingPlanCardSource
{
    /// <summary>The owner's saved recipe; needs no expansion.</summary>
    Saved,
    /// <summary>The shared idea library; expansion usually needs no AI.</summary>
    Idea,
    /// <summary>The model.</summary>
    Ai
}

/// <summary>What the person decided about a card.</summary>
public enum ShoppingPlanCardState
{
    /// <summary>Not decided.</summary>
    Unseen,
    /// <summary>Accepted into the plan.</summary>
    Accepted,
    /// <summary>Refused or removed.</summary>
    Rejected
}

/// <summary>Whether an accepted card has its saved recipe.</summary>
public enum ShoppingPlanExpansionState
{
    /// <summary>No expansion needed yet.</summary>
    None,
    /// <summary>Waiting for the browser to request it.</summary>
    Pending,
    /// <summary>The recipe exists and the meals are on the menu.</summary>
    Ready,
    /// <summary>The expansion failed or the guard refused the full recipe.</summary>
    Failed
}

/// <summary>One ingredient as a card shows it, for the portions in <see cref="ShoppingPlanCardSnapshot.Servings"/>.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Quantity">Quantity.</param>
/// <param name="Unit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="Optional">Whether the dish works without it.</param>
/// <param name="Assumed">Whether the recipe assumes the person has it.</param>
public sealed record ShoppingPlanCardIngredient(string Name, decimal Quantity, string Unit, bool Optional, bool Assumed);

/// <summary>What makes a card a reusable base: what it makes and the dishes that use it.</summary>
/// <param name="OutputName">Ingredient it makes.</param>
/// <param name="OutputQuantity">Quantity it makes.</param>
/// <param name="OutputUnit"><c>Gram</c>, <c>Milliliter</c>, or <c>Unit</c>.</param>
/// <param name="UsedByCardIds">Cards of the dishes that use it; it is offered once one is accepted.</param>
/// <param name="UsedByNames">Their names, for the card's text.</param>
public sealed record ShoppingPlanCardComponent(string OutputName, decimal OutputQuantity, string OutputUnit, IReadOnlyList<Guid> UsedByCardIds, IReadOnlyList<string> UsedByNames);

/// <summary>What a card displays and how it is ranked. It may hold pantry names, so it is deleted when a plan ends.</summary>
/// <param name="Name">Dish name.</param>
/// <param name="Summary">Short description.</param>
/// <param name="MealType">Target meal.</param>
/// <param name="DishFormat">Format, for variety.</param>
/// <param name="Technique">Main technique, for variety.</param>
/// <param name="PrimaryRefs">Main ingredient references, for deduplication.</param>
/// <param name="Servings">Portions the quantities are for.</param>
/// <param name="ActiveMinutes">Hands-on minutes.</param>
/// <param name="TotalMinutes">Total minutes.</param>
/// <param name="Difficulty"><c>easy</c>, <c>medium</c>, or <c>hard</c>.</param>
/// <param name="Ingredients">Ingredients; an estimate until the recipe is expanded.</param>
/// <param name="GuardCode">Restriction the guard could not verify, if any.</param>
/// <param name="GuardIngredient">Ingredient the guard could not verify, if any.</param>
/// <param name="FailureCode">Stable reason for a failed expansion, if any.</param>
/// <param name="ExpansionAttempt">Expansion attempt, which derives a fresh key after a generation session expires.</param>
/// <param name="Component">Present when the card is a reusable base rather than a dish.</param>
public sealed record ShoppingPlanCardSnapshot(
    string Name,
    string Summary,
    string MealType,
    string DishFormat,
    string Technique,
    IReadOnlyList<string> PrimaryRefs,
    int Servings,
    int ActiveMinutes,
    int TotalMinutes,
    string Difficulty,
    IReadOnlyList<ShoppingPlanCardIngredient> Ingredients,
    string? GuardCode = null,
    string? GuardIngredient = null,
    string? FailureCode = null,
    int ExpansionAttempt = 0,
    ShoppingPlanCardComponent? Component = null);

/// <summary>One dish card of a shopping plan.</summary>
public sealed class ShoppingPlanCard
{
    /// <summary>Gets the identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the plan.</summary>
    public required Guid SessionId { get; init; }
    /// <summary>Gets the owner.</summary>
    public required Guid OwnerUserId { get; init; }
    /// <summary>Gets the source.</summary>
    public required ShoppingPlanCardSource Source { get; init; }
    /// <summary>Gets or sets the recipe: set for saved cards and after expansion.</summary>
    public Guid? RecipeId { get; set; }
    /// <summary>Gets the validated candidate as JSON; null for saved cards.</summary>
    public string? CandidateJson { get; init; }
    /// <summary>Gets or sets the display and ranking data.</summary>
    public required ShoppingPlanCardSnapshot Snapshot { get; set; }
    /// <summary>Gets or sets the decision.</summary>
    public ShoppingPlanCardState State { get; set; } = ShoppingPlanCardState.Unseen;
    /// <summary>Gets or sets the rejection reason: <c>dislike</c>, <c>too_much_work</c>, <c>tired_of</c>, or null.</summary>
    public string? RejectReason { get; set; }
    /// <summary>Gets or sets the ingredient key behind a <c>tired_of</c> rejection.</summary>
    public string? RejectIngredientKey { get; set; }
    /// <summary>Gets or sets how repeats are cooked: <c>yield</c>, <c>again</c>, or null for one meal.</summary>
    public string? AcceptMode { get; set; }
    /// <summary>Gets or sets the meals this card fills.</summary>
    public int Repeats { get; set; } = 1;
    /// <summary>Gets or sets the expansion state.</summary>
    public ShoppingPlanExpansionState ExpansionState { get; set; } = ShoppingPlanExpansionState.None;
    /// <summary>Gets the key that makes this card's session, selection, and entries idempotent.</summary>
    public required Guid ExpansionKey { get; init; }
    /// <summary>Gets the order the card joined the deck; it breaks ranking ties.</summary>
    public required int FirstSeenOrder { get; init; }
    /// <summary>Gets or sets the UTC decision instant.</summary>
    public DateTimeOffset? DecidedAt { get; set; }
}
