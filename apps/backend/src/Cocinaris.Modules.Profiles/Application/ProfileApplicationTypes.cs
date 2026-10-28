using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.Modules.Profiles.Application;

/// <summary>API-facing progressive field projection.</summary>
public sealed record ProfileFieldView<T>(T? Value, string Presence, T? DefaultValue, string Durability = "durable");

/// <summary>Application representation of the owner profile, including whether a durable row exists.</summary>
public sealed record ProfileView(
    Guid OwnerUserId,
    ProfileFieldView<string?> DisplayName,
    HouseholdView Household,
    CookingContextView CookingContext,
    AdultDeclarationView AdultDeclaration,
    IReadOnlyList<string> KnownTechniques,
    IReadOnlyList<string> TechniquesToLearn,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> AbandonmentReasons,
    Guid ConcurrencyToken,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool ProfileExists);

/// <summary>Household context projection.</summary>
public sealed record HouseholdView(
    ProfileFieldView<int?> DefaultAdultCount,
    ProfileFieldView<int?> DefaultChildCount,
    ProfileFieldView<int?> DefaultServingCount,
    ProfileFieldView<string?> Language,
    ProfileFieldView<string?> Region,
    ProfileFieldView<string?> Currency,
    ProfileFieldView<string?> MeasurementSystem,
    ProfileFieldView<string?> TimeZone,
    ProfileFieldView<string?> PlanningCadence,
    ProfileFieldView<string?> ShoppingCadence);

/// <summary>Cooking context projection.</summary>
public sealed record CookingContextView(
    ProfileFieldView<string?> OverallSkill,
    ProfileFieldView<string?> Confidence,
    ProfileFieldView<string?> PreferredInstructionDetail,
    ProfileFieldView<int?> OrdinaryPrepMinutes,
    ProfileFieldView<int?> ExceptionalPrepMinutes,
    ProfileFieldView<string?> EffortTolerance,
    ProfileFieldView<string?> CleanupTolerance,
    ProfileFieldView<string?> RepeatMealPreference,
    ProfileFieldView<string?> ReheatingPreference,
    ProfileFieldView<string?> LeftoverPreference,
    ProfileFieldView<string?> FreezingPreference);

/// <summary>Adult declaration projection.</summary>
public sealed record AdultDeclarationView(bool? AdultDeclared, string? TermsVersion, string? PrivacyVersion, DateTimeOffset? AcceptedAt, string State);

/// <summary>Preference or restriction projection.</summary>
public sealed record PreferenceView(Guid EntryId, string Category, string StableCode, string? Note, string Presence, int SortOrder);

/// <summary>Equipment projection.</summary>
public sealed record EquipmentView(Guid EntryId, string StableCode, string? CustomName, decimal? Capacity, string? CapacityUnit, string? ConstraintNote, bool IsActive, int SortOrder);

/// <summary>Versioned collection projection for preferences or equipment.</summary>
public sealed record VersionedCollectionView<T>(Guid OwnerUserId, Guid ConcurrencyToken, IReadOnlyList<T> Items);

/// <summary>Completeness projection.</summary>
public sealed record ProfileCompletenessView(int PercentComplete, int CompletedSections, int TotalSections, IReadOnlyDictionary<string, int> SectionCounts, string AdultDeclarationState, bool ProfileExists);

