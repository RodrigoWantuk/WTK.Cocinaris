using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.Modules.Profiles.Application;

/// <summary>Module-owned application workflow for the authenticated profile slice.</summary>
public sealed class ProfileApplicationWorkflow(ICurrentUserAccessor currentUser, IProfileReadStore readStore, IProfileWriteStore writeStore, TimeProvider timeProvider)
{
    /// <summary>Returns the owner profile projection, using an absent scaffold when no profile exists yet.</summary>
    public async Task<ProfileApplicationResult<ProfileView>> GetAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var model = await readStore.FindAsync(user.Id, cancellationToken);
        return ProfileApplicationResult<ProfileView>.Succeeded(model is null ? EmptyView(user.Id, timeProvider.GetUtcNow()) : ToView(model));
    }

    /// <summary>Replaces durable profile sections.</summary>
    public async Task<ProfileApplicationResult<ProfileView>> PutAsync(PutProfileCommand command, CancellationToken cancellationToken) =>
        await MutateAsync(command.Input, command.Precondition, command.CorrelationId, replaceMissingWithAbsent: true, cancellationToken);

    /// <summary>Partially updates durable profile sections.</summary>
    public async Task<ProfileApplicationResult<ProfileView>> PatchAsync(PatchProfileCommand command, CancellationToken cancellationToken) =>
        await MutateAsync(command.Input, command.Precondition, command.CorrelationId, replaceMissingWithAbsent: false, cancellationToken);

    /// <summary>Returns owner preferences and restrictions.</summary>
    public async Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> GetPreferencesAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var model = await readStore.FindAsync(user.Id, cancellationToken);
        if (model is null)
        {
            return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Succeeded(new VersionedCollectionView<PreferenceView>(user.Id, Guid.Empty, []));
        }

        var items = model.Preferences.Where(item => item.Presence == ProfileFieldPresence.Confirmed).Select(ToPreferenceView).ToList();
        return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Succeeded(new VersionedCollectionView<PreferenceView>(user.Id, model.Profile.ConcurrencyToken, items));
    }

    /// <summary>Replaces preferences and restrictions via explicit commands only.</summary>
    public async Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> PutPreferencesAsync(PutPreferencesCommand command, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var existing = await readStore.FindAsync(user.Id, cancellationToken);
        var isCreate = existing is null;
        if (!isCreate)
        {
            if (!ValidatePrecondition(command.Precondition, out var preconditionProblem))
            {
                return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Failure(preconditionProblem!.ErrorCode, preconditionProblem.Detail, preconditionProblem.Errors);
            }

            if (existing!.Profile.ConcurrencyToken != command.Precondition.Token)
            {
                return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Failure("precondition_failed", "The profile version is out of date.");
            }
        }

        var model = existing ?? new ProfileReadModel(UserProfile.Create(user.Id, now, Guid.NewGuid()), 0, [], [], []);

        var errors = ValidatePreferenceCommands(command.Entries);
        if (errors.Count > 0)
        {
            return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Failure("validation_failed", "One or more preference commands are invalid.", errors);
        }

        var preferences = ApplyPreferenceCommands(model.Preferences.ToList(), user.Id, command.Entries, now);
        var changedCodes = ProfileHistoryRedaction.RedactPreferenceFieldCodes(ToValidatedPreferenceCommands(command.Entries));
        model.Profile.ApplyDurableUpdate(_ => { }, now);
        var write = new ProfileMutationWrite(user.Id, model.Profile, preferences, model.Equipment.ToList(), model.OrderedCodes.ToList(), "preferences", changedCodes, command.CorrelationId, isCreate ? 0 : model.Version);
        var outcome = isCreate ? await writeStore.CreateAsync(write, cancellationToken) : await writeStore.SaveAsync(write, cancellationToken);
        if (outcome == ProfileWriteOutcome.CreateConflict)
        {
            return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Failure("profile_already_exists", "A profile already exists for this account.");
        }

        if (outcome == ProfileWriteOutcome.ConcurrencyConflict)
        {
            return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Failure("precondition_failed", "The profile version is out of date.");
        }

        var refreshed = await readStore.FindAsync(user.Id, cancellationToken);
        var items = refreshed!.Preferences.Where(item => item.Presence == ProfileFieldPresence.Confirmed).Select(ToPreferenceView).ToList();
        return ProfileApplicationResult<VersionedCollectionView<PreferenceView>>.Succeeded(new VersionedCollectionView<PreferenceView>(user.Id, refreshed.Profile.ConcurrencyToken, items));
    }

    /// <summary>Returns active owner equipment.</summary>
    public async Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> GetEquipmentAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var model = await readStore.FindAsync(user.Id, cancellationToken);
        if (model is null)
        {
            return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Succeeded(new VersionedCollectionView<EquipmentView>(user.Id, Guid.Empty, []));
        }

        var items = model.Equipment.Where(item => !item.IsRemoved).OrderBy(item => item.SortOrder).ThenBy(item => item.StableCode.Value, StringComparer.Ordinal).Select(ToEquipmentView).ToList();
        return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Succeeded(new VersionedCollectionView<EquipmentView>(user.Id, model.Profile.ConcurrencyToken, items));
    }

    /// <summary>Replaces owner equipment.</summary>
    public async Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> PutEquipmentAsync(PutEquipmentCommand command, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var existing = await readStore.FindAsync(user.Id, cancellationToken);
        var isCreate = existing is null;
        if (!isCreate)
        {
            if (!ValidatePrecondition(command.Precondition, out var preconditionProblem))
            {
                return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Failure(preconditionProblem!.ErrorCode, preconditionProblem.Detail, preconditionProblem.Errors);
            }

            if (existing!.Profile.ConcurrencyToken != command.Precondition.Token)
            {
                return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Failure("precondition_failed", "The profile version is out of date.");
            }
        }

        var model = existing ?? new ProfileReadModel(UserProfile.Create(user.Id, now, Guid.NewGuid()), 0, [], [], []);

        var errors = ValidateEquipmentCommands(command.Entries);
        if (errors.Count > 0)
        {
            return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Failure("validation_failed", "One or more equipment entries are invalid.", errors);
        }

        var equipment = ReconcileEquipment(model.Equipment.ToList(), user.Id, command.Entries, now);
        var canonicalCodes = command.Entries
            .Select(item =>
            {
                StableCode.TryCreate(item.StableCode, out var code);
                return code!.Value;
            });
        var changedCodes = ProfileHistoryRedaction.CanonicalEquipmentFieldCodes(canonicalCodes);
        model.Profile.ApplyDurableUpdate(_ => { }, now);
        var write = new ProfileMutationWrite(user.Id, model.Profile, model.Preferences.ToList(), equipment, model.OrderedCodes.ToList(), "equipment", changedCodes, command.CorrelationId, isCreate ? 0 : model.Version);
        var outcome = isCreate ? await writeStore.CreateAsync(write, cancellationToken) : await writeStore.SaveAsync(write, cancellationToken);
        if (outcome == ProfileWriteOutcome.CreateConflict)
        {
            return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Failure("profile_already_exists", "A profile already exists for this account.");
        }

        if (outcome == ProfileWriteOutcome.ConcurrencyConflict)
        {
            return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Failure("precondition_failed", "The profile version is out of date.");
        }

        var refreshed = await readStore.FindAsync(user.Id, cancellationToken);
        var items = refreshed!.Equipment.Where(item => !item.IsRemoved).OrderBy(item => item.SortOrder).ThenBy(item => item.StableCode.Value, StringComparer.Ordinal).Select(ToEquipmentView).ToList();
        return ProfileApplicationResult<VersionedCollectionView<EquipmentView>>.Succeeded(new VersionedCollectionView<EquipmentView>(user.Id, refreshed.Profile.ConcurrencyToken, items));
    }

    /// <summary>Returns progressive completeness without blocking usage.</summary>
    public async Task<ProfileApplicationResult<ProfileCompletenessView>> GetCompletenessAsync(CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var model = await readStore.FindAsync(user.Id, cancellationToken);
        if (model is null)
        {
            return ProfileApplicationResult<ProfileCompletenessView>.Succeeded(new ProfileCompletenessView(0, 0, 5, new Dictionary<string, int>(StringComparer.Ordinal), AdultDeclarationState.NotDeclared.ToString(), false));
        }

        var activePreferences = model.Preferences.Count(item => item.Presence == ProfileFieldPresence.Confirmed);
        var activeEquipment = model.Equipment.Count(item => !item.IsRemoved);
        var knownTechniques = model.OrderedCodes.Count(item => item.ListName == ProfileListNames.KnownTechniques);
        var goals = model.OrderedCodes.Count(item => item.ListName == ProfileListNames.Goals);
        var summary = ProfileCompletenessCalculator.Compute(model.Profile, activePreferences, activeEquipment, knownTechniques, goals);
        var sectionCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["household"] = summary.HouseholdFieldsConfirmed,
            ["cooking"] = summary.CookingFieldsConfirmed,
            ["preferences"] = summary.ActivePreferenceCount,
            ["equipment"] = summary.ActiveEquipmentCount,
            ["adultDeclaration"] = summary.AdultDeclarationState == AdultDeclarationState.Declared ? 1 : 0
        };
        return ProfileApplicationResult<ProfileCompletenessView>.Succeeded(new ProfileCompletenessView(summary.PercentComplete, summary.CompletedSections, summary.TotalSections, sectionCounts, summary.AdultDeclarationState.ToString(), true));
    }

    /// <summary>Returns the safe session projection for one owner.</summary>
    public Task<ProfileSessionProjection> GetSessionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        readStore.FindSessionProjectionAsync(ownerUserId, cancellationToken);

    private async Task<ProfileApplicationResult<ProfileView>> MutateAsync(ProfileMutationInput input, ProfileVersionPrecondition precondition, string correlationId, bool replaceMissingWithAbsent, CancellationToken cancellationToken)
    {
        var user = await currentUser.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var existing = await readStore.FindAsync(user.Id, cancellationToken);
        var isCreate = existing is null;
        if (!isCreate)
        {
            if (!ValidatePrecondition(precondition, out var preconditionProblem))
            {
                return ProfileApplicationResult<ProfileView>.Failure(preconditionProblem!.ErrorCode, preconditionProblem.Detail, preconditionProblem.Errors);
            }

            if (existing!.Profile.ConcurrencyToken != precondition.Token)
            {
                return ProfileApplicationResult<ProfileView>.Failure("precondition_failed", "The profile version is out of date.");
            }
        }

        var model = existing ?? new ProfileReadModel(UserProfile.Create(user.Id, now, Guid.NewGuid()), 0, [], [], []);

        var errors = ValidateMutation(input, replaceMissingWithAbsent);
        if (errors.Count > 0)
        {
            return ProfileApplicationResult<ProfileView>.Failure("validation_failed", "One or more profile fields are invalid.", errors);
        }

        var orderedCodes = model.OrderedCodes.ToList();
        var changedFields = model.Profile.ApplyDurableUpdate(profile =>
        {
            ApplyDisplayName(profile, input.DisplayName, replaceMissingWithAbsent);
            ApplyCount(profile, input.DefaultAdultCount, replaceMissingWithAbsent, 1, 20, (p, v, presence) => { p.DefaultAdultCount = v; p.DefaultAdultCountPresence = presence; });
            ApplyCount(profile, input.DefaultChildCount, replaceMissingWithAbsent, 0, 20, (p, v, presence) => { p.DefaultChildCount = v; p.DefaultChildCountPresence = presence; });
            ApplyCount(profile, input.DefaultServingCount, replaceMissingWithAbsent, 1, 30, (p, v, presence) => { p.DefaultServingCount = v; p.DefaultServingCountPresence = presence; });
            ApplyLanguage(profile, input.Language, replaceMissingWithAbsent);
            ApplyRegion(profile, input.Region, replaceMissingWithAbsent);
            ApplyCurrency(profile, input.Currency, replaceMissingWithAbsent);
            ApplyMeasurementSystem(profile, input.MeasurementSystem, replaceMissingWithAbsent);
            ApplyTimeZone(profile, input.TimeZone, replaceMissingWithAbsent);
            ApplyPlanningCadence(profile, input.PlanningCadence, replaceMissingWithAbsent);
            ApplyShoppingCadence(profile, input.ShoppingCadence, replaceMissingWithAbsent);
            ApplyOverallSkill(profile, input.OverallSkill, replaceMissingWithAbsent);
            ApplyConfidence(profile, input.Confidence, replaceMissingWithAbsent);
            ApplyInstructionDetail(profile, input.PreferredInstructionDetail, replaceMissingWithAbsent);
            ApplyPrepMinutes(profile, input.OrdinaryPrepMinutes, replaceMissingWithAbsent, (p, v, presence) => { p.OrdinaryPrepMinutes = v; p.OrdinaryPrepMinutesPresence = presence; });
            ApplyPrepMinutes(profile, input.ExceptionalPrepMinutes, replaceMissingWithAbsent, (p, v, presence) => { p.ExceptionalPrepMinutes = v; p.ExceptionalPrepMinutesPresence = presence; });
            ApplyTolerance(profile, input.EffortTolerance, replaceMissingWithAbsent, (p, v, presence) => { p.EffortTolerance = v; p.EffortTolerancePresence = presence; });
            ApplyTolerance(profile, input.CleanupTolerance, replaceMissingWithAbsent, (p, v, presence) => { p.CleanupTolerance = v; p.CleanupTolerancePresence = presence; });
            ApplyRepeatMeal(profile, input.RepeatMealPreference, replaceMissingWithAbsent);
            ApplyReheating(profile, input.ReheatingPreference, replaceMissingWithAbsent);
            ApplyLeftover(profile, input.LeftoverPreference, replaceMissingWithAbsent);
            ApplyFreezing(profile, input.FreezingPreference, replaceMissingWithAbsent);
            ApplyAdultDeclaration(profile, input.AdultDeclaration, now);
        }, now).ToList();

        if (input.KnownTechniques is not null)
        {
            orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.KnownTechniques);
            orderedCodes.AddRange(CreateOrderedCodes(user.Id, ProfileListNames.KnownTechniques, input.KnownTechniques, now));
            changedFields.Add("knownTechniques");
        }
        else if (replaceMissingWithAbsent)
        {
            if (orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.KnownTechniques) > 0)
            {
                changedFields.Add("knownTechniques");
            }
        }

        if (input.TechniquesToLearn is not null)
        {
            orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.TechniquesToLearn);
            orderedCodes.AddRange(CreateOrderedCodes(user.Id, ProfileListNames.TechniquesToLearn, input.TechniquesToLearn, now));
            changedFields.Add("techniquesToLearn");
        }
        else if (replaceMissingWithAbsent)
        {
            if (orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.TechniquesToLearn) > 0)
            {
                changedFields.Add("techniquesToLearn");
            }
        }

        if (input.Goals is not null)
        {
            orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.Goals);
            orderedCodes.AddRange(CreateOrderedCodes(user.Id, ProfileListNames.Goals, input.Goals, now));
            changedFields.Add("goals");
        }
        else if (replaceMissingWithAbsent)
        {
            if (orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.Goals) > 0)
            {
                changedFields.Add("goals");
            }
        }

        if (input.AbandonmentReasons is not null)
        {
            orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.AbandonmentReasons);
            orderedCodes.AddRange(CreateOrderedCodes(user.Id, ProfileListNames.AbandonmentReasons, input.AbandonmentReasons, now));
            changedFields.Add("abandonmentReasons");
        }
        else if (replaceMissingWithAbsent)
        {
            if (orderedCodes.RemoveAll(item => item.ListName == ProfileListNames.AbandonmentReasons) > 0)
            {
                changedFields.Add("abandonmentReasons");
            }
        }

        var write = new ProfileMutationWrite(user.Id, model.Profile, model.Preferences.ToList(), model.Equipment.ToList(), orderedCodes, "profile", changedFields.Distinct(StringComparer.Ordinal).ToList(), correlationId, isCreate ? 0 : model.Version);
        var outcome = isCreate ? await writeStore.CreateAsync(write, cancellationToken) : await writeStore.SaveAsync(write, cancellationToken);
        if (outcome == ProfileWriteOutcome.CreateConflict)
        {
            return ProfileApplicationResult<ProfileView>.Failure("profile_already_exists", "A profile already exists for this account.");
        }

        if (outcome == ProfileWriteOutcome.ConcurrencyConflict)
        {
            return ProfileApplicationResult<ProfileView>.Failure("precondition_failed", "The profile version is out of date.");
        }

        var refreshed = await readStore.FindAsync(user.Id, cancellationToken);
        return ProfileApplicationResult<ProfileView>.Succeeded(ToView(refreshed!), isCreate ? ProfileApplicationSuccess.Created : ProfileApplicationSuccess.Succeeded);
    }

    private async Task<ProfileReadModel> EnsureProfileAsync(Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await readStore.FindAsync(ownerUserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = UserProfile.Create(ownerUserId, now, Guid.NewGuid());
        var write = new ProfileMutationWrite(ownerUserId, profile, [], [], [], "profile", ["created"], "bootstrap", 0);
        await writeStore.CreateAsync(write, cancellationToken);
        return (await readStore.FindAsync(ownerUserId, cancellationToken))!;
    }

    private static bool ValidatePrecondition(ProfileVersionPrecondition precondition, out ProfileApplicationProblem? problem)
    {
        if (!precondition.IsPresent)
        {
            problem = new ProfileApplicationProblem("precondition_required", "An If-Match header is required.");
            return false;
        }

        if (!precondition.IsValid)
        {
            problem = new ProfileApplicationProblem("precondition_failed", "The profile version token is invalid.");
            return false;
        }

        problem = null;
        return true;
    }

    private static Dictionary<string, string[]> ValidateMutation(ProfileMutationInput input, bool replaceMissingWithAbsent)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message)
        {
            errors[field] = [message];
        }

        ValidateField(input.DisplayName, "displayName", replaceMissingWithAbsent, value => DisplayName.TryCreate(value, out _), Add);
        ValidateCountField(input.DefaultAdultCount, "defaultAdultCount", replaceMissingWithAbsent, 1, 20, Add);
        ValidateCountField(input.DefaultChildCount, "defaultChildCount", replaceMissingWithAbsent, 0, 20, Add);
        ValidateCountField(input.DefaultServingCount, "defaultServingCount", replaceMissingWithAbsent, 1, 30, Add);
        ValidateField(input.Language, "language", replaceMissingWithAbsent, value => LanguageTag.TryCreate(value, out _), Add);
        ValidateField(input.Region, "region", replaceMissingWithAbsent, value => RegionCode.TryCreate(value, out _), Add);
        ValidateField(input.Currency, "currency", replaceMissingWithAbsent, value => CurrencyCode.TryCreate(value, out _), Add);
        ValidateField(input.MeasurementSystem, "measurementSystem", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseMeasurementSystem(value, out _), Add);
        ValidateField(input.TimeZone, "timeZone", replaceMissingWithAbsent, value => IanaTimeZoneId.TryCreate(value, out _), Add);
        ValidateField(input.PlanningCadence, "planningCadence", replaceMissingWithAbsent, value => ProfileEnumParser.TryParsePlanningCadence(value, out _), Add);
        ValidateField(input.ShoppingCadence, "shoppingCadence", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseShoppingCadence(value, out _), Add);
        ValidateField(input.OverallSkill, "overallSkill", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseCookingSkillLevel(value, out _), Add);
        ValidateField(input.Confidence, "confidence", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseCookingConfidenceLevel(value, out _), Add);
        ValidateField(input.PreferredInstructionDetail, "preferredInstructionDetail", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseInstructionDetailLevel(value, out _), Add);
        ValidateCountField(input.OrdinaryPrepMinutes, "ordinaryPrepMinutes", replaceMissingWithAbsent, 5, 600, Add);
        ValidateCountField(input.ExceptionalPrepMinutes, "exceptionalPrepMinutes", replaceMissingWithAbsent, 5, 1440, Add);
        ValidateField(input.EffortTolerance, "effortTolerance", replaceMissingWithAbsent, value => ProfileEnumParser.TryParsePreferenceTolerance(value, out _), Add);
        ValidateField(input.CleanupTolerance, "cleanupTolerance", replaceMissingWithAbsent, value => ProfileEnumParser.TryParsePreferenceTolerance(value, out _), Add);
        ValidateField(input.RepeatMealPreference, "repeatMealPreference", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseRepeatMealPreference(value, out _), Add);
        ValidateField(input.ReheatingPreference, "reheatingPreference", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseReheatingPreference(value, out _), Add);
        ValidateField(input.LeftoverPreference, "leftoverPreference", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseLeftoverPreference(value, out _), Add);
        ValidateField(input.FreezingPreference, "freezingPreference", replaceMissingWithAbsent, value => ProfileEnumParser.TryParseFreezingPreference(value, out _), Add);
        ValidateCodeList(input.KnownTechniques, "knownTechniques", Add);
        ValidateCodeList(input.TechniquesToLearn, "techniquesToLearn", Add);
        ValidateCodeList(input.Goals, "goals", Add);
        ValidateCodeList(input.AbandonmentReasons, "abandonmentReasons", Add);

        if (input.AdultDeclaration is not null)
        {
            if (string.IsNullOrWhiteSpace(input.AdultDeclaration.TermsVersion) || input.AdultDeclaration.TermsVersion.Length > 32)
            {
                Add("adultDeclaration.termsVersion", "termsVersion is required and must be at most 32 characters.");
            }

            if (input.AdultDeclaration.PrivacyVersion is not null && input.AdultDeclaration.PrivacyVersion.Length > 32)
            {
                Add("adultDeclaration.privacyVersion", "privacyVersion must be at most 32 characters.");
            }
        }

        return errors;
    }

    private static void ValidateField<T>(FieldMutation<T>? mutation, string field, bool replaceMissing, Func<T?, bool> validator, Action<string, string> addError)
    {
        if (mutation is null)
        {
            return;
        }

        if (!ProfileFieldMutationRules.IsValidAction(mutation.Action, replaceMissing, out var actionError))
        {
            addError(field, actionError);
            return;
        }

        if (!ProfileFieldMutationRules.IsValidDurability(mutation.Durability, out var durabilityError))
        {
            addError(field, durabilityError);
            return;
        }

        if (mutation.Action is "remove" or "absent")
        {
            return;
        }

        if (!validator(mutation.Value))
        {
            addError(field, $"{field} is invalid.");
        }
    }

    private static void ValidateCountField(FieldMutation<int?>? mutation, string field, bool replaceMissing, int min, int max, Action<string, string> addError)
    {
        if (mutation is null)
        {
            return;
        }

        if (!ProfileFieldMutationRules.IsValidAction(mutation.Action, replaceMissing, out var actionError))
        {
            addError(field, actionError);
            return;
        }

        if (!ProfileFieldMutationRules.IsValidDurability(mutation.Durability, out var durabilityError))
        {
            addError(field, durabilityError);
            return;
        }

        if (mutation.Action is "remove" or "absent")
        {
            return;
        }

        if (mutation.Value is null || mutation.Value < min || mutation.Value > max)
        {
            addError(field, $"{field} must be between {min} and {max}.");
        }
    }

    private static void ValidateCodeList(IReadOnlyList<string>? values, string field, Action<string, string> addError)
    {
        if (values is null)
        {
            return;
        }

        if (values.Count > 50)
        {
            addError(field, $"{field} cannot contain more than 50 entries.");
            return;
        }

        foreach (var value in values)
        {
            if (!StableCode.TryCreate(value, out _))
            {
                addError(field, $"{field} contains an invalid stable code.");
                return;
            }
        }
    }

    private static Dictionary<string, string[]> ValidatePreferenceCommands(IReadOnlyList<PreferenceMutationInput> entries)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (entries.Count > 100)
        {
            errors["entries"] = ["A maximum of 100 preference commands is allowed."];
            return errors;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Action is not ("add" or "remove" or "update"))
            {
                errors[$"entries[{index}].action"] = ["action must be add, remove, or update."];
            }

            if (!ProfileEnumParser.TryParsePreferenceCategory(entry.Category, out _))
            {
                errors[$"entries[{index}].category"] = ["category is invalid."];
            }

            if (!StableCode.TryCreate(entry.StableCode, out _))
            {
                errors[$"entries[{index}].stableCode"] = ["stableCode is invalid."];
            }

            if (!PrivateNote.TryCreate(entry.Note, out _))
            {
                errors[$"entries[{index}].note"] = ["note is invalid."];
            }
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateEquipmentCommands(IReadOnlyList<EquipmentMutationInput> entries)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (entries.Count > 100)
        {
            errors["entries"] = ["A maximum of 100 equipment entries is allowed."];
            return errors;
        }

        var seenCodes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!StableCode.TryCreate(entry.StableCode, out var code))
            {
                errors[$"entries[{index}].stableCode"] = ["stableCode is invalid."];
            }
            else if (seenCodes.TryGetValue(code!.Value, out var firstIndex))
            {
                errors[$"entries[{index}].stableCode"] = [$"stableCode duplicates entries[{firstIndex}].stableCode."];
                errors["entries"] = ["entries contains duplicate stableCode values after normalization."];
            }
            else
            {
                seenCodes[code.Value] = index;
            }

            if (entry.CustomName is not null && entry.CustomName.Length > 80)
            {
                errors[$"entries[{index}].customName"] = ["customName must be at most 80 characters."];
            }

            if (entry.Capacity is < 0)
            {
                errors[$"entries[{index}].capacity"] = ["capacity must be non-negative."];
            }

            if (entry.CapacityUnit is not null && entry.CapacityUnit.Length > 20)
            {
                errors[$"entries[{index}].capacityUnit"] = ["capacityUnit must be at most 20 characters."];
            }

            if (entry.ConstraintNote is not null && entry.ConstraintNote.Length > 200)
            {
                errors[$"entries[{index}].constraintNote"] = ["constraintNote must be at most 200 characters."];
            }
        }

        return errors;
    }

    private static List<PreferenceEntry> ApplyPreferenceCommands(IList<PreferenceEntry> existing, Guid ownerUserId, IReadOnlyList<PreferenceMutationInput> commands, DateTimeOffset now)
    {
        var working = existing.ToList();
        foreach (var command in commands)
        {
            ProfileEnumParser.TryParsePreferenceCategory(command.Category, out var category);
            StableCode.TryCreate(command.StableCode, out var code);
            PrivateNote.TryCreate(command.Note, out var note);
            var match = working.SingleOrDefault(item => item.Category == category && item.StableCode.Value == code!.Value);
            switch (command.Action)
            {
                case "add":
                    if (match is null)
                    {
                        working.Add(PreferenceEntry.CreateConfirmed(ownerUserId, category, code!, note, working.Count, now));
                    }
                    else if (match.Presence == ProfileFieldPresence.Removed)
                    {
                        match.UpdateNote(note, now);
                    }

                    break;
                case "update":
                    if (match is not null)
                    {
                        match.UpdateNote(note, now);
                    }

                    break;
                case "remove":
                    if (match is not null)
                    {
                        match.Remove(now);
                    }

                    break;
            }
        }

        return working;
    }

    private static List<EquipmentEntry> ReconcileEquipment(IList<EquipmentEntry> existing, Guid ownerUserId, IReadOnlyList<EquipmentMutationInput> commands, DateTimeOffset now)
    {
        var working = existing.ToList();
        var requestedCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (command, index) in commands.Select((item, index) => (item, index)))
        {
            if (!StableCode.TryCreate(command.StableCode, out var code))
            {
                throw new InvalidOperationException("Validated equipment command contained an invalid stable code.");
            }

            requestedCodes.Add(code!.Value);
            var match = working.FirstOrDefault(item => item.StableCode.Value == code.Value);
            // Array position is the only canonical sort order; clients cannot send an ambiguous zero sentinel.
            var sortOrder = index;
            if (match is null)
            {
                working.Add(EquipmentEntry.Create(ownerUserId, code, command.CustomName, command.Capacity, command.CapacityUnit, command.ConstraintNote, sortOrder, now));
                continue;
            }

            match.Update(command.CustomName, command.Capacity, command.CapacityUnit, command.ConstraintNote, sortOrder, now);
        }

        foreach (var item in working.Where(item => !item.IsRemoved && !requestedCodes.Contains(item.StableCode.Value)))
        {
            item.Remove(now);
        }

        return working;
    }

    private static IEnumerable<OrderedCodeEntry> CreateOrderedCodes(Guid ownerUserId, string listName, IReadOnlyList<string> codes, DateTimeOffset now) =>
        codes.Select((code, index) =>
        {
            StableCode.TryCreate(code, out var stableCode);
            return OrderedCodeEntry.Create(ownerUserId, listName, stableCode!, index, now);
        });

    private static void ApplyField<T>(FieldMutation<T>? mutation, bool replaceMissing, Action<T?> applyConfirmed, Action applyRemoved)
    {
        if (mutation is null)
        {
            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm":
                applyConfirmed(mutation.Value);
                break;
            case "remove":
                applyRemoved();
                break;
            case "absent" when replaceMissing:
                applyRemoved();
                break;
        }
    }

    private static void ApplyDisplayName(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.DisplayName = null; p.DisplayNamePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when DisplayName.TryCreate(mutation.Value, out var name):
                SetProfile(profile, p => { p.DisplayName = name; p.DisplayNamePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.DisplayName = null; p.DisplayNamePresence = mutation.Action == "remove" ? ProfileFieldPresence.Removed : ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyCount(UserProfile profile, FieldMutation<int?>? mutation, bool replaceMissing, int min, int max, Action<UserProfile, int?, ProfileFieldPresence> assign)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                assign(profile, null, ProfileFieldPresence.Absent);
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when mutation.Value is int value && value >= min && value <= max:
                assign(profile, value, ProfileFieldPresence.Confirmed);
                break;
            case "remove":
                assign(profile, null, ProfileFieldPresence.Removed);
                break;
            case "absent" when replaceMissing:
                assign(profile, null, ProfileFieldPresence.Absent);
                break;
        }
    }

    private static void ApplyLanguage(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.Language = null; p.LanguagePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when LanguageTag.TryCreate(mutation.Value, out var value):
                SetProfile(profile, p => { p.Language = value; p.LanguagePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.Language = null; p.LanguagePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.Language = null; p.LanguagePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyRegion(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.Region = null; p.RegionPresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when RegionCode.TryCreate(mutation.Value, out var value):
                SetProfile(profile, p => { p.Region = value; p.RegionPresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.Region = null; p.RegionPresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.Region = null; p.RegionPresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyCurrency(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.Currency = null; p.CurrencyPresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when CurrencyCode.TryCreate(mutation.Value, out var value):
                SetProfile(profile, p => { p.Currency = value; p.CurrencyPresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.Currency = null; p.CurrencyPresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.Currency = null; p.CurrencyPresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyMeasurementSystem(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.MeasurementSystem = null; p.MeasurementSystemPresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseMeasurementSystem(mutation.Value, out var value):
                SetProfile(profile, p => { p.MeasurementSystem = value; p.MeasurementSystemPresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.MeasurementSystem = null; p.MeasurementSystemPresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.MeasurementSystem = null; p.MeasurementSystemPresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyTimeZone(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.TimeZone = null; p.TimeZonePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when IanaTimeZoneId.TryCreate(mutation.Value, out var value):
                SetProfile(profile, p => { p.TimeZone = value; p.TimeZonePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.TimeZone = null; p.TimeZonePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.TimeZone = null; p.TimeZonePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyPlanningCadence(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.PlanningCadence = null; p.PlanningCadencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParsePlanningCadence(mutation.Value, out var value):
                SetProfile(profile, p => { p.PlanningCadence = value; p.PlanningCadencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.PlanningCadence = null; p.PlanningCadencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.PlanningCadence = null; p.PlanningCadencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyShoppingCadence(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.ShoppingCadence = null; p.ShoppingCadencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseShoppingCadence(mutation.Value, out var value):
                SetProfile(profile, p => { p.ShoppingCadence = value; p.ShoppingCadencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.ShoppingCadence = null; p.ShoppingCadencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.ShoppingCadence = null; p.ShoppingCadencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyOverallSkill(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.OverallSkill = null; p.OverallSkillPresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseCookingSkillLevel(mutation.Value, out var value):
                SetProfile(profile, p => { p.OverallSkill = value; p.OverallSkillPresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.OverallSkill = null; p.OverallSkillPresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.OverallSkill = null; p.OverallSkillPresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyConfidence(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.Confidence = null; p.ConfidencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseCookingConfidenceLevel(mutation.Value, out var value):
                SetProfile(profile, p => { p.Confidence = value; p.ConfidencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.Confidence = null; p.ConfidencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.Confidence = null; p.ConfidencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyInstructionDetail(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.PreferredInstructionDetail = null; p.PreferredInstructionDetailPresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseInstructionDetailLevel(mutation.Value, out var value):
                SetProfile(profile, p => { p.PreferredInstructionDetail = value; p.PreferredInstructionDetailPresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.PreferredInstructionDetail = null; p.PreferredInstructionDetailPresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.PreferredInstructionDetail = null; p.PreferredInstructionDetailPresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyPrepMinutes(UserProfile profile, FieldMutation<int?>? mutation, bool replaceMissing, Action<UserProfile, int?, ProfileFieldPresence> assign) =>
        ApplyCount(profile, mutation, replaceMissing, 5, 1440, assign);

    private static void ApplyTolerance(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing, Action<UserProfile, PreferenceTolerance?, ProfileFieldPresence> assign)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                assign(profile, null, ProfileFieldPresence.Absent);
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParsePreferenceTolerance(mutation.Value, out var value):
                assign(profile, value, ProfileFieldPresence.Confirmed);
                break;
            case "remove":
                assign(profile, null, ProfileFieldPresence.Removed);
                break;
            case "absent" when replaceMissing:
                assign(profile, null, ProfileFieldPresence.Absent);
                break;
        }
    }

    private static void ApplyRepeatMeal(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.RepeatMealPreference = null; p.RepeatMealPreferencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseRepeatMealPreference(mutation.Value, out var value):
                SetProfile(profile, p => { p.RepeatMealPreference = value; p.RepeatMealPreferencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.RepeatMealPreference = null; p.RepeatMealPreferencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.RepeatMealPreference = null; p.RepeatMealPreferencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyReheating(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.ReheatingPreference = null; p.ReheatingPreferencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseReheatingPreference(mutation.Value, out var value):
                SetProfile(profile, p => { p.ReheatingPreference = value; p.ReheatingPreferencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.ReheatingPreference = null; p.ReheatingPreferencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.ReheatingPreference = null; p.ReheatingPreferencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyLeftover(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.LeftoverPreference = null; p.LeftoverPreferencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseLeftoverPreference(mutation.Value, out var value):
                SetProfile(profile, p => { p.LeftoverPreference = value; p.LeftoverPreferencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.LeftoverPreference = null; p.LeftoverPreferencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.LeftoverPreference = null; p.LeftoverPreferencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyFreezing(UserProfile profile, FieldMutation<string?>? mutation, bool replaceMissing)
    {
        if (mutation is null)
        {
            if (replaceMissing)
            {
                SetProfile(profile, p => { p.FreezingPreference = null; p.FreezingPreferencePresence = ProfileFieldPresence.Absent; });
            }

            return;
        }

        if (mutation.Durability == "temporary")
        {
            return;
        }

        switch (mutation.Action)
        {
            case "confirm" when ProfileEnumParser.TryParseFreezingPreference(mutation.Value, out var value):
                SetProfile(profile, p => { p.FreezingPreference = value; p.FreezingPreferencePresence = ProfileFieldPresence.Confirmed; });
                break;
            case "remove":
                SetProfile(profile, p => { p.FreezingPreference = null; p.FreezingPreferencePresence = ProfileFieldPresence.Removed; });
                break;
            case "absent" when replaceMissing:
                SetProfile(profile, p => { p.FreezingPreference = null; p.FreezingPreferencePresence = ProfileFieldPresence.Absent; });
                break;
        }
    }

    private static void ApplyAdultDeclaration(UserProfile profile, AdultDeclarationMutationInput? mutation, DateTimeOffset now)
    {
        if (mutation is null)
        {
            return;
        }

        SetProfile(profile, p =>
        {
            p.AdultDeclared = mutation.AdultDeclared;
            p.TermsVersion = mutation.TermsVersion.Trim();
            p.PrivacyVersion = mutation.PrivacyVersion?.Trim();
            p.TermsAcceptedAt = mutation.AdultDeclared ? now : null;
        });
    }

    private static void SetProfile(UserProfile profile, Action<UserProfileMutator> mutate) => mutate(new UserProfileMutator(profile));

    private sealed class UserProfileMutator(UserProfile profile)
    {
        public DisplayName? DisplayName { set => profile.DisplayName = value; }
        public ProfileFieldPresence DisplayNamePresence { set => profile.DisplayNamePresence = value; }
        public int? DefaultAdultCount { set => profile.DefaultAdultCount = value; }
        public ProfileFieldPresence DefaultAdultCountPresence { set => profile.DefaultAdultCountPresence = value; }
        public int? DefaultChildCount { set => profile.DefaultChildCount = value; }
        public ProfileFieldPresence DefaultChildCountPresence { set => profile.DefaultChildCountPresence = value; }
        public int? DefaultServingCount { set => profile.DefaultServingCount = value; }
        public ProfileFieldPresence DefaultServingCountPresence { set => profile.DefaultServingCountPresence = value; }
        public LanguageTag? Language { set => profile.Language = value; }
        public ProfileFieldPresence LanguagePresence { set => profile.LanguagePresence = value; }
        public RegionCode? Region { set => profile.Region = value; }
        public ProfileFieldPresence RegionPresence { set => profile.RegionPresence = value; }
        public CurrencyCode? Currency { set => profile.Currency = value; }
        public ProfileFieldPresence CurrencyPresence { set => profile.CurrencyPresence = value; }
        public MeasurementSystem? MeasurementSystem { set => profile.MeasurementSystem = value; }
        public ProfileFieldPresence MeasurementSystemPresence { set => profile.MeasurementSystemPresence = value; }
        public IanaTimeZoneId? TimeZone { set => profile.TimeZone = value; }
        public ProfileFieldPresence TimeZonePresence { set => profile.TimeZonePresence = value; }
        public PlanningCadence? PlanningCadence { set => profile.PlanningCadence = value; }
        public ProfileFieldPresence PlanningCadencePresence { set => profile.PlanningCadencePresence = value; }
        public ShoppingCadence? ShoppingCadence { set => profile.ShoppingCadence = value; }
        public ProfileFieldPresence ShoppingCadencePresence { set => profile.ShoppingCadencePresence = value; }
        public CookingSkillLevel? OverallSkill { set => profile.OverallSkill = value; }
        public ProfileFieldPresence OverallSkillPresence { set => profile.OverallSkillPresence = value; }
        public CookingConfidenceLevel? Confidence { set => profile.Confidence = value; }
        public ProfileFieldPresence ConfidencePresence { set => profile.ConfidencePresence = value; }
        public InstructionDetailLevel? PreferredInstructionDetail { set => profile.PreferredInstructionDetail = value; }
        public ProfileFieldPresence PreferredInstructionDetailPresence { set => profile.PreferredInstructionDetailPresence = value; }
        public int? OrdinaryPrepMinutes { set => profile.OrdinaryPrepMinutes = value; }
        public ProfileFieldPresence OrdinaryPrepMinutesPresence { set => profile.OrdinaryPrepMinutesPresence = value; }
        public int? ExceptionalPrepMinutes { set => profile.ExceptionalPrepMinutes = value; }
        public ProfileFieldPresence ExceptionalPrepMinutesPresence { set => profile.ExceptionalPrepMinutesPresence = value; }
        public PreferenceTolerance? EffortTolerance { set => profile.EffortTolerance = value; }
        public ProfileFieldPresence EffortTolerancePresence { set => profile.EffortTolerancePresence = value; }
        public PreferenceTolerance? CleanupTolerance { set => profile.CleanupTolerance = value; }
        public ProfileFieldPresence CleanupTolerancePresence { set => profile.CleanupTolerancePresence = value; }
        public RepeatMealPreference? RepeatMealPreference { set => profile.RepeatMealPreference = value; }
        public ProfileFieldPresence RepeatMealPreferencePresence { set => profile.RepeatMealPreferencePresence = value; }
        public ReheatingPreference? ReheatingPreference { set => profile.ReheatingPreference = value; }
        public ProfileFieldPresence ReheatingPreferencePresence { set => profile.ReheatingPreferencePresence = value; }
        public LeftoverPreference? LeftoverPreference { set => profile.LeftoverPreference = value; }
        public ProfileFieldPresence LeftoverPreferencePresence { set => profile.LeftoverPreferencePresence = value; }
        public FreezingPreference? FreezingPreference { set => profile.FreezingPreference = value; }
        public ProfileFieldPresence FreezingPreferencePresence { set => profile.FreezingPreferencePresence = value; }
        public bool? AdultDeclared { set => profile.AdultDeclared = value; }
        public string? TermsVersion { set => profile.TermsVersion = value; }
        public string? PrivacyVersion { set => profile.PrivacyVersion = value; }
        public DateTimeOffset? TermsAcceptedAt { set => profile.TermsAcceptedAt = value; }
    }

    private static ProfileView EmptyView(Guid ownerUserId, DateTimeOffset now)
    {
        var profile = UserProfile.Create(ownerUserId, now, Guid.Empty);
        return ToView(new ProfileReadModel(profile, 0, [], [], []), profileExists: false);
    }

    private static ProfileView ToView(ProfileReadModel model) => ToView(model, profileExists: true);

    private static ProfileView ToView(ProfileReadModel model, bool profileExists)
    {
        var profile = model.Profile;
        string? defaultLanguage = "en";
        string? defaultMeasurement = MeasurementSystem.Metric.ToString();
        return new ProfileView(
            profile.OwnerUserId,
            Field<string?>(profile.DisplayName?.Value, profile.DisplayNamePresence, null),
            new HouseholdView(
                Field(profile.DefaultAdultCount, profile.DefaultAdultCountPresence, 1),
                Field(profile.DefaultChildCount, profile.DefaultChildCountPresence, 0),
                Field(profile.DefaultServingCount, profile.DefaultServingCountPresence, 1),
                Field<string?>(profile.Language?.Value, profile.LanguagePresence, defaultLanguage),
                Field<string?>(profile.Region?.Value, profile.RegionPresence, null),
                Field<string?>(profile.Currency?.Value, profile.CurrencyPresence, null),
                Field<string?>(profile.MeasurementSystem?.ToString(), profile.MeasurementSystemPresence, defaultMeasurement),
                Field<string?>(profile.TimeZone?.Value, profile.TimeZonePresence, null),
                Field<string?>(profile.PlanningCadence?.ToString(), profile.PlanningCadencePresence, null),
                Field<string?>(profile.ShoppingCadence?.ToString(), profile.ShoppingCadencePresence, null)),
            new CookingContextView(
                Field<string?>(profile.OverallSkill?.ToString(), profile.OverallSkillPresence, null),
                Field<string?>(profile.Confidence?.ToString(), profile.ConfidencePresence, null),
                Field<string?>(profile.PreferredInstructionDetail?.ToString(), profile.PreferredInstructionDetailPresence, null),
                Field(profile.OrdinaryPrepMinutes, profile.OrdinaryPrepMinutesPresence, null),
                Field(profile.ExceptionalPrepMinutes, profile.ExceptionalPrepMinutesPresence, null),
                Field<string?>(profile.EffortTolerance?.ToString(), profile.EffortTolerancePresence, null),
                Field<string?>(profile.CleanupTolerance?.ToString(), profile.CleanupTolerancePresence, null),
                Field<string?>(profile.RepeatMealPreference?.ToString(), profile.RepeatMealPreferencePresence, null),
                Field<string?>(profile.ReheatingPreference?.ToString(), profile.ReheatingPreferencePresence, null),
                Field<string?>(profile.LeftoverPreference?.ToString(), profile.LeftoverPreferencePresence, null),
                Field<string?>(profile.FreezingPreference?.ToString(), profile.FreezingPreferencePresence, null)),
            new AdultDeclarationView(profile.AdultDeclared, profile.TermsVersion, profile.PrivacyVersion, profile.TermsAcceptedAt, profile.AdultDeclarationState.ToString()),
            Codes(model, ProfileListNames.KnownTechniques),
            Codes(model, ProfileListNames.TechniquesToLearn),
            Codes(model, ProfileListNames.Goals),
            Codes(model, ProfileListNames.AbandonmentReasons),
            profile.ConcurrencyToken,
            profileExists ? profile.CreatedAt : null,
            profileExists ? profile.UpdatedAt : null,
            profileExists);
    }

    private static IReadOnlyList<ProfileHistoryRedaction.ValidatedPreferenceCommand> ToValidatedPreferenceCommands(IReadOnlyList<PreferenceMutationInput> entries)
    {
        var commands = new List<ProfileHistoryRedaction.ValidatedPreferenceCommand>(entries.Count);
        foreach (var entry in entries)
        {
            ProfileEnumParser.TryParsePreferenceCategory(entry.Category, out var category);
            StableCode.TryCreate(entry.StableCode, out var code);
            commands.Add(new ProfileHistoryRedaction.ValidatedPreferenceCommand(category, code!, entry.Action));
        }

        return commands;
    }

    private static IReadOnlyList<string> Codes(ProfileReadModel model, string listName) =>
        model.OrderedCodes.Where(item => item.ListName == listName).OrderBy(item => item.SortOrder).Select(item => item.StableCode.Value).ToList();

    private static ProfileFieldView<T> Field<T>(T? value, ProfileFieldPresence presence, T? defaultValue)
    {
        var presenceName = presence switch
        {
            ProfileFieldPresence.Confirmed => "confirmed",
            ProfileFieldPresence.Removed => "removed",
            ProfileFieldPresence.Absent when defaultValue is not null => "default",
            _ => "absent"
        };
        T? resolved = presence switch
        {
            ProfileFieldPresence.Confirmed => value,
            ProfileFieldPresence.Absent when defaultValue is not null => defaultValue,
            _ => default
        };
        return new ProfileFieldView<T>(resolved, presenceName, defaultValue, "durable");
    }

    private static PreferenceView ToPreferenceView(PreferenceEntry entry) =>
        new(entry.Id, entry.Category.ToString(), entry.StableCode.Value, entry.Note?.Value, entry.Presence.ToString().ToLowerInvariant(), entry.SortOrder);

    private static EquipmentView ToEquipmentView(EquipmentEntry entry) =>
        new(entry.Id, entry.StableCode.Value, entry.CustomName, entry.Capacity, entry.CapacityUnit, entry.ConstraintNote, !entry.IsRemoved, entry.SortOrder);
}

/// <summary>Thin use-case handlers delegating to the profile workflow.</summary>
public sealed class GetProfileHandler(ProfileApplicationWorkflow workflow) : IGetProfileUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<ProfileView>> GetAsync(CancellationToken cancellationToken) => workflow.GetAsync(cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class PutProfileHandler(ProfileApplicationWorkflow workflow) : IPutProfileUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<ProfileView>> PutAsync(PutProfileCommand command, CancellationToken cancellationToken) => workflow.PutAsync(command, cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class PatchProfileHandler(ProfileApplicationWorkflow workflow) : IPatchProfileUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<ProfileView>> PatchAsync(PatchProfileCommand command, CancellationToken cancellationToken) => workflow.PatchAsync(command, cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class GetPreferencesHandler(ProfileApplicationWorkflow workflow) : IGetPreferencesUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> GetAsync(CancellationToken cancellationToken) => workflow.GetPreferencesAsync(cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class PutPreferencesHandler(ProfileApplicationWorkflow workflow) : IPutPreferencesUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<VersionedCollectionView<PreferenceView>>> PutAsync(PutPreferencesCommand command, CancellationToken cancellationToken) => workflow.PutPreferencesAsync(command, cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class GetEquipmentHandler(ProfileApplicationWorkflow workflow) : IGetEquipmentUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> GetAsync(CancellationToken cancellationToken) => workflow.GetEquipmentAsync(cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class PutEquipmentHandler(ProfileApplicationWorkflow workflow) : IPutEquipmentUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<VersionedCollectionView<EquipmentView>>> PutAsync(PutEquipmentCommand command, CancellationToken cancellationToken) => workflow.PutEquipmentAsync(command, cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class GetProfileCompletenessHandler(ProfileApplicationWorkflow workflow) : IGetProfileCompletenessUseCase
{
    /// <inheritdoc />
    public Task<ProfileApplicationResult<ProfileCompletenessView>> GetAsync(CancellationToken cancellationToken) => workflow.GetCompletenessAsync(cancellationToken);
}

/// <summary>Thin use-case handler delegating to the profile workflow.</summary>
public sealed class GetProfileSessionProjectionHandler(ProfileApplicationWorkflow workflow) : IGetProfileSessionProjectionUseCase
{
    /// <inheritdoc />
    public Task<ProfileSessionProjection> GetAsync(Guid ownerUserId, CancellationToken cancellationToken) => workflow.GetSessionProjectionAsync(ownerUserId, cancellationToken);
}
