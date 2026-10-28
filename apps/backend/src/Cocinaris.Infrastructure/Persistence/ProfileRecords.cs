namespace Cocinaris.Infrastructure.Persistence;

/// <summary>Persistence representation of one owner profile aggregate root.</summary>
public sealed class UserProfileRecord
{
    /// <summary>Gets or sets the owner user identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the optional display name.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Gets or sets the display-name presence state.</summary>
    public required string DisplayNamePresence { get; set; }
    /// <summary>Gets or sets the default adult count.</summary>
    public int? DefaultAdultCount { get; set; }
    /// <summary>Gets or sets the default adult count presence state.</summary>
    public required string DefaultAdultCountPresence { get; set; }
    /// <summary>Gets or sets the default child count.</summary>
    public int? DefaultChildCount { get; set; }
    /// <summary>Gets or sets the default child count presence state.</summary>
    public required string DefaultChildCountPresence { get; set; }
    /// <summary>Gets or sets the default serving count.</summary>
    public int? DefaultServingCount { get; set; }
    /// <summary>Gets or sets the default serving count presence state.</summary>
    public required string DefaultServingCountPresence { get; set; }
    /// <summary>Gets or sets the language tag.</summary>
    public string? Language { get; set; }
    /// <summary>Gets or sets the language presence state.</summary>
    public required string LanguagePresence { get; set; }
    /// <summary>Gets or sets the region code.</summary>
    public string? Region { get; set; }
    /// <summary>Gets or sets the region presence state.</summary>
    public required string RegionPresence { get; set; }
    /// <summary>Gets or sets the currency code.</summary>
    public string? Currency { get; set; }
    /// <summary>Gets or sets the currency presence state.</summary>
    public required string CurrencyPresence { get; set; }
    /// <summary>Gets or sets the measurement system.</summary>
    public string? MeasurementSystem { get; set; }
    /// <summary>Gets or sets the measurement-system presence state.</summary>
    public required string MeasurementSystemPresence { get; set; }
    /// <summary>Gets or sets the IANA timezone identifier.</summary>
    public string? TimeZone { get; set; }
    /// <summary>Gets or sets the timezone presence state.</summary>
    public required string TimeZonePresence { get; set; }
    /// <summary>Gets or sets the planning cadence.</summary>
    public string? PlanningCadence { get; set; }
    /// <summary>Gets or sets the planning-cadence presence state.</summary>
    public required string PlanningCadencePresence { get; set; }
    /// <summary>Gets or sets the shopping cadence.</summary>
    public string? ShoppingCadence { get; set; }
    /// <summary>Gets or sets the shopping-cadence presence state.</summary>
    public required string ShoppingCadencePresence { get; set; }
    /// <summary>Gets or sets the overall skill level.</summary>
    public string? OverallSkill { get; set; }
    /// <summary>Gets or sets the overall-skill presence state.</summary>
    public required string OverallSkillPresence { get; set; }
    /// <summary>Gets or sets the confidence level.</summary>
    public string? Confidence { get; set; }
    /// <summary>Gets or sets the confidence presence state.</summary>
    public required string ConfidencePresence { get; set; }
    /// <summary>Gets or sets the preferred instruction detail level.</summary>
    public string? PreferredInstructionDetail { get; set; }
    /// <summary>Gets or sets the instruction-detail presence state.</summary>
    public required string PreferredInstructionDetailPresence { get; set; }
    /// <summary>Gets or sets the ordinary preparation time in minutes.</summary>
    public int? OrdinaryPrepMinutes { get; set; }
    /// <summary>Gets or sets the ordinary preparation time presence state.</summary>
    public required string OrdinaryPrepMinutesPresence { get; set; }
    /// <summary>Gets or sets the exceptional preparation time in minutes.</summary>
    public int? ExceptionalPrepMinutes { get; set; }
    /// <summary>Gets or sets the exceptional preparation time presence state.</summary>
    public required string ExceptionalPrepMinutesPresence { get; set; }
    /// <summary>Gets or sets the effort tolerance.</summary>
    public string? EffortTolerance { get; set; }
    /// <summary>Gets or sets the effort-tolerance presence state.</summary>
    public required string EffortTolerancePresence { get; set; }
    /// <summary>Gets or sets the cleanup tolerance.</summary>
    public string? CleanupTolerance { get; set; }
    /// <summary>Gets or sets the cleanup-tolerance presence state.</summary>
    public required string CleanupTolerancePresence { get; set; }
    /// <summary>Gets or sets the repeat-meal preference.</summary>
    public string? RepeatMealPreference { get; set; }
    /// <summary>Gets or sets the repeat-meal preference presence state.</summary>
    public required string RepeatMealPreferencePresence { get; set; }
    /// <summary>Gets or sets the reheating preference.</summary>
    public string? ReheatingPreference { get; set; }
    /// <summary>Gets or sets the reheating preference presence state.</summary>
    public required string ReheatingPreferencePresence { get; set; }
    /// <summary>Gets or sets the leftover preference.</summary>
    public string? LeftoverPreference { get; set; }
    /// <summary>Gets or sets the leftover preference presence state.</summary>
    public required string LeftoverPreferencePresence { get; set; }
    /// <summary>Gets or sets the freezing preference.</summary>
    public string? FreezingPreference { get; set; }
    /// <summary>Gets or sets the freezing preference presence state.</summary>
    public required string FreezingPreferencePresence { get; set; }
    /// <summary>Gets or sets whether the user declared adult capacity.</summary>
    public bool? AdultDeclared { get; set; }
    /// <summary>Gets or sets the accepted terms version.</summary>
    public string? TermsVersion { get; set; }
    /// <summary>Gets or sets the accepted privacy version.</summary>
    public string? PrivacyVersion { get; set; }
    /// <summary>Gets or sets the terms acceptance timestamp.</summary>
    public DateTimeOffset? TermsAcceptedAt { get; set; }
    /// <summary>Gets or sets the opaque concurrency token.</summary>
    public Guid ConcurrencyToken { get; set; }
    /// <summary>Gets or sets the internal optimistic-concurrency version.</summary>
    public long Version { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Persistence representation of one explicit preference or restriction entry.</summary>
public sealed class PreferenceEntryRecord
{
    /// <summary>Gets or sets the entry identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner user identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the category.</summary>
    public required string Category { get; set; }
    /// <summary>Gets or sets the stable code.</summary>
    public required string StableCode { get; set; }
    /// <summary>Gets or sets the optional private note.</summary>
    public string? Note { get; set; }
    /// <summary>Gets or sets the presence state.</summary>
    public required string Presence { get; set; }
    /// <summary>Gets or sets the sort order.</summary>
    public int SortOrder { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Persistence representation of one equipment entry.</summary>
public sealed class EquipmentEntryRecord
{
    /// <summary>Gets or sets the entry identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner user identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the stable equipment code.</summary>
    public required string StableCode { get; set; }
    /// <summary>Gets or sets the optional custom name.</summary>
    public string? CustomName { get; set; }
    /// <summary>Gets or sets the optional capacity.</summary>
    public decimal? Capacity { get; set; }
    /// <summary>Gets or sets the optional capacity unit.</summary>
    public string? CapacityUnit { get; set; }
    /// <summary>Gets or sets the optional constraint note.</summary>
    public string? ConstraintNote { get; set; }
    /// <summary>Gets or sets whether the entry is removed.</summary>
    public bool IsRemoved { get; set; }
    /// <summary>Gets or sets the sort order.</summary>
    public int SortOrder { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Persistence representation of one ordered stable-code list entry.</summary>
public sealed class ProfileOrderedCodeEntryRecord
{
    /// <summary>Gets or sets the entry identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner user identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the list name.</summary>
    public required string ListName { get; set; }
    /// <summary>Gets or sets the stable code.</summary>
    public required string StableCode { get; set; }
    /// <summary>Gets or sets the sort order.</summary>
    public int SortOrder { get; set; }
    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Privacy-minimizing profile change history record.</summary>
public sealed class ProfileChangeHistoryEntryRecord
{
    /// <summary>Gets or sets the entry identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the owner user identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the changed section name.</summary>
    public required string SectionChanged { get; set; }
    /// <summary>Gets or sets the changed field codes as JSON.</summary>
    public required string FieldCodesJson { get; set; }
    /// <summary>Gets or sets the request correlation identifier.</summary>
    public required string CorrelationId { get; set; }
    /// <summary>Gets or sets the occurrence timestamp.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