/// <summary>Describes a stable application error mapped by an outer transport.</summary>
public sealed record ProfileApplicationProblem(string ErrorCode, string Detail, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Transport-neutral successful state of a profile application operation.</summary>
public enum ProfileApplicationSuccess
{
    /// <summary>A read or mutation completed with a representation.</summary>
    Succeeded,
    /// <summary>A new profile was created.</summary>
    Created
}

/// <summary>Typed transport-neutral outcome of a profile application command or query.</summary>
public sealed record ProfileApplicationResult<T>(ProfileApplicationSuccess Success, T? Value, ProfileApplicationProblem? Problem)
{
    /// <summary>Creates a successful typed application result.</summary>
    public static ProfileApplicationResult<T> Succeeded(T? value, ProfileApplicationSuccess success = ProfileApplicationSuccess.Succeeded) => new(success, value, null);

    /// <summary>Creates an unsuccessful typed application result.</summary>
    public static ProfileApplicationResult<T> Failure(string errorCode, string detail, IReadOnlyDictionary<string, string[]>? errors = null) => new(ProfileApplicationSuccess.Succeeded, default, new ProfileApplicationProblem(errorCode, detail, errors));
}

/// <summary>Represents the client concurrency precondition after an outer transport decodes it.</summary>
public sealed record ProfileVersionPrecondition(bool IsPresent, bool IsValid, Guid Token)
{
    /// <summary>Creates the missing-header precondition state.</summary>
    public static ProfileVersionPrecondition Missing { get; } = new(false, false, Guid.Empty);

    /// <summary>Creates the invalid-token precondition state.</summary>
    public static ProfileVersionPrecondition Invalid { get; } = new(true, false, Guid.Empty);

    /// <summary>Creates a valid expected-version precondition.</summary>
    public static ProfileVersionPrecondition Valid(Guid token) => new(true, true, token);
}

/// <summary>Transport-neutral durable profile replace input.</summary>
public sealed record PutProfileCommand(ProfileMutationInput Input, ProfileVersionPrecondition Precondition, string CorrelationId);

/// <summary>Transport-neutral partial profile update input.</summary>
public sealed record PatchProfileCommand(ProfileMutationInput Input, ProfileVersionPrecondition Precondition, string CorrelationId);

/// <summary>Transport-neutral preferences replace input.</summary>
public sealed record PutPreferencesCommand(IReadOnlyList<PreferenceMutationInput> Entries, ProfileVersionPrecondition Precondition, string CorrelationId);

/// <summary>Transport-neutral equipment replace input.</summary>
public sealed record PutEquipmentCommand(IReadOnlyList<EquipmentMutationInput> Entries, ProfileVersionPrecondition Precondition, string CorrelationId);

/// <summary>Scalar and collection mutation input shared by PUT and PATCH.</summary>
public sealed record ProfileMutationInput(
    FieldMutation<string?>? DisplayName,
    FieldMutation<int?>? DefaultAdultCount,
    FieldMutation<int?>? DefaultChildCount,
    FieldMutation<int?>? DefaultServingCount,
    FieldMutation<string?>? Language,
    FieldMutation<string?>? Region,
    FieldMutation<string?>? Currency,
    FieldMutation<string?>? MeasurementSystem,
    FieldMutation<string?>? TimeZone,
    FieldMutation<string?>? PlanningCadence,
    FieldMutation<string?>? ShoppingCadence,
    FieldMutation<string?>? OverallSkill,
    FieldMutation<string?>? Confidence,
    FieldMutation<string?>? PreferredInstructionDetail,
    FieldMutation<int?>? OrdinaryPrepMinutes,
    FieldMutation<int?>? ExceptionalPrepMinutes,
    FieldMutation<string?>? EffortTolerance,
    FieldMutation<string?>? CleanupTolerance,
    FieldMutation<string?>? RepeatMealPreference,
    FieldMutation<string?>? ReheatingPreference,
    FieldMutation<string?>? LeftoverPreference,
    FieldMutation<string?>? FreezingPreference,
    AdultDeclarationMutationInput? AdultDeclaration,
    IReadOnlyList<string>? KnownTechniques,
    IReadOnlyList<string>? TechniquesToLearn,
    IReadOnlyList<string>? Goals,
    IReadOnlyList<string>? AbandonmentReasons);

/// <summary>Describes one progressive field mutation.</summary>
public sealed record FieldMutation<T>(string? Action, T? Value, string? Durability = "durable");

/// <summary>Explicit adult declaration mutation input.</summary>
public sealed record AdultDeclarationMutationInput(bool AdultDeclared, string TermsVersion, string? PrivacyVersion);

/// <summary>Explicit preference or restriction mutation input.</summary>
public sealed record PreferenceMutationInput(string Action, string Category, string StableCode, string? Note);

/// <summary>Equipment mutation input. Sort order is derived from array position by the workflow.</summary>
public sealed record EquipmentMutationInput(string StableCode, string? CustomName, decimal? Capacity, string? CapacityUnit, string? ConstraintNote);

/// <summary>Executes the owner-scoped profile-read use case.</summary>
public interface IGetProfileUseCase
{
    /// <summary>Returns the owner profile projection.</summary>
    Task<ProfileApplicationResult<ProfileView>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped profile-replace use case.</summary>
public interface IPutProfileUseCase
{
    /// <summary>Replaces durable profile sections.</summary>
    Task<ProfileApplicationResult<ProfileView>> PutAsync(PutProfileCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped profile-patch use case.</summary>
public interface IPatchProfileUseCase
{
    /// <summary>Partially updates durable profile sections.</summary>
    Task<ProfileApplicationResult<ProfileView>> PatchAsync(PatchProfileCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped preferences-read use case.</summary>
public interface IGetPreferencesUseCase
{
    /// <summary>Returns owner preferences and restrictions.</summary>
    Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped preferences-replace use case.</summary>
public interface IPutPreferencesUseCase
{
    /// <summary>Replaces preferences and restrictions via explicit commands.</summary>
    Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> PutAsync(PutPreferencesCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped equipment-read use case.</summary>
public interface IGetEquipmentUseCase
{
    /// <summary>Returns active owner equipment.</summary>
    Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped equipment-replace use case.</summary>
public interface IPutEquipmentUseCase
{
    /// <summary>Replaces owner equipment.</summary>
    Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> PutAsync(PutEquipmentCommand command, CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped completeness-read use case.</summary>
public interface IGetProfileCompletenessUseCase
{
    /// <summary>Returns progressive completeness without blocking usage.</summary>
    Task<ProfileApplicationResult<ProfileCompletenessView>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Executes the owner-scoped safe session projection use case.</summary>
public interface IGetProfileSessionProjectionUseCase
{
    /// <summary>Returns the safe session projection for one owner.</summary>
    Task<ProfileSessionProjection> GetAsync(Guid ownerUserId, CancellationToken cancellationToken);
}
