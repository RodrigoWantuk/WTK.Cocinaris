using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Profiles.Application;
using Cocinaris.Modules.Profiles.Domain;

namespace Cocinaris.UnitTests;

public sealed class ProfileApplicationUseCaseTests
{
    [Fact]
    public async Task PatchCreatesProfileWithoutIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var readStore = new TestProfileReadStore();
        var writeStore = new TestProfileWriteStore(readStore);
        var workflow = new ProfileApplicationWorkflow(new TestCurrentUser(ownerId), readStore, writeStore, TimeProvider.System);
        var handler = new PatchProfileHandler(workflow);
        var result = await handler.PatchAsync(new PatchProfileCommand(
            new ProfileMutationInput(
                DisplayName: new FieldMutation<string?>("confirm", "Alex", "durable"),
                DefaultAdultCount: null,
                DefaultChildCount: null,
                DefaultServingCount: null,
                Language: new FieldMutation<string?>("confirm", "en", "durable"),
                Region: new FieldMutation<string?>("confirm", "BR", "durable"),
                Currency: new FieldMutation<string?>("confirm", "BRL", "durable"),
                MeasurementSystem: new FieldMutation<string?>("confirm", "Metric", "durable"),
                TimeZone: new FieldMutation<string?>("confirm", "America/Sao_Paulo", "durable"),
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
                AbandonmentReasons: null),
            ProfileVersionPrecondition.Missing,
            "test"), CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal("Alex", result.Value!.DisplayName.Value);
        Assert.Equal("confirmed", result.Value.DisplayName.Presence);
    }

    [Fact]
    public async Task PutProfileClearsOmittedDisplayNameToAbsent()
    {
        var ownerId = Guid.NewGuid();
        var readStore = new TestProfileReadStore();
        var writeStore = new TestProfileWriteStore(readStore);
        var workflow = new ProfileApplicationWorkflow(new TestCurrentUser(ownerId), readStore, writeStore, TimeProvider.System);
        var patchHandler = new PatchProfileHandler(workflow);
        await patchHandler.PatchAsync(new PatchProfileCommand(
            new ProfileMutationInput(
                DisplayName: new FieldMutation<string?>("confirm", "Alex", "durable"),
                DefaultAdultCount: null,
                DefaultChildCount: null,
                DefaultServingCount: null,
                Language: new FieldMutation<string?>("confirm", "pt-BR", "durable"),
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
                AbandonmentReasons: null),
            ProfileVersionPrecondition.Missing,
            "test"), CancellationToken.None);

        var putHandler = new PutProfileHandler(workflow);
        var model = await readStore.FindAsync(ownerId, CancellationToken.None);
        var result = await putHandler.PutAsync(new PutProfileCommand(
            new ProfileMutationInput(
                DisplayName: null,
                DefaultAdultCount: null,
                DefaultChildCount: null,
                DefaultServingCount: null,
                Language: new FieldMutation<string?>("confirm", "en", "durable"),
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
                AbandonmentReasons: null),
            ProfileVersionPrecondition.Valid(model!.Profile.ConcurrencyToken),
            "test"), CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal("absent", result.Value!.DisplayName.Presence);
        Assert.Equal("confirmed", result.Value.Household.Language.Presence);
        Assert.Equal("en", result.Value.Household.Language.Value);
    }

    [Fact]
    public async Task InvalidDurabilityIsRejected()
    {
        var ownerId = Guid.NewGuid();
        var workflow = new ProfileApplicationWorkflow(new TestCurrentUser(ownerId), new TestProfileReadStore(), new TestProfileWriteStore(new TestProfileReadStore()), TimeProvider.System);
        var handler = new PatchProfileHandler(workflow);
        var result = await handler.PatchAsync(new PatchProfileCommand(
            new ProfileMutationInput(
                DisplayName: new FieldMutation<string?>("confirm", "Alex", "temporary"),
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
                AbandonmentReasons: null),
            ProfileVersionPrecondition.Missing,
            "test"), CancellationToken.None);

        Assert.Equal("validation_failed", result.Problem!.ErrorCode);
    }

    [Fact]
    public void PreferenceHistoryRedactionUsesGenericCodes()
    {
        Assert.True(StableCode.TryCreate("peanut_allergy", out var code));
        var codes = ProfileHistoryRedaction.RedactPreferenceFieldCodes(
        [
            new ProfileHistoryRedaction.ValidatedPreferenceCommand(PreferenceCategory.Allergy, code!, "add")
        ]);
        Assert.Contains("allergy_entry_added", codes);
        Assert.DoesNotContain(codes, item => item.Contains("peanut", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PutPreferencesRejectsInvalidCategory()
    {
        var ownerId = Guid.NewGuid();
        var readStore = new TestProfileReadStore();
        var writeStore = new TestProfileWriteStore(readStore);
        await writeStore.CreateAsync(new ProfileMutationWrite(ownerId, UserProfile.Create(ownerId, DateTimeOffset.UtcNow, Guid.NewGuid()), [], [], [], "profile", ["created"], "bootstrap", 0), CancellationToken.None);
        var workflow = new ProfileApplicationWorkflow(new TestCurrentUser(ownerId), readStore, writeStore, TimeProvider.System);
        var handler = new PutPreferencesHandler(workflow);
        var model = await readStore.FindAsync(ownerId, CancellationToken.None);
        var result = await handler.PutAsync(new PutPreferencesCommand(
            [new PreferenceMutationInput("add", "InvalidCategory", "peanut_allergy", null)],
            ProfileVersionPrecondition.Valid(model!.Profile.ConcurrencyToken),
            "test"), CancellationToken.None);

        Assert.Equal("validation_failed", result.Problem!.ErrorCode);
    }

    private sealed class TestCurrentUser(Guid id) : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(new InternalUser(id, "test", "sub", DateTimeOffset.UtcNow));
    }

    private sealed class TestProfileReadStore : IProfileReadStore
    {
        private ProfileReadModel? _model;

        public Task<ProfileReadModel?> FindAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult(_model);

        public Task<ProfileSessionProjection> FindSessionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileSessionProjection(_model is not null, _model?.Profile.DisplayName?.Value, "en", null, "Metric", 0, AdultDeclarationState.NotDeclared));

        public Task<IReadOnlyList<ProfileChangeHistoryEntry>> FindHistoryAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProfileChangeHistoryEntry>>([]);

        public Task<ProfileExportProjection> BuildExportProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileExportProjection(ownerUserId, new Dictionary<string, string?>(), [], [], [], [], [], [], AdultDeclarationState.NotDeclared, null));

        public Task<ProfileDeletionProjection> BuildDeletionProjectionAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileDeletionProjection(ownerUserId, _model is not null));

        public void Save(ProfileReadModel model) => _model = model;
    }

    private sealed class TestProfileWriteStore(TestProfileReadStore readStore) : IProfileWriteStore
    {
        public Task<ProfileWriteOutcome> CreateAsync(ProfileMutationWrite write, CancellationToken cancellationToken)
        {
            readStore.Save(new ProfileReadModel(write.Profile, 1, write.Preferences, write.Equipment, write.OrderedCodes));
            return Task.FromResult(ProfileWriteOutcome.Saved);
        }

        public Task<ProfileWriteOutcome> SaveAsync(ProfileMutationWrite write, CancellationToken cancellationToken)
        {
            readStore.Save(new ProfileReadModel(write.Profile, write.ExpectedVersion + 1, write.Preferences, write.Equipment, write.OrderedCodes));
            return Task.FromResult(ProfileWriteOutcome.Saved);
        }
    }
}
