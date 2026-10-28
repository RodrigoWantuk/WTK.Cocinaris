namespace Cocinaris.Modules.Profiles.Domain;

/// <summary>Owner-scoped user profile aggregate with household, cooking, and compliance state.</summary>
public sealed class UserProfile
{
    private UserProfile(
        Guid ownerUserId,
        DisplayName? displayName,
        ProfileFieldPresence displayNamePresence,
        int? defaultAdultCount,
        ProfileFieldPresence defaultAdultCountPresence,
        int? defaultChildCount,
        ProfileFieldPresence defaultChildCountPresence,
        int? defaultServingCount,
        ProfileFieldPresence defaultServingCountPresence,
        LanguageTag? language,
        ProfileFieldPresence languagePresence,
        RegionCode? region,
        ProfileFieldPresence regionPresence,
        CurrencyCode? currency,
        ProfileFieldPresence currencyPresence,
        MeasurementSystem? measurementSystem,
        ProfileFieldPresence measurementSystemPresence,
        IanaTimeZoneId? timeZone,
        ProfileFieldPresence timeZonePresence,
        PlanningCadence? planningCadence,
        ProfileFieldPresence planningCadencePresence,
        ShoppingCadence? shoppingCadence,
        ProfileFieldPresence shoppingCadencePresence,
        CookingSkillLevel? overallSkill,
        ProfileFieldPresence overallSkillPresence,
        CookingConfidenceLevel? confidence,
        ProfileFieldPresence confidencePresence,
        InstructionDetailLevel? preferredInstructionDetail,
        ProfileFieldPresence preferredInstructionDetailPresence,
        int? ordinaryPrepMinutes,
        ProfileFieldPresence ordinaryPrepMinutesPresence,
        int? exceptionalPrepMinutes,
        ProfileFieldPresence exceptionalPrepMinutesPresence,
        PreferenceTolerance? effortTolerance,
        ProfileFieldPresence effortTolerancePresence,
        PreferenceTolerance? cleanupTolerance,
        ProfileFieldPresence cleanupTolerancePresence,
        RepeatMealPreference? repeatMealPreference,
        ProfileFieldPresence repeatMealPreferencePresence,
        ReheatingPreference? reheatingPreference,
        ProfileFieldPresence reheatingPreferencePresence,
        LeftoverPreference? leftoverPreference,
        ProfileFieldPresence leftoverPreferencePresence,
        FreezingPreference? freezingPreference,
        ProfileFieldPresence freezingPreferencePresence,
        bool? adultDeclared,
        string? termsVersion,
        string? privacyVersion,
        DateTimeOffset? termsAcceptedAt,
        Guid concurrencyToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        OwnerUserId = ownerUserId;
        DisplayName = displayName;
        DisplayNamePresence = displayNamePresence;
        DefaultAdultCount = defaultAdultCount;
        DefaultAdultCountPresence = defaultAdultCountPresence;
        DefaultChildCount = defaultChildCount;
        DefaultChildCountPresence = defaultChildCountPresence;
        DefaultServingCount = defaultServingCount;
        DefaultServingCountPresence = defaultServingCountPresence;
        Language = language;
        LanguagePresence = languagePresence;
        Region = region;
        RegionPresence = regionPresence;
        Currency = currency;
        CurrencyPresence = currencyPresence;
        MeasurementSystem = measurementSystem;
        MeasurementSystemPresence = measurementSystemPresence;
        TimeZone = timeZone;
        TimeZonePresence = timeZonePresence;
        PlanningCadence = planningCadence;
        PlanningCadencePresence = planningCadencePresence;
        ShoppingCadence = shoppingCadence;
        ShoppingCadencePresence = shoppingCadencePresence;
        OverallSkill = overallSkill;
        OverallSkillPresence = overallSkillPresence;
        Confidence = confidence;
        ConfidencePresence = confidencePresence;
        PreferredInstructionDetail = preferredInstructionDetail;
        PreferredInstructionDetailPresence = preferredInstructionDetailPresence;
        OrdinaryPrepMinutes = ordinaryPrepMinutes;
        OrdinaryPrepMinutesPresence = ordinaryPrepMinutesPresence;
        ExceptionalPrepMinutes = exceptionalPrepMinutes;
        ExceptionalPrepMinutesPresence = exceptionalPrepMinutesPresence;
        EffortTolerance = effortTolerance;
        EffortTolerancePresence = effortTolerancePresence;
        CleanupTolerance = cleanupTolerance;
        CleanupTolerancePresence = cleanupTolerancePresence;
        RepeatMealPreference = repeatMealPreference;
        RepeatMealPreferencePresence = repeatMealPreferencePresence;
        ReheatingPreference = reheatingPreference;
        ReheatingPreferencePresence = reheatingPreferencePresence;
        LeftoverPreference = leftoverPreference;
        LeftoverPreferencePresence = leftoverPreferencePresence;
        FreezingPreference = freezingPreference;
        FreezingPreferencePresence = freezingPreferencePresence;
        AdultDeclared = adultDeclared;
        TermsVersion = termsVersion;
        PrivacyVersion = privacyVersion;
        TermsAcceptedAt = termsAcceptedAt;
        ConcurrencyToken = concurrencyToken;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the owner user identifier, which is also the profile identifier.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Gets the optional display name.</summary>
    public DisplayName? DisplayName { get; internal set; }

    /// <summary>Gets the display-name presence state.</summary>
    public ProfileFieldPresence DisplayNamePresence { get; internal set; }

    /// <summary>Gets the default adult count.</summary>
    public int? DefaultAdultCount { get; internal set; }

    /// <summary>Gets the default adult count presence state.</summary>
    public ProfileFieldPresence DefaultAdultCountPresence { get; internal set; }

    /// <summary>Gets the default child or additional-person count.</summary>
    public int? DefaultChildCount { get; internal set; }

    /// <summary>Gets the default child count presence state.</summary>
    public ProfileFieldPresence DefaultChildCountPresence { get; internal set; }

    /// <summary>Gets the default serving count.</summary>
    public int? DefaultServingCount { get; internal set; }

    /// <summary>Gets the default serving count presence state.</summary>
    public ProfileFieldPresence DefaultServingCountPresence { get; internal set; }

    /// <summary>Gets the household language.</summary>
    public LanguageTag? Language { get; internal set; }

    /// <summary>Gets the language presence state.</summary>
    public ProfileFieldPresence LanguagePresence { get; internal set; }

    /// <summary>Gets the household region.</summary>
    public RegionCode? Region { get; internal set; }

    /// <summary>Gets the region presence state.</summary>
    public ProfileFieldPresence RegionPresence { get; internal set; }

    /// <summary>Gets the household currency.</summary>
    public CurrencyCode? Currency { get; internal set; }

    /// <summary>Gets the currency presence state.</summary>
    public ProfileFieldPresence CurrencyPresence { get; internal set; }

    /// <summary>Gets the measurement system.</summary>
    public MeasurementSystem? MeasurementSystem { get; internal set; }

    /// <summary>Gets the measurement-system presence state.</summary>
    public ProfileFieldPresence MeasurementSystemPresence { get; internal set; }

    /// <summary>Gets the IANA timezone.</summary>
    public IanaTimeZoneId? TimeZone { get; internal set; }

    /// <summary>Gets the timezone presence state.</summary>
    public ProfileFieldPresence TimeZonePresence { get; internal set; }

    /// <summary>Gets the planning cadence.</summary>
    public PlanningCadence? PlanningCadence { get; internal set; }

    /// <summary>Gets the planning-cadence presence state.</summary>
    public ProfileFieldPresence PlanningCadencePresence { get; internal set; }

    /// <summary>Gets the shopping cadence.</summary>
    public ShoppingCadence? ShoppingCadence { get; internal set; }

    /// <summary>Gets the shopping-cadence presence state.</summary>
    public ProfileFieldPresence ShoppingCadencePresence { get; internal set; }

    /// <summary>Gets the overall cooking skill.</summary>
    public CookingSkillLevel? OverallSkill { get; internal set; }

    /// <summary>Gets the overall-skill presence state.</summary>
    public ProfileFieldPresence OverallSkillPresence { get; internal set; }

    /// <summary>Gets the cooking confidence level.</summary>
    public CookingConfidenceLevel? Confidence { get; internal set; }

    /// <summary>Gets the confidence presence state.</summary>
    public ProfileFieldPresence ConfidencePresence { get; internal set; }

    /// <summary>Gets the preferred instruction detail level.</summary>
    public InstructionDetailLevel? PreferredInstructionDetail { get; internal set; }

    /// <summary>Gets the instruction-detail presence state.</summary>
    public ProfileFieldPresence PreferredInstructionDetailPresence { get; internal set; }

    /// <summary>Gets the ordinary preparation time in minutes.</summary>
    public int? OrdinaryPrepMinutes { get; internal set; }

    /// <summary>Gets the ordinary preparation time presence state.</summary>
    public ProfileFieldPresence OrdinaryPrepMinutesPresence { get; internal set; }

    /// <summary>Gets the exceptional preparation time in minutes.</summary>
    public int? ExceptionalPrepMinutes { get; internal set; }

    /// <summary>Gets the exceptional preparation time presence state.</summary>
    public ProfileFieldPresence ExceptionalPrepMinutesPresence { get; internal set; }

    /// <summary>Gets the effort tolerance.</summary>
    public PreferenceTolerance? EffortTolerance { get; internal set; }

    /// <summary>Gets the effort-tolerance presence state.</summary>
    public ProfileFieldPresence EffortTolerancePresence { get; internal set; }

    /// <summary>Gets the cleanup tolerance.</summary>
    public PreferenceTolerance? CleanupTolerance { get; internal set; }

    /// <summary>Gets the cleanup-tolerance presence state.</summary>
    public ProfileFieldPresence CleanupTolerancePresence { get; internal set; }

    /// <summary>Gets the repeat-meal preference.</summary>
    public RepeatMealPreference? RepeatMealPreference { get; internal set; }

    /// <summary>Gets the repeat-meal preference presence state.</summary>
    public ProfileFieldPresence RepeatMealPreferencePresence { get; internal set; }

    /// <summary>Gets the reheating preference.</summary>
    public ReheatingPreference? ReheatingPreference { get; internal set; }

    /// <summary>Gets the reheating preference presence state.</summary>
    public ProfileFieldPresence ReheatingPreferencePresence { get; internal set; }

    /// <summary>Gets the leftover preference.</summary>
    public LeftoverPreference? LeftoverPreference { get; internal set; }

    /// <summary>Gets the leftover preference presence state.</summary>
    public ProfileFieldPresence LeftoverPreferencePresence { get; internal set; }

    /// <summary>Gets the freezing preference.</summary>
    public FreezingPreference? FreezingPreference { get; internal set; }

    /// <summary>Gets the freezing preference presence state.</summary>
    public ProfileFieldPresence FreezingPreferencePresence { get; internal set; }

    /// <summary>Gets whether the user declared adult capacity.</summary>
    public bool? AdultDeclared { get; internal set; }

    /// <summary>Gets the accepted terms version.</summary>
    public string? TermsVersion { get; internal set; }

    /// <summary>Gets the accepted privacy version when applicable.</summary>
    public string? PrivacyVersion { get; internal set; }

    /// <summary>Gets the terms acceptance timestamp.</summary>
    public DateTimeOffset? TermsAcceptedAt { get; internal set; }

    /// <summary>Gets the opaque concurrency token.</summary>
    public Guid ConcurrencyToken { get; internal set; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a new empty profile for one owner.</summary>
    public static UserProfile Create(Guid ownerUserId, DateTimeOffset now, Guid concurrencyToken) =>
        Restore(
            ownerUserId,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, ProfileFieldPresence.Absent,
            null, null, null, null,
            concurrencyToken, now, now);

    /// <summary>Restores a profile from persistence.</summary>
    public static UserProfile Restore(
        Guid ownerUserId,
        DisplayName? displayName,
        ProfileFieldPresence displayNamePresence,
        int? defaultAdultCount,
        ProfileFieldPresence defaultAdultCountPresence,
        int? defaultChildCount,
        ProfileFieldPresence defaultChildCountPresence,
        int? defaultServingCount,
        ProfileFieldPresence defaultServingCountPresence,
        LanguageTag? language,
        ProfileFieldPresence languagePresence,
        RegionCode? region,
        ProfileFieldPresence regionPresence,
        CurrencyCode? currency,
        ProfileFieldPresence currencyPresence,
        MeasurementSystem? measurementSystem,
        ProfileFieldPresence measurementSystemPresence,
        IanaTimeZoneId? timeZone,
        ProfileFieldPresence timeZonePresence,
        PlanningCadence? planningCadence,
        ProfileFieldPresence planningCadencePresence,
        ShoppingCadence? shoppingCadence,
        ProfileFieldPresence shoppingCadencePresence,
        CookingSkillLevel? overallSkill,
        ProfileFieldPresence overallSkillPresence,
        CookingConfidenceLevel? confidence,
        ProfileFieldPresence confidencePresence,
        InstructionDetailLevel? preferredInstructionDetail,
        ProfileFieldPresence preferredInstructionDetailPresence,
        int? ordinaryPrepMinutes,
        ProfileFieldPresence ordinaryPrepMinutesPresence,
        int? exceptionalPrepMinutes,
        ProfileFieldPresence exceptionalPrepMinutesPresence,
        PreferenceTolerance? effortTolerance,
        ProfileFieldPresence effortTolerancePresence,
        PreferenceTolerance? cleanupTolerance,
        ProfileFieldPresence cleanupTolerancePresence,
        RepeatMealPreference? repeatMealPreference,
        ProfileFieldPresence repeatMealPreferencePresence,
        ReheatingPreference? reheatingPreference,
        ProfileFieldPresence reheatingPreferencePresence,
        LeftoverPreference? leftoverPreference,
        ProfileFieldPresence leftoverPreferencePresence,
        FreezingPreference? freezingPreference,
        ProfileFieldPresence freezingPreferencePresence,
        bool? adultDeclared,
        string? termsVersion,
        string? privacyVersion,
        DateTimeOffset? termsAcceptedAt,
        Guid concurrencyToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new(ownerUserId, displayName, displayNamePresence, defaultAdultCount, defaultAdultCountPresence, defaultChildCount, defaultChildCountPresence,
            defaultServingCount, defaultServingCountPresence, language, languagePresence, region, regionPresence, currency, currencyPresence,
            measurementSystem, measurementSystemPresence, timeZone, timeZonePresence, planningCadence, planningCadencePresence, shoppingCadence,
            shoppingCadencePresence, overallSkill, overallSkillPresence, confidence, confidencePresence, preferredInstructionDetail,
            preferredInstructionDetailPresence, ordinaryPrepMinutes, ordinaryPrepMinutesPresence, exceptionalPrepMinutes, exceptionalPrepMinutesPresence,
            effortTolerance, effortTolerancePresence, cleanupTolerance, cleanupTolerancePresence, repeatMealPreference, repeatMealPreferencePresence,
            reheatingPreference, reheatingPreferencePresence, leftoverPreference, leftoverPreferencePresence, freezingPreference, freezingPreferencePresence,
            adultDeclared, termsVersion, privacyVersion, termsAcceptedAt, concurrencyToken, createdAt, updatedAt);

    /// <summary>Applies durable profile mutations and rotates the concurrency token.</summary>
    public IReadOnlyList<string> ApplyDurableUpdate(Action<UserProfile> mutate, DateTimeOffset now)
    {
        var changed = new List<string>();
        var before = SnapshotFieldStates();
        mutate(this);
        var after = SnapshotFieldStates();
        foreach (var (field, state) in after)
        {
            if (!before.TryGetValue(field, out var previous) || previous != state)
            {
                changed.Add(field);
            }
        }

        ConcurrencyToken = Guid.NewGuid();
        UpdatedAt = now;
        return changed;
    }

    /// <summary>Gets the adult declaration state for safe session projection.</summary>
    public AdultDeclarationState AdultDeclarationState => AdultDeclared switch
    {
        true => AdultDeclarationState.Declared,
        false => AdultDeclarationState.Declined,
        _ => AdultDeclarationState.NotDeclared
    };

    private Dictionary<string, string> SnapshotFieldStates() => new(StringComparer.Ordinal)
    {
        ["displayName"] = $"{DisplayNamePresence}:{DisplayName?.Value}",
        ["defaultAdultCount"] = $"{DefaultAdultCountPresence}:{DefaultAdultCount}",
        ["defaultChildCount"] = $"{DefaultChildCountPresence}:{DefaultChildCount}",
        ["defaultServingCount"] = $"{DefaultServingCountPresence}:{DefaultServingCount}",
        ["language"] = $"{LanguagePresence}:{Language?.Value}",
        ["region"] = $"{RegionPresence}:{Region?.Value}",
        ["currency"] = $"{CurrencyPresence}:{Currency?.Value}",
        ["measurementSystem"] = $"{MeasurementSystemPresence}:{MeasurementSystem}",
        ["timeZone"] = $"{TimeZonePresence}:{TimeZone?.Value}",
        ["planningCadence"] = $"{PlanningCadencePresence}:{PlanningCadence}",
        ["shoppingCadence"] = $"{ShoppingCadencePresence}:{ShoppingCadence}",
        ["overallSkill"] = $"{OverallSkillPresence}:{OverallSkill}",
        ["confidence"] = $"{ConfidencePresence}:{Confidence}",
        ["preferredInstructionDetail"] = $"{PreferredInstructionDetailPresence}:{PreferredInstructionDetail}",
        ["ordinaryPrepMinutes"] = $"{OrdinaryPrepMinutesPresence}:{OrdinaryPrepMinutes}",
        ["exceptionalPrepMinutes"] = $"{ExceptionalPrepMinutesPresence}:{ExceptionalPrepMinutes}",
        ["effortTolerance"] = $"{EffortTolerancePresence}:{EffortTolerance}",
        ["cleanupTolerance"] = $"{CleanupTolerancePresence}:{CleanupTolerance}",
        ["repeatMealPreference"] = $"{RepeatMealPreferencePresence}:{RepeatMealPreference}",
        ["reheatingPreference"] = $"{ReheatingPreferencePresence}:{ReheatingPreference}",
        ["leftoverPreference"] = $"{LeftoverPreferencePresence}:{LeftoverPreference}",
        ["freezingPreference"] = $"{FreezingPreferencePresence}:{FreezingPreference}",
        ["adultDeclaration"] = $"{AdultDeclared}:{TermsVersion}:{PrivacyVersion}:{TermsAcceptedAt:O}"
    };
}

/// <summary>Explicit user-declared preference or restriction entry.</summary>
public sealed class PreferenceEntry
{
    private PreferenceEntry(Guid id, Guid ownerUserId, PreferenceCategory category, StableCode stableCode, PrivateNote? note, ProfileFieldPresence presence, int sortOrder, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Category = category;
        StableCode = stableCode;
        Note = note;
        Presence = presence;
        SortOrder = sortOrder;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the entry identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owner user identifier.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Gets the preference or restriction category.</summary>
    public PreferenceCategory Category { get; }

    /// <summary>Gets the stable non-localized code.</summary>
    public StableCode StableCode { get; }

    /// <summary>Gets the optional private note.</summary>
    public PrivateNote? Note { get; private set; }

    /// <summary>Gets the presence state.</summary>
    public ProfileFieldPresence Presence { get; private set; }

    /// <summary>Gets the sort order.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a confirmed preference entry from an explicit user command.</summary>
    public static PreferenceEntry CreateConfirmed(Guid ownerUserId, PreferenceCategory category, StableCode stableCode, PrivateNote? note, int sortOrder, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerUserId, category, stableCode, note, ProfileFieldPresence.Confirmed, sortOrder, now, now);

    /// <summary>Restores a preference entry from persistence.</summary>
    public static PreferenceEntry Restore(Guid id, Guid ownerUserId, PreferenceCategory category, StableCode stableCode, PrivateNote? note, ProfileFieldPresence presence, int sortOrder, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(id, ownerUserId, category, stableCode, note, presence, sortOrder, createdAt, updatedAt);

    /// <summary>Marks the entry as explicitly removed by the user.</summary>
    public void Remove(DateTimeOffset now)
    {
        Presence = ProfileFieldPresence.Removed;
        UpdatedAt = now;
    }

    /// <summary>Updates the optional note from an explicit user command.</summary>
    public void UpdateNote(PrivateNote? note, DateTimeOffset now)
    {
        Note = note;
        Presence = ProfileFieldPresence.Confirmed;
        UpdatedAt = now;
    }
}

/// <summary>Owner equipment entry with optional custom metadata.</summary>
public sealed class EquipmentEntry
{
    private EquipmentEntry(Guid id, Guid ownerUserId, StableCode stableCode, string? customName, decimal? capacity, string? capacityUnit, string? constraintNote, bool isRemoved, int sortOrder, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        StableCode = stableCode;
        CustomName = customName;
        Capacity = capacity;
        CapacityUnit = capacityUnit;
        ConstraintNote = constraintNote;
        IsRemoved = isRemoved;
        SortOrder = sortOrder;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the entry identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owner user identifier.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Gets the stable equipment code.</summary>
    public StableCode StableCode { get; }

    /// <summary>Gets the optional custom name.</summary>
    public string? CustomName { get; private set; }

    /// <summary>Gets the optional capacity.</summary>
    public decimal? Capacity { get; private set; }

    /// <summary>Gets the optional capacity unit.</summary>
    public string? CapacityUnit { get; private set; }

    /// <summary>Gets the optional constraint note.</summary>
    public string? ConstraintNote { get; private set; }

    /// <summary>Gets whether the equipment entry was removed.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>Gets the sort order.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Gets the last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active equipment entry.</summary>
    public static EquipmentEntry Create(Guid ownerUserId, StableCode stableCode, string? customName, decimal? capacity, string? capacityUnit, string? constraintNote, int sortOrder, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerUserId, stableCode, customName, capacity, capacityUnit, constraintNote, false, sortOrder, now, now);

    /// <summary>Restores an equipment entry from persistence.</summary>
    public static EquipmentEntry Restore(Guid id, Guid ownerUserId, StableCode stableCode, string? customName, decimal? capacity, string? capacityUnit, string? constraintNote, bool isRemoved, int sortOrder, DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(id, ownerUserId, stableCode, customName, capacity, capacityUnit, constraintNote, isRemoved, sortOrder, createdAt, updatedAt);

    /// <summary>Soft-removes the equipment entry.</summary>
    public void Remove(DateTimeOffset now)
    {
        IsRemoved = true;
        UpdatedAt = now;
    }

    /// <summary>Updates mutable equipment metadata.</summary>
    public void Update(string? customName, decimal? capacity, string? capacityUnit, string? constraintNote, int sortOrder, DateTimeOffset now)
    {
        CustomName = customName;
        Capacity = capacity;
        CapacityUnit = capacityUnit;
        ConstraintNote = constraintNote;
        SortOrder = sortOrder;
        IsRemoved = false;
        UpdatedAt = now;
    }
}

/// <summary>Ordered stable-code list entry for techniques, goals, or abandonment reasons.</summary>
public sealed class OrderedCodeEntry
{
    private OrderedCodeEntry(Guid id, Guid ownerUserId, string listName, StableCode stableCode, int sortOrder, DateTimeOffset createdAt)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        ListName = listName;
        StableCode = stableCode;
        SortOrder = sortOrder;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the entry identifier.</summary>
    public Guid Id { get; }

    /// <summary>Gets the owner user identifier.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>Gets the owning list name.</summary>
    public string ListName { get; }

    /// <summary>Gets the stable code.</summary>
    public StableCode StableCode { get; }

    /// <summary>Gets the sort order.</summary>
    public int SortOrder { get; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Creates one ordered code entry.</summary>
    public static OrderedCodeEntry Create(Guid ownerUserId, string listName, StableCode stableCode, int sortOrder, DateTimeOffset now) =>
        new(Guid.NewGuid(), ownerUserId, listName, stableCode, sortOrder, now);

    /// <summary>Restores one ordered code entry from persistence.</summary>
    public static OrderedCodeEntry Restore(Guid id, Guid ownerUserId, string listName, StableCode stableCode, int sortOrder, DateTimeOffset createdAt) =>
        new(id, ownerUserId, listName, stableCode, sortOrder, createdAt);
}

/// <summary>Well-known ordered list names owned by the profile module.</summary>
public static class ProfileListNames
{
    /// <summary>List of known cooking techniques.</summary>
    public const string KnownTechniques = "known_techniques";

    /// <summary>List of techniques the user wants to learn.</summary>
    public const string TechniquesToLearn = "techniques_to_learn";

    /// <summary>Ordered cooking goals.</summary>
    public const string Goals = "goals";

    /// <summary>Ordered common cooking-abandonment reasons.</summary>
    public const string AbandonmentReasons = "abandonment_reasons";
}

/// <summary>Computes progressive profile completeness without blocking usage.</summary>
public static class ProfileCompletenessCalculator
{
    /// <summary>Computes a completeness summary for one profile snapshot.</summary>
    public static ProfileCompletenessSummary Compute(UserProfile profile, int activePreferenceCount, int activeEquipmentCount, int knownTechniqueCount, int goalCount)
    {
        var householdFields = new[] { profile.LanguagePresence, profile.RegionPresence, profile.CurrencyPresence, profile.MeasurementSystemPresence, profile.TimeZonePresence, profile.DefaultServingCountPresence };
        var cookingFields = new[] { profile.OverallSkillPresence, profile.ConfidencePresence, profile.OrdinaryPrepMinutesPresence };
        var householdCompleted = householdFields.Count(item => item == ProfileFieldPresence.Confirmed);
        var cookingCompleted = cookingFields.Count(item => item == ProfileFieldPresence.Confirmed);
        var adultCompleted = profile.AdultDeclared == true && !string.IsNullOrWhiteSpace(profile.TermsVersion) ? 1 : 0;
        var totalSections = 5;
        var completedSections = 0;
        if (householdCompleted >= 3)
        {
            completedSections++;
        }

        if (cookingCompleted >= 2)
        {
            completedSections++;
        }

        if (activePreferenceCount > 0)
        {
            completedSections++;
        }

        if (activeEquipmentCount > 0)
        {
            completedSections++;
        }

        if (adultCompleted == 1)
        {
            completedSections++;
        }

        var percent = (int)Math.Round(completedSections * 100.0 / totalSections, MidpointRounding.AwayFromZero);
        return new ProfileCompletenessSummary(percent, completedSections, totalSections, householdCompleted, cookingCompleted, activePreferenceCount, activeEquipmentCount, knownTechniqueCount, goalCount, profile.AdultDeclarationState);
    }
}

/// <summary>Progressive completeness summary for API projection.</summary>
public sealed record ProfileCompletenessSummary(
    int PercentComplete,
    int CompletedSections,
    int TotalSections,
    int HouseholdFieldsConfirmed,
    int CookingFieldsConfirmed,
    int ActivePreferenceCount,
    int ActiveEquipmentCount,
    int KnownTechniqueCount,
    int GoalCount,
    AdultDeclarationState AdultDeclarationState);
