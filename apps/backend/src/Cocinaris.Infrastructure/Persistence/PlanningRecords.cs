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
