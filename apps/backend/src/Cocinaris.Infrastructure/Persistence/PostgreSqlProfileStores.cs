using System.Text.Json;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Profiles.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cocinaris.Infrastructure.Persistence;

/// <summary>PostgreSQL read-store implementation for owner-scoped profile data.</summary>
public sealed class PostgreSqlProfileReadStore(ApplicationDbContext database) : IProfileReadStore
{
    /// <inheritdoc />
    public async Task<ProfileReadModel?> FindAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var record = await database.UserProfiles.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var preferences = await database.PreferenceEntries.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId).ToListAsync(cancellationToken);
        var equipment = await database.EquipmentEntries.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId).ToListAsync(cancellationToken);
        var orderedCodes = await database.ProfileOrderedCodeEntries.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId).ToListAsync(cancellationToken);
        return new ProfileReadModel(ToDomain(record), record.Version, preferences.Select(ToDomain).ToList(), equipment.Select(ToDomain).ToList(), orderedCodes.Select(ToDomain).ToList());
    }

    /// <inheritdoc />
    public async Task<ProfileSessionProjection> FindSessionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var record = await database.UserProfiles.AsNoTracking().SingleOrDefaultAsync(item => item.OwnerUserId == ownerUserId, cancellationToken);
        if (record is null)
        {
            return new ProfileSessionProjection(false, null, "en", null, MeasurementSystem.Metric.ToString(), 0, AdultDeclarationState.NotDeclared);
        }

        var preferenceCount = await database.PreferenceEntries.AsNoTracking().CountAsync(item => item.OwnerUserId == ownerUserId && item.Presence == nameof(ProfileFieldPresence.Confirmed), cancellationToken);
        var equipmentCount = await database.EquipmentEntries.AsNoTracking().CountAsync(item => item.OwnerUserId == ownerUserId && !item.IsRemoved, cancellationToken);
        var knownTechniques = await database.ProfileOrderedCodeEntries.AsNoTracking().CountAsync(item => item.OwnerUserId == ownerUserId && item.ListName == ProfileListNames.KnownTechniques, cancellationToken);
        var goals = await database.ProfileOrderedCodeEntries.AsNoTracking().CountAsync(item => item.OwnerUserId == ownerUserId && item.ListName == ProfileListNames.Goals, cancellationToken);
        var profile = ToDomain(record);
        var summary = ProfileCompletenessCalculator.Compute(profile, preferenceCount, equipmentCount, knownTechniques, goals);
        return new ProfileSessionProjection(true, record.DisplayName, record.Language ?? "en", record.TimeZone, record.MeasurementSystem ?? MeasurementSystem.Metric.ToString(), summary.PercentComplete, summary.AdultDeclarationState);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileChangeHistoryEntry>> FindHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var items = await database.ProfileChangeHistoryEntries.AsNoTracking()
            .Where(item => item.OwnerUserId == ownerUserId)
            .OrderByDescending(item => item.OccurredAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return items.Select(item => new ProfileChangeHistoryEntry(item.Id, item.SectionChanged, JsonSerializer.Deserialize<string[]>(item.FieldCodesJson) ?? [], item.OccurredAt)).ToList();
    }

    /// <inheritdoc />
    public async Task<ProfileExportProjection> BuildExportProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var model = await FindAsync(ownerUserId, cancellationToken);
        if (model is null)
        {
            return new ProfileExportProjection(ownerUserId, new Dictionary<string, string?>(StringComparer.Ordinal), [], [], [], [], [], [], AdultDeclarationState.NotDeclared, null);
        }

        var profile = model.Profile;
        var scalars = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["displayName"] = profile.DisplayName?.Value,
            ["language"] = profile.Language?.Value,
            ["region"] = profile.Region?.Value,
            ["currency"] = profile.Currency?.Value,
            ["measurementSystem"] = profile.MeasurementSystem?.ToString(),
            ["timeZone"] = profile.TimeZone?.Value
        };
        return new ProfileExportProjection(
            ownerUserId,
            scalars,
            model.Preferences.Where(item => item.Presence == ProfileFieldPresence.Confirmed).Select(item => item.StableCode.Value).ToList(),
            model.Equipment.Where(item => !item.IsRemoved).Select(item => item.StableCode.Value).ToList(),
            Codes(model, ProfileListNames.KnownTechniques),
            Codes(model, ProfileListNames.TechniquesToLearn),
            Codes(model, ProfileListNames.Goals),
            Codes(model, ProfileListNames.AbandonmentReasons),
            profile.AdultDeclarationState,
            profile.TermsAcceptedAt);
    }

    /// <inheritdoc />
    public async Task<ProfileDeletionProjection> BuildDeletionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var exists = await database.UserProfiles.AsNoTracking().AnyAsync(item => item.OwnerUserId == ownerUserId, cancellationToken);
        return new ProfileDeletionProjection(ownerUserId, exists);
    }

    private static IReadOnlyList<string> Codes(ProfileReadModel model, string listName) =>
        model.OrderedCodes.Where(item => item.ListName == listName).OrderBy(item => item.SortOrder).Select(item => item.StableCode.Value).ToList();

    internal static UserProfile ToDomain(UserProfileRecord record)
    {
        DisplayName? displayName = null;
        if (record.DisplayName is not null)
        {
            DisplayName.TryCreate(record.DisplayName, out displayName);
        }

        LanguageTag? language = null;
        if (record.Language is not null)
        {
            LanguageTag.TryCreate(record.Language, out language);
        }

        RegionCode? region = null;
        if (record.Region is not null)
        {
            RegionCode.TryCreate(record.Region, out region);
        }

        CurrencyCode? currency = null;
        if (record.Currency is not null)
        {
            CurrencyCode.TryCreate(record.Currency, out currency);
        }

        IanaTimeZoneId? timeZone = null;
        if (record.TimeZone is not null)
        {
            IanaTimeZoneId.TryCreate(record.TimeZone, out timeZone);
        }

        return UserProfile.Restore(
            record.OwnerUserId,
            displayName,
            Enum.Parse<ProfileFieldPresence>(record.DisplayNamePresence),
            record.DefaultAdultCount,
            Enum.Parse<ProfileFieldPresence>(record.DefaultAdultCountPresence),
            record.DefaultChildCount,
            Enum.Parse<ProfileFieldPresence>(record.DefaultChildCountPresence),
            record.DefaultServingCount,
            Enum.Parse<ProfileFieldPresence>(record.DefaultServingCountPresence),
            language,
            Enum.Parse<ProfileFieldPresence>(record.LanguagePresence),
            region,
            Enum.Parse<ProfileFieldPresence>(record.RegionPresence),
            currency,
            Enum.Parse<ProfileFieldPresence>(record.CurrencyPresence),
            record.MeasurementSystem is null ? null : Enum.Parse<MeasurementSystem>(record.MeasurementSystem),
            Enum.Parse<ProfileFieldPresence>(record.MeasurementSystemPresence),
            timeZone,
            Enum.Parse<ProfileFieldPresence>(record.TimeZonePresence),
            record.PlanningCadence is null ? null : Enum.Parse<PlanningCadence>(record.PlanningCadence),
            Enum.Parse<ProfileFieldPresence>(record.PlanningCadencePresence),
            record.ShoppingCadence is null ? null : Enum.Parse<ShoppingCadence>(record.ShoppingCadence),
            Enum.Parse<ProfileFieldPresence>(record.ShoppingCadencePresence),
            record.OverallSkill is null ? null : Enum.Parse<CookingSkillLevel>(record.OverallSkill),
            Enum.Parse<ProfileFieldPresence>(record.OverallSkillPresence),
            record.Confidence is null ? null : Enum.Parse<CookingConfidenceLevel>(record.Confidence),
            Enum.Parse<ProfileFieldPresence>(record.ConfidencePresence),
            record.PreferredInstructionDetail is null ? null : Enum.Parse<InstructionDetailLevel>(record.PreferredInstructionDetail),
            Enum.Parse<ProfileFieldPresence>(record.PreferredInstructionDetailPresence),
            record.OrdinaryPrepMinutes,
            Enum.Parse<ProfileFieldPresence>(record.OrdinaryPrepMinutesPresence),
            record.ExceptionalPrepMinutes,
            Enum.Parse<ProfileFieldPresence>(record.ExceptionalPrepMinutesPresence),
            record.EffortTolerance is null ? null : Enum.Parse<PreferenceTolerance>(record.EffortTolerance),
            Enum.Parse<ProfileFieldPresence>(record.EffortTolerancePresence),
            record.CleanupTolerance is null ? null : Enum.Parse<PreferenceTolerance>(record.CleanupTolerance),
            Enum.Parse<ProfileFieldPresence>(record.CleanupTolerancePresence),
            record.RepeatMealPreference is null ? null : Enum.Parse<RepeatMealPreference>(record.RepeatMealPreference),
            Enum.Parse<ProfileFieldPresence>(record.RepeatMealPreferencePresence),
            record.ReheatingPreference is null ? null : Enum.Parse<ReheatingPreference>(record.ReheatingPreference),
            Enum.Parse<ProfileFieldPresence>(record.ReheatingPreferencePresence),
            record.LeftoverPreference is null ? null : Enum.Parse<LeftoverPreference>(record.LeftoverPreference),
            Enum.Parse<ProfileFieldPresence>(record.LeftoverPreferencePresence),
            record.FreezingPreference is null ? null : Enum.Parse<FreezingPreference>(record.FreezingPreference),
            Enum.Parse<ProfileFieldPresence>(record.FreezingPreferencePresence),
            record.AdultDeclared,
            record.TermsVersion,
            record.PrivacyVersion,
            record.TermsAcceptedAt,
            record.ConcurrencyToken,
            record.CreatedAt,
            record.UpdatedAt);
    }

    private static PreferenceEntry ToDomain(PreferenceEntryRecord record)
    {
        StableCode.TryCreate(record.StableCode, out var code);
        PrivateNote.TryCreate(record.Note, out var note);
        return PreferenceEntry.Restore(record.Id, record.OwnerUserId, Enum.Parse<PreferenceCategory>(record.Category), code!, note, Enum.Parse<ProfileFieldPresence>(record.Presence), record.SortOrder, record.CreatedAt, record.UpdatedAt);
    }

    private static EquipmentEntry ToDomain(EquipmentEntryRecord record)
    {
        StableCode.TryCreate(record.StableCode, out var code);
        return EquipmentEntry.Restore(record.Id, record.OwnerUserId, code!, record.CustomName, record.Capacity, record.CapacityUnit, record.ConstraintNote, record.IsRemoved, record.SortOrder, record.CreatedAt, record.UpdatedAt);
    }

    private static OrderedCodeEntry ToDomain(ProfileOrderedCodeEntryRecord record)
    {
        StableCode.TryCreate(record.StableCode, out var code);
        return OrderedCodeEntry.Restore(record.Id, record.OwnerUserId, record.ListName, code!, record.SortOrder, record.CreatedAt);
    }
}

