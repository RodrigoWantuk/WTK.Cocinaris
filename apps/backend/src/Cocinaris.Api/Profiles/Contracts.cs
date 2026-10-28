namespace Cocinaris.Api.Profiles;

/// <summary>Progressive profile field transport DTO.</summary>
public sealed record ProfileFieldDto<T>(T? Value, string Presence, T? DefaultValue, string Durability);

/// <summary>Household context transport DTO.</summary>
public sealed record HouseholdDto(
    ProfileFieldDto<int?> DefaultAdultCount,
    ProfileFieldDto<int?> DefaultChildCount,
    ProfileFieldDto<int?> DefaultServingCount,
    ProfileFieldDto<string?> Language,
    ProfileFieldDto<string?> Region,
    ProfileFieldDto<string?> Currency,
    ProfileFieldDto<string?> MeasurementSystem,
    ProfileFieldDto<string?> TimeZone,
    ProfileFieldDto<string?> PlanningCadence,
    ProfileFieldDto<string?> ShoppingCadence);

/// <summary>Cooking context transport DTO.</summary>
public sealed record CookingContextDto(
    ProfileFieldDto<string?> OverallSkill,
    ProfileFieldDto<string?> Confidence,
    ProfileFieldDto<string?> PreferredInstructionDetail,
    ProfileFieldDto<int?> OrdinaryPrepMinutes,
    ProfileFieldDto<int?> ExceptionalPrepMinutes,
    ProfileFieldDto<string?> EffortTolerance,
    ProfileFieldDto<string?> CleanupTolerance,
    ProfileFieldDto<string?> RepeatMealPreference,
    ProfileFieldDto<string?> ReheatingPreference,
    ProfileFieldDto<string?> LeftoverPreference,
    ProfileFieldDto<string?> FreezingPreference);

/// <summary>Adult declaration transport DTO.</summary>
public sealed record AdultDeclarationDto(bool? AdultDeclared, string? TermsVersion, string? PrivacyVersion, DateTimeOffset? AcceptedAt, string State);

/// <summary>Full profile response with progressive defaults and an explicit existence flag.</summary>
public sealed record ProfileResponse(
    Guid OwnerUserId,
    ProfileFieldDto<string?> DisplayName,
    HouseholdDto Household,
    CookingContextDto CookingContext,
    AdultDeclarationDto AdultDeclaration,
    IReadOnlyList<string> KnownTechniques,
    IReadOnlyList<string> TechniquesToLearn,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> AbandonmentReasons,
    bool ProfileExists,
    string? Version,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Profile mutation request body.</summary>
public sealed record ProfileMutationRequest(
    FieldMutationDto<string?>? DisplayName,
    FieldMutationDto<int?>? DefaultAdultCount,
    FieldMutationDto<int?>? DefaultChildCount,
    FieldMutationDto<int?>? DefaultServingCount,
    FieldMutationDto<string?>? Language,
    FieldMutationDto<string?>? Region,
    FieldMutationDto<string?>? Currency,
    FieldMutationDto<string?>? MeasurementSystem,
    FieldMutationDto<string?>? TimeZone,
    FieldMutationDto<string?>? PlanningCadence,
    FieldMutationDto<string?>? ShoppingCadence,
    FieldMutationDto<string?>? OverallSkill,
    FieldMutationDto<string?>? Confidence,
    FieldMutationDto<string?>? PreferredInstructionDetail,
    FieldMutationDto<int?>? OrdinaryPrepMinutes,
    FieldMutationDto<int?>? ExceptionalPrepMinutes,
    FieldMutationDto<string?>? EffortTolerance,
    FieldMutationDto<string?>? CleanupTolerance,
    FieldMutationDto<string?>? RepeatMealPreference,
    FieldMutationDto<string?>? ReheatingPreference,
    FieldMutationDto<string?>? LeftoverPreference,
    FieldMutationDto<string?>? FreezingPreference,
    AdultDeclarationMutationDto? AdultDeclaration,
    IReadOnlyList<string>? KnownTechniques,
    IReadOnlyList<string>? TechniquesToLearn,
    IReadOnlyList<string>? Goals,
    IReadOnlyList<string>? AbandonmentReasons);

/// <summary>Field mutation transport DTO.</summary>
public sealed record FieldMutationDto<T>(string? Action, T? Value, string? Durability = "durable");

/// <summary>Adult declaration mutation transport DTO.</summary>
public sealed record AdultDeclarationMutationDto(bool AdultDeclared, string TermsVersion, string? PrivacyVersion);

/// <summary>Preference response DTO.</summary>
public sealed record PreferenceResponse(Guid EntryId, string Category, string StableCode, string? Note, string Presence, int SortOrder);

/// <summary>Versioned preference collection response DTO.</summary>
/// <param name="Version">Opaque profile version matching ETag when a profile exists; null when no profile exists.</param>
/// <param name="Entries">Preference and restriction entries.</param>
public sealed record PreferencesCollectionResponse(string? Version, IReadOnlyList<PreferenceResponse> Entries);

/// <summary>Preference command request DTO.</summary>
public sealed record PreferencesRequest(IReadOnlyList<PreferenceCommandDto> Entries);

/// <summary>Explicit preference command DTO.</summary>
public sealed record PreferenceCommandDto(string Action, string Category, string StableCode, string? Note);

/// <summary>Equipment response DTO.</summary>
public sealed record EquipmentResponse(Guid EntryId, string StableCode, string? CustomName, decimal? Capacity, string? CapacityUnit, string? ConstraintNote, bool IsActive, int SortOrder);

/// <summary>Versioned equipment collection response DTO.</summary>
/// <param name="Version">Opaque profile version matching ETag when a profile exists; null when no profile exists.</param>
/// <param name="Entries">Active equipment entries.</param>
public sealed record EquipmentCollectionResponse(string? Version, IReadOnlyList<EquipmentResponse> Entries);

/// <summary>Equipment replace request DTO.</summary>
public sealed record EquipmentRequest(IReadOnlyList<EquipmentItemDto> Entries);

/// <summary>Equipment item request DTO. Order is the array position; response exposes sortOrder.</summary>
public sealed record EquipmentItemDto(string StableCode, string? CustomName, decimal? Capacity, string? CapacityUnit, string? ConstraintNote);

/// <summary>Profile completeness response DTO.</summary>
public sealed record ProfileCompletenessResponse(int PercentComplete, int CompletedSections, int TotalSections, IReadOnlyDictionary<string, int> SectionCounts, string AdultDeclarationState, bool ProfileExists);
