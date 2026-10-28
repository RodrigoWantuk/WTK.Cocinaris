using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.UnitTests;

/// <summary>Unit coverage for privacy-safe preference history and canonical audit codes.</summary>
public sealed class ProfileHistoryRedactionTests
{
    public static TheoryData<string> AllergyCategoryCasings =>
    [
        "Allergy",
        "allergy",
        "ALLERGY",
        "aLlErGy"
    ];

    public static TheoryData<string> MedicalRestrictionCategoryCasings =>
    [
        "MedicalRestriction",
        "medicalRestriction",
        "medicalrestriction",
        "MEDICALRESTRICTION"
    ];

    [Theory]
    [MemberData(nameof(AllergyCategoryCasings))]
    public void AllergyCategoryVariantsAreAlwaysRedacted(string category)
    {
        Assert.True(ProfileEnumParser.TryParsePreferenceCategory(category, out var parsed));
        Assert.Equal(PreferenceCategory.Allergy, parsed);
        Assert.True(StableCode.TryCreate("peanut", out var code));

        foreach (var action in new[] { "add", "remove", "update" })
        {
            var fieldCode = ProfileHistoryRedaction.RedactPreferenceCommand(parsed, code!, action);
            Assert.DoesNotContain("peanut", fieldCode, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("allergy_entry_", fieldCode, StringComparison.Ordinal);
            Assert.DoesNotContain(':', fieldCode);
        }

        var commands = new[]
        {
            new ProfileHistoryRedaction.ValidatedPreferenceCommand(parsed, code!, "add")
        };
        var codes = ProfileHistoryRedaction.RedactPreferenceFieldCodes(commands);
        Assert.Equal(["allergy_entry_added"], codes);
    }

    [Theory]
    [MemberData(nameof(MedicalRestrictionCategoryCasings))]
    public void MedicalRestrictionCategoryVariantsAreAlwaysRedacted(string category)
    {
        Assert.True(ProfileEnumParser.TryParsePreferenceCategory(category, out var parsed));
        Assert.Equal(PreferenceCategory.MedicalRestriction, parsed);
        Assert.True(StableCode.TryCreate("low_sodium", out var code));

        foreach (var action in new[] { "add", "remove", "update" })
        {
            var fieldCode = ProfileHistoryRedaction.RedactPreferenceCommand(parsed, code!, action);
            Assert.DoesNotContain("low_sodium", fieldCode, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("medical_restriction_", fieldCode, StringComparison.Ordinal);
            Assert.DoesNotContain(':', fieldCode);
        }

        var codes = ProfileHistoryRedaction.RedactPreferenceFieldCodes(
        [
            new ProfileHistoryRedaction.ValidatedPreferenceCommand(parsed, code!, "remove")
        ]);
        Assert.Equal(["medical_restriction_removed"], codes);
    }

    [Fact]
    public void NonSensitiveCategoriesUseCanonicalCategoryAndStableCode()
    {
        Assert.True(ProfileEnumParser.TryParsePreferenceCategory("dietarypattern", out var category));
        Assert.True(StableCode.TryCreate(" vegetarian ", out var code));
        var fieldCode = ProfileHistoryRedaction.RedactPreferenceCommand(category, code!, "add");
        Assert.Equal("DietaryPattern:vegetarian", fieldCode);
        Assert.DoesNotContain("dietarypattern", fieldCode, StringComparison.Ordinal);
        Assert.DoesNotContain(" vegetarian ", fieldCode, StringComparison.Ordinal);
    }

    [Fact]
    public void EquipmentHistoryUsesDistinctOrderedCanonicalCodes()
    {
        var codes = ProfileHistoryRedaction.CanonicalEquipmentFieldCodes(["oven", "blender", "oven", "air_fryer"]);
        Assert.Equal(["air_fryer", "blender", "oven"], codes);
    }

    [Fact]
    public async Task PutPreferencesHistoryUsesValidatedCategoryEnumsNotRawStrings()
    {
        var ownerId = Guid.NewGuid();
        var readStore = new CapturingReadStore();
        var writeStore = new CapturingWriteStore(readStore);
        await writeStore.CreateAsync(new ProfileMutationWrite(ownerId, UserProfile.Create(ownerId, DateTimeOffset.UtcNow, Guid.NewGuid()), [], [], [], "profile", ["created"], "bootstrap", 0), CancellationToken.None);
        var workflow = new ProfileApplicationWorkflow(new FixedUser(ownerId), readStore, writeStore, TimeProvider.System);
        var model = await readStore.FindAsync(ownerId, CancellationToken.None);

        var result = await new PutPreferencesHandler(workflow).PutAsync(
            new PutPreferencesCommand(
                [new PreferenceMutationInput("add", "allergy", "peanut", "severe")],
                ProfileVersionPrecondition.Valid(model!.Profile.ConcurrencyToken),
                "test"),
            CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(["allergy_entry_added"], writeStore.LastWrite!.ChangedFieldCodes);
        Assert.DoesNotContain(writeStore.LastWrite.ChangedFieldCodes, code => code.Contains("peanut", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(writeStore.LastWrite.ChangedFieldCodes, code => code.Contains("severe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PutEquipmentHistoryUsesCanonicalStableCodes()
    {
        var ownerId = Guid.NewGuid();
        var readStore = new CapturingReadStore();
        var writeStore = new CapturingWriteStore(readStore);
        await writeStore.CreateAsync(new ProfileMutationWrite(ownerId, UserProfile.Create(ownerId, DateTimeOffset.UtcNow, Guid.NewGuid()), [], [], [], "profile", ["created"], "bootstrap", 0), CancellationToken.None);
        var workflow = new ProfileApplicationWorkflow(new FixedUser(ownerId), readStore, writeStore, TimeProvider.System);
        var model = await readStore.FindAsync(ownerId, CancellationToken.None);

        var result = await new PutEquipmentHandler(workflow).PutAsync(
            new PutEquipmentCommand(
                [
                    new EquipmentMutationInput(" oven ", null, null, null, null),
                    new EquipmentMutationInput("blender", null, null, null, null)
                ],
                ProfileVersionPrecondition.Valid(model!.Profile.ConcurrencyToken),
                "test"),
            CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(["blender", "oven"], writeStore.LastWrite!.ChangedFieldCodes);
        Assert.DoesNotContain(writeStore.LastWrite.ChangedFieldCodes, code => code.Contains(' '));
    }

    [Fact]
    public async Task AdultDeclarationValidationUsesNestedErrorPaths()
    {
        var ownerId = Guid.NewGuid();
        var workflow = new ProfileApplicationWorkflow(new FixedUser(ownerId), new CapturingReadStore(), new CapturingWriteStore(new CapturingReadStore()), TimeProvider.System);
        var result = await new PutProfileHandler(workflow).PutAsync(
            new PutProfileCommand(
                EmptyMutation() with
                {
                    AdultDeclaration = new AdultDeclarationMutationInput(true, "", new string('p', 33))
                },
                ProfileVersionPrecondition.Missing,
                "test"),
            CancellationToken.None);

        Assert.Equal("validation_failed", result.Problem!.ErrorCode);
        Assert.True(result.Problem.Errors!.ContainsKey("adultDeclaration.termsVersion"));
        Assert.True(result.Problem.Errors.ContainsKey("adultDeclaration.privacyVersion"));
        Assert.False(result.Problem.Errors.ContainsKey("termsVersion"));
        Assert.False(result.Problem.Errors.ContainsKey("privacyVersion"));
    }

    [Theory]
    [InlineData(null, "durable")]
    [InlineData("confirm", null)]
    [InlineData("", "")]
    [InlineData(null, null)]
    public async Task MalformedDisplayNameMutationsReturnValidationFailed(string? action, string? durability)
    {
        var ownerId = Guid.NewGuid();
        var workflow = new ProfileApplicationWorkflow(new FixedUser(ownerId), new CapturingReadStore(), new CapturingWriteStore(new CapturingReadStore()), TimeProvider.System);
        var result = await new PatchProfileHandler(workflow).PatchAsync(
            new PatchProfileCommand(
                EmptyMutation() with
                {
                    DisplayName = new FieldMutation<string?>(action, "Alex", durability)
                },
                ProfileVersionPrecondition.Missing,
                "test"),
            CancellationToken.None);

        Assert.Equal("validation_failed", result.Problem!.ErrorCode);
        Assert.True(result.Problem.Errors!.ContainsKey("displayName"));
    }

    private static ProfileMutationInput EmptyMutation() =>
        new(
            DisplayName: null,
            DefaultAdultCount: null,
            DefaultChildCount: null,
            DefaultServingCount: null,
            Language: null,
            Region: null,
            Currency: null,
            MeasurementSystem: null,
            TimeZone: null,
            PlanningCadence: null,
            ShoppingCadence: null,
            OverallSkill: null,
            Confidence: null,
            PreferredInstructionDetail: null,
            OrdinaryPrepMinutes: null,
            ExceptionalPrepMinutes: null,
            EffortTolerance: null,
            CleanupTolerance: null,
            RepeatMealPreference: null,
            ReheatingPreference: null,
            LeftoverPreference: null,
            FreezingPreference: null,
            AdultDeclaration: null,
            KnownTechniques: null,
            TechniquesToLearn: null,
            Goals: null,
            AbandonmentReasons: null);

    private sealed class FixedUser(Guid id) : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InternalUser(id, "test", "sub", DateTimeOffset.UtcNow));
    }

    private sealed class CapturingReadStore : IProfileReadStore
    {
        private ProfileReadModel? _model;

        public Task<ProfileReadModel?> FindAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult(_model);

        public Task<ProfileSessionProjection> FindSessionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileSessionProjection(_model is not null, _model?.Profile.DisplayName?.Value, "en", null, "Metric", 0, AdultDeclarationState.NotDeclared));

        public Task<IReadOnlyList<ProfileChangeHistoryEntry>> FindHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProfileChangeHistoryEntry>>([]);

        public Task<ProfileExportProjection> BuildExportProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileExportProjection(ownerUserId, new Dictionary<string, string?>(), [], [], [], [], [], [], AdultDeclarationState.NotDeclared, null));

        public Task<ProfileDeletionProjection> BuildDeletionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileDeletionProjection(ownerUserId, _model is not null));

        public void Save(ProfileReadModel model) => _model = model;
    }

    private sealed class CapturingWriteStore(CapturingReadStore readStore) : IProfileWriteStore
    {
        public ProfileMutationWrite? LastWrite { get; private set; }

        public Task<ProfileWriteOutcome> CreateAsync(ProfileMutationWrite write, CancellationToken cancellationToken)
        {
            LastWrite = write;
            readStore.Save(new ProfileReadModel(write.Profile, write.ExpectedVersion + 1, write.Preferences, write.Equipment, write.OrderedCodes));
            return Task.FromResult(ProfileWriteOutcome.Saved);
        }

        public Task<ProfileWriteOutcome> SaveAsync(ProfileMutationWrite write, CancellationToken cancellationToken) => CreateAsync(write, cancellationToken);
    }
}