/// <summary>PostgreSQL write-store implementation for owner-scoped profile data.</summary>
public sealed class PostgreSqlProfileWriteStore(ApplicationDbContext database) : IProfileWriteStore
{
    /// <inheritdoc />
    public async Task<ProfileWriteOutcome> CreateAsync(ProfileMutationWrite write, CancellationToken cancellationToken)
    {
        var record = ToRecord(write.Profile);
        record.Version = 1;
        database.UserProfiles.Add(record);
        database.PreferenceEntries.AddRange(write.Preferences.Select(ToRecord));
        database.EquipmentEntries.AddRange(write.Equipment.Select(ToRecord));
        database.ProfileOrderedCodeEntries.AddRange(write.OrderedCodes.Select(ToRecord));
        database.ProfileChangeHistoryEntries.Add(CreateHistory(write));
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return ProfileWriteOutcome.Saved;
        }
        catch (DbUpdateException ex) when (IsDuplicateProfile(ex))
        {
            database.ChangeTracker.Clear();
            return ProfileWriteOutcome.CreateConflict;
        }
        catch
        {
            database.ChangeTracker.Clear();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ProfileWriteOutcome> SaveAsync(ProfileMutationWrite write, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // ExecuteDelete avoids tracking conflicts that occur when RemoveRange marks rows deleted
            // and AddRange re-inserts the same primary keys retained by equipment/preference identity.
            await database.PreferenceEntries.Where(item => item.OwnerUserId == write.OwnerUserId).ExecuteDeleteAsync(cancellationToken);
            await database.EquipmentEntries.Where(item => item.OwnerUserId == write.OwnerUserId).ExecuteDeleteAsync(cancellationToken);
            await database.ProfileOrderedCodeEntries.Where(item => item.OwnerUserId == write.OwnerUserId).ExecuteDeleteAsync(cancellationToken);

            var record = ToRecord(write.Profile);
            record.Version = write.ExpectedVersion + 1;
            database.UserProfiles.Attach(record);
            database.Entry(record).State = EntityState.Modified;
            database.Entry(record).Property(item => item.Version).OriginalValue = write.ExpectedVersion;

            database.PreferenceEntries.AddRange(write.Preferences.Select(ToRecord));
            database.EquipmentEntries.AddRange(write.Equipment.Select(ToRecord));
            database.ProfileOrderedCodeEntries.AddRange(write.OrderedCodes.Select(ToRecord));
            database.ProfileChangeHistoryEntries.Add(CreateHistory(write));

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProfileWriteOutcome.Saved;
        }
        catch (Exception ex) when (IsConcurrentMutationConflictException(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            return ProfileWriteOutcome.ConcurrencyConflict;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            database.ChangeTracker.Clear();
            throw;
        }
    }

    private static bool IsDuplicateProfile(DbUpdateException exception) =>
        IsPostgresState(exception, PostgresErrorCodes.UniqueViolation);

    /// <summary>
    /// Returns true when a write conflict (optimistic concurrency, deadlock, serialization, or unique race)
    /// should surface as a controlled concurrency precondition failure instead of an unexpected 500.
    /// </summary>
    private static bool IsConcurrentMutationConflictException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
            {
                return true;
            }

            if (current is PostgresException postgres
                && postgres.SqlState is PostgresErrorCodes.UniqueViolation
                    or PostgresErrorCodes.DeadlockDetected
                    or PostgresErrorCodes.SerializationFailure
                    or PostgresErrorCodes.ExclusionViolation)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPostgresState(Exception exception, string sqlState)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == sqlState)
            {
                return true;
            }
        }

        return false;
    }

    private static ProfileChangeHistoryEntryRecord CreateHistory(ProfileMutationWrite write) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = write.OwnerUserId,
        SectionChanged = write.SectionChanged,
        FieldCodesJson = JsonSerializer.Serialize(write.ChangedFieldCodes),
        CorrelationId = write.CorrelationId,
        OccurredAt = write.Profile.UpdatedAt
    };

    internal static UserProfileRecord ToRecord(UserProfile profile) => new()
    {
        OwnerUserId = profile.OwnerUserId,
        DisplayName = profile.DisplayName?.Value,
        DisplayNamePresence = profile.DisplayNamePresence.ToString(),
        DefaultAdultCount = profile.DefaultAdultCount,
        DefaultAdultCountPresence = profile.DefaultAdultCountPresence.ToString(),
        DefaultChildCount = profile.DefaultChildCount,
        DefaultChildCountPresence = profile.DefaultChildCountPresence.ToString(),
        DefaultServingCount = profile.DefaultServingCount,
        DefaultServingCountPresence = profile.DefaultServingCountPresence.ToString(),
        Language = profile.Language?.Value,
        LanguagePresence = profile.LanguagePresence.ToString(),
        Region = profile.Region?.Value,
        RegionPresence = profile.RegionPresence.ToString(),
        Currency = profile.Currency?.Value,
        CurrencyPresence = profile.CurrencyPresence.ToString(),
        MeasurementSystem = profile.MeasurementSystem?.ToString(),
        MeasurementSystemPresence = profile.MeasurementSystemPresence.ToString(),
        TimeZone = profile.TimeZone?.Value,
        TimeZonePresence = profile.TimeZonePresence.ToString(),
        PlanningCadence = profile.PlanningCadence?.ToString(),
        PlanningCadencePresence = profile.PlanningCadencePresence.ToString(),
        ShoppingCadence = profile.ShoppingCadence?.ToString(),
        ShoppingCadencePresence = profile.ShoppingCadencePresence.ToString(),
        OverallSkill = profile.OverallSkill?.ToString(),
        OverallSkillPresence = profile.OverallSkillPresence.ToString(),
        Confidence = profile.Confidence?.ToString(),
        ConfidencePresence = profile.ConfidencePresence.ToString(),
        PreferredInstructionDetail = profile.PreferredInstructionDetail?.ToString(),
        PreferredInstructionDetailPresence = profile.PreferredInstructionDetailPresence.ToString(),
        OrdinaryPrepMinutes = profile.OrdinaryPrepMinutes,
        OrdinaryPrepMinutesPresence = profile.OrdinaryPrepMinutesPresence.ToString(),
        ExceptionalPrepMinutes = profile.ExceptionalPrepMinutes,
        ExceptionalPrepMinutesPresence = profile.ExceptionalPrepMinutesPresence.ToString(),
        EffortTolerance = profile.EffortTolerance?.ToString(),
        EffortTolerancePresence = profile.EffortTolerancePresence.ToString(),
        CleanupTolerance = profile.CleanupTolerance?.ToString(),
        CleanupTolerancePresence = profile.CleanupTolerancePresence.ToString(),
        RepeatMealPreference = profile.RepeatMealPreference?.ToString(),
        RepeatMealPreferencePresence = profile.RepeatMealPreferencePresence.ToString(),
        ReheatingPreference = profile.ReheatingPreference?.ToString(),
        ReheatingPreferencePresence = profile.ReheatingPreferencePresence.ToString(),
        LeftoverPreference = profile.LeftoverPreference?.ToString(),
        LeftoverPreferencePresence = profile.LeftoverPreferencePresence.ToString(),
        FreezingPreference = profile.FreezingPreference?.ToString(),
        FreezingPreferencePresence = profile.FreezingPreferencePresence.ToString(),
        AdultDeclared = profile.AdultDeclared,
        TermsVersion = profile.TermsVersion,
        PrivacyVersion = profile.PrivacyVersion,
        TermsAcceptedAt = profile.TermsAcceptedAt,
        ConcurrencyToken = profile.ConcurrencyToken,
        CreatedAt = profile.CreatedAt,
        UpdatedAt = profile.UpdatedAt
    };

    private static PreferenceEntryRecord ToRecord(PreferenceEntry entry) => new()
    {
        Id = entry.Id,
        OwnerUserId = entry.OwnerUserId,
        Category = entry.Category.ToString(),
        StableCode = entry.StableCode.Value,
        Note = entry.Note?.Value,
        Presence = entry.Presence.ToString(),
        SortOrder = entry.SortOrder,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };

    private static EquipmentEntryRecord ToRecord(EquipmentEntry entry) => new()
    {
        Id = entry.Id,
        OwnerUserId = entry.OwnerUserId,
        StableCode = entry.StableCode.Value,
        CustomName = entry.CustomName,
        Capacity = entry.Capacity,
        CapacityUnit = entry.CapacityUnit,
        ConstraintNote = entry.ConstraintNote,
        IsRemoved = entry.IsRemoved,
        SortOrder = entry.SortOrder,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };

    private static ProfileOrderedCodeEntryRecord ToRecord(OrderedCodeEntry entry) => new()
    {
        Id = entry.Id,
        OwnerUserId = entry.OwnerUserId,
        ListName = entry.ListName,
        StableCode = entry.StableCode.Value,
        SortOrder = entry.SortOrder,
        CreatedAt = entry.CreatedAt
    };
}
