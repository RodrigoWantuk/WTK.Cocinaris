using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.Modules.Profiles.Application;

/// <summary>Read model for one owner profile aggregate and related collections.</summary>
public sealed record ProfileReadModel(
    UserProfile Profile,
    long Version,
    IReadOnlyList<PreferenceEntry> Preferences,
    IReadOnlyList<EquipmentEntry> Equipment,
    IReadOnlyList<OrderedCodeEntry> OrderedCodes);

/// <summary>Safe session projection without sensitive profile data.</summary>
public sealed record ProfileSessionProjection(
    bool ProfileExists,
    string? DisplayName,
    string? Language,
    string? TimeZone,
    string? MeasurementSystem,
    int PercentComplete,
    AdultDeclarationState AdultDeclarationState);

/// <summary>Privacy-minimizing profile change history entry.</summary>
public sealed record ProfileChangeHistoryEntry(Guid EntryId, string Section, IReadOnlyList<string> FieldCodes, DateTimeOffset OccurredAt);

/// <summary>Module-owned export projection for future Privacy workflows.</summary>
public sealed record ProfileExportProjection(
    Guid OwnerUserId,
    IReadOnlyDictionary<string, string?> ScalarFields,
    IReadOnlyList<string> PreferenceCodes,
    IReadOnlyList<string> EquipmentCodes,
    IReadOnlyList<string> KnownTechniques,
    IReadOnlyList<string> TechniquesToLearn,
    IReadOnlyList<string> Goals,
    IReadOnlyList<string> AbandonmentReasons,
    AdultDeclarationState AdultDeclarationState,
    DateTimeOffset? TermsAcceptedAt);

/// <summary>Module-owned deletion projection for future Privacy workflows.</summary>
public sealed record ProfileDeletionProjection(Guid OwnerUserId, bool HasProfileData);

/// <summary>Read persistence port for owner-scoped profile data.</summary>
public interface IProfileReadStore
{
    /// <summary>Finds the complete profile read model for one owner.</summary>
    Task<ProfileReadModel?> FindAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Finds the safe session projection for one owner.</summary>
    Task<ProfileSessionProjection> FindSessionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Finds privacy-minimizing change history for one owner.</summary>
    Task<IReadOnlyList<ProfileChangeHistoryEntry>> FindHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Builds the module-owned export projection for one owner.</summary>
    Task<ProfileExportProjection> BuildExportProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>Builds the module-owned deletion projection for one owner.</summary>
    Task<ProfileDeletionProjection> BuildDeletionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken);
}

/// <summary>Atomic profile mutation payload.</summary>
public sealed record ProfileMutationWrite(
    Guid OwnerUserId,
    UserProfile Profile,
    IReadOnlyList<PreferenceEntry> Preferences,
    IReadOnlyList<EquipmentEntry> Equipment,
    IReadOnlyList<OrderedCodeEntry> OrderedCodes,
    string SectionChanged,
    IReadOnlyList<string> ChangedFieldCodes,
    string CorrelationId,
    long ExpectedVersion);

/// <summary>Outcome of a profile persistence mutation.</summary>
public enum ProfileWriteOutcome
{
    /// <summary>The mutation was saved.</summary>
    Saved,
    /// <summary>The expected version did not match.</summary>
    ConcurrencyConflict,
    /// <summary>The profile already exists for the owner.</summary>
    CreateConflict
}

/// <summary>Write persistence port for owner-scoped profile data.</summary>
public interface IProfileWriteStore
{
    /// <summary>Creates an empty profile for one owner.</summary>
    Task<ProfileWriteOutcome> CreateAsync(ProfileMutationWrite write, CancellationToken cancellationToken);

    /// <summary>Saves a profile mutation under optimistic concurrency.</summary>
    Task<ProfileWriteOutcome> SaveAsync(ProfileMutationWrite write, CancellationToken cancellationToken);
}
