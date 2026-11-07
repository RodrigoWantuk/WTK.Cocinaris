namespace Cocinaris.Infrastructure.Persistence;

/// <summary>Persistence representation of one owner-scoped menu plan (PLAN-0029).</summary>
public sealed class MenuPlanRecord
{
    /// <summary>Gets or sets the plan identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the idempotency key of the creating request.</summary>
    public Guid CreationKey { get; set; }
    /// <summary>Gets or sets the owner's name for the plan; may be empty.</summary>
    public required string Name { get; set; }
    /// <summary>Gets or sets the first day of the period.</summary>
    public DateOnly StartDate { get; set; }
    /// <summary>Gets or sets the last day of the period, inclusive.</summary>
    public DateOnly EndDate { get; set; }
    /// <summary>Gets or sets <c>Draft</c>, <c>Accepted</c>, or <c>Archived</c>.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change to the plan or its entries.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets the UTC acceptance instant.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }
}

/// <summary>Persistence representation of one menu entry.</summary>
public sealed class MenuEntryRecord
{
    /// <summary>Gets or sets the entry identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the plan.</summary>
    public Guid MenuPlanId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the idempotency key of the creating request.</summary>
    public Guid CreationKey { get; set; }
    /// <summary>Gets or sets the recipe (same owner).</summary>
    public Guid RecipeId { get; set; }
    /// <summary>Gets or sets the pinned revision; null follows the current revision.</summary>
    public int? PinnedRevisionNumber { get; set; }
    /// <summary>Gets or sets the intended day; null is flexible within the period.</summary>
    public DateOnly? PlannedDate { get; set; }
    /// <summary>Gets or sets the intended meal, if any.</summary>
    public string? MealType { get; set; }
    /// <summary>Gets or sets the portions.</summary>
    public int Servings { get; set; }
    /// <summary>Gets or sets <c>Planned</c> or <c>Skipped</c>.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets whether the meal reserves pantry quantity while its menu is accepted.</summary>
    public bool ReserveIngredients { get; set; } = true;
    /// <summary>Gets or sets the meals this one cooking covers, 1 to 4 (PLAN-0062).</summary>
    public int MealsCovered { get; set; } = 1;
    /// <summary>Gets or sets the kind: <c>meal</c> or <c>component</c> (PLAN-0062).</summary>
    public string Kind { get; set; } = "meal";
    /// <summary>Gets or sets what a component makes.</summary>
    public string? ProducesName { get; set; }
    /// <summary>Gets or sets the quantity a component makes.</summary>
    public decimal? ProducesQuantity { get; set; }
    /// <summary>Gets or sets the unit of what a component makes.</summary>
    public string? ProducesUnit { get; set; }
    /// <summary>Gets or sets the suggested cooking order within the menu; null for a menu built by hand.</summary>
    public int? SuggestedOrder { get; set; }
    /// <summary>Gets or sets the day the dish is cooked ahead (PLAN-0065 Slice 4); null when not set.</summary>
    public DateOnly? CookOnDate { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The owner's protection of one pantry lot: the whole lot, or a minimum quantity to keep.</summary>
public sealed class LotProtectionRecord
{
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the protected lot.</summary>
    public Guid LotId { get; set; }
    /// <summary>Gets or sets whether the whole lot is protected.</summary>
    public bool Whole { get; set; }
    /// <summary>Gets or sets the minimum quantity to keep, in the lot's unit.</summary>
    public decimal? MinimumQuantity { get; set; }
    /// <summary>Gets or sets the UTC instant the protection was set.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A shopping line the owner checked off in a plan. The line itself is always derived.</summary>
public sealed class ShoppingCheckRecord
{
    /// <summary>Gets or sets the plan.</summary>
    public Guid MenuPlanId { get; set; }
    /// <summary>Gets or sets the derived item key (normalized name and unit).</summary>
    public required string ItemKey { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the UTC check instant.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>A package size the owner confirmed for a shopping item (PLAN-0029 Phase 4).</summary>
public sealed class PackageSizeRecord
{
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the shopping item key (catalog ingredient or normalized name, and unit).</summary>
    public required string ItemKey { get; set; }
    /// <summary>Gets or sets the size of one package, in the item key's unit.</summary>
    public decimal Size { get; set; }
    /// <summary>Gets or sets the UTC instant the owner set the size.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The owner said "Deixar como está" to a reminder about one meal (PLAN-0062 Slice 4). It holds the
/// reminder's day, so the reminder comes back when that day changes.
/// </summary>
public sealed class ReminderDismissalRecord
{
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the menu entry the reminder is about.</summary>
    public Guid MenuEntryId { get; set; }
    /// <summary>Gets or sets the reminder kind, such as <c>cook_by</c>.</summary>
    public required string Kind { get; set; }
    /// <summary>Gets or sets the day the reminder named when it was dismissed.</summary>
    public DateOnly ReminderDate { get; set; }
    /// <summary>Gets or sets the UTC instant of the dismissal.</summary>
    public DateTimeOffset DismissedAt { get; set; }
}

/// <summary>Persistence representation of one shopping plan (PLAN-0062).</summary>
public sealed class ShoppingPlanSessionRecord
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the plan's menu.</summary>
    public Guid MenuPlanId { get; set; }
    /// <summary>Gets or sets the idempotency key of the creating request.</summary>
    public Guid CreationKey { get; set; }
    /// <summary>Gets or sets <c>Building</c>, <c>Reviewing</c>, <c>Finished</c>, or <c>Abandoned</c>.</summary>
    public required string Status { get; set; }
    /// <summary>Gets or sets the validated setup as JSON; kept for later defaults.</summary>
    public required string SetupJson { get; set; }
    /// <summary>Gets or sets the AI batches used.</summary>
    public int BatchCount { get; set; }
    /// <summary>Gets or sets the key of the last batch.</summary>
    public Guid? LastBatchKey { get; set; }
    /// <summary>Gets or sets the preparation session's progress as JSON; null until it starts.</summary>
    public string? RouteProgressJson { get; set; }
    /// <summary>Gets or sets what the person asked for under "Quero outras opções", as JSON. Holds a typed hint, so it is private and never logged.</summary>
    public string? DeckPreferenceJson { get; set; }
    /// <summary>Gets or sets the rejections since the last acceptance.</summary>
    public int ConsecutiveRejections { get; set; }
    /// <summary>Gets or sets the card undo would restore.</summary>
    public Guid? LastDecidedCardId { get; set; }
    /// <summary>Gets or sets whether a typed anchor shaped this plan.</summary>
    public bool PrivateDirection { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Persistence representation of one dish card. It may hold pantry names, so rows are deleted when a plan ends.</summary>
public sealed class ShoppingPlanCardRecord
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the plan.</summary>
    public Guid SessionId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets <c>Saved</c>, <c>Idea</c>, or <c>Ai</c>.</summary>
    public required string Source { get; set; }
    /// <summary>Gets or sets the recipe, when it exists.</summary>
    public Guid? RecipeId { get; set; }
    /// <summary>Gets or sets the validated candidate as JSON.</summary>
    public string? CandidateJson { get; set; }
    /// <summary>Gets or sets the display and ranking data as JSON.</summary>
    public required string SnapshotJson { get; set; }
    /// <summary>Gets or sets <c>Unseen</c>, <c>Accepted</c>, or <c>Rejected</c>.</summary>
    public required string State { get; set; }
    /// <summary>Gets or sets the rejection reason.</summary>
    public string? RejectReason { get; set; }
    /// <summary>Gets or sets the ingredient key behind a rejection.</summary>
    public string? RejectIngredientKey { get; set; }
    /// <summary>Gets or sets how repeats are cooked.</summary>
    public string? AcceptMode { get; set; }
    /// <summary>Gets or sets the meals this card fills.</summary>
    public int Repeats { get; set; } = 1;
    /// <summary>Gets or sets <c>None</c>, <c>Pending</c>, <c>Ready</c>, or <c>Failed</c>.</summary>
    public required string ExpansionState { get; set; }
    /// <summary>Gets or sets the key behind this card's idempotency.</summary>
    public Guid ExpansionKey { get; set; }
    /// <summary>Gets or sets the order the card joined the deck.</summary>
    public int FirstSeenOrder { get; set; }
    /// <summary>Gets or sets the UTC decision instant.</summary>
    public DateTimeOffset? DecidedAt { get; set; }
}

/// <summary>A browser subscribed to plan notifications (ADR-0013).</summary>
public sealed class PushSubscriptionRecord
{
    /// <summary>Gets or sets the identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the push service address. Personal data; never logged.</summary>
    public required string Endpoint { get; set; }
    /// <summary>Gets or sets the browser's public key.</summary>
    public required string P256dh { get; set; }
    /// <summary>Gets or sets the browser's secret.</summary>
    public required string Auth { get; set; }
    /// <summary>Gets or sets when it was subscribed.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets when a push service last accepted a message.</summary>
    public DateTimeOffset? LastSuccessAt { get; set; }
}

/// <summary>The last day an owner was notified, so a restart never sends twice (ADR-0013).</summary>
public sealed class PushDeliveryDayRecord
{
    /// <summary>Gets or sets the owner.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the owner's local day.</summary>
    public DateOnly LastSentOn { get; set; }
}

/// <summary>What the owner said about a shopping line while shopping (PLAN-0062). The line itself is always derived.</summary>
public sealed class ShoppingLineMarkRecord
{
    /// <summary>Gets or sets the plan.</summary>
    public Guid MenuPlanId { get; set; }
    /// <summary>Gets or sets the line's original key.</summary>
    public required string ItemKey { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets <c>later</c>, or null.</summary>
    public string? State { get; set; }
    /// <summary>Gets or sets the product bought instead, if any.</summary>
    public string? SubstituteName { get; set; }
    /// <summary>Gets or sets the place code, if the owner moved the line.</summary>
    public string? Place { get; set; }
    /// <summary>Gets or sets the UTC instant of the last change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
