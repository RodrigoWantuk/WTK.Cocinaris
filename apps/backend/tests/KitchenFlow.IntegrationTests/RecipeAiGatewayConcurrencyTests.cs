using KitchenFlow.Infrastructure.Persistence;
using KitchenFlow.Modules.Ai.Abstractions;
using KitchenFlow.Modules.Ai.Providers;
using KitchenFlow.Modules.Ai.Usage;
using KitchenFlow.Modules.Identity;
using KitchenFlow.Modules.Recipes.Ai;
using KitchenFlow.Modules.Recipes.Application;
using KitchenFlow.Modules.Recipes.Domain;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace KitchenFlow.IntegrationTests;

/// <summary>
/// Focused PostgreSQL concurrency tests for PLAN-0028 cook-now claim, selection, and usage reservation.
/// Uses FakeAiProvider only — no live DeepSeek calls.
/// </summary>
public sealed class RecipeAiGatewayConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18.4")
        .WithDatabase("kitchenflow_recipe_ai_test")
        .WithUsername("kitchenflow_test")
        .WithPassword("development-only-test-password")
        .Build();

    /// <inheritdoc />
    public Task InitializeAsync() => _postgres.StartAsync();

    /// <inheritdoc />
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentRequestCandidatesSameIdempotencyKey_OneSessionOneProviderCallOneSettle()
    {
        var options = CreateOptions();
        await MigrateAsync(options);
        var ownerId = await SeedUserAsync(options);
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);
        var key = Guid.NewGuid();

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);
        var results = new RecipeApplicationResult<RecipeGenerationSessionView>[2];

        async Task Run(int index)
        {
            ready.Signal();
            start.Wait();
            await using var context = new ApplicationDbContext(options);
            var service = CreateService(context, ownerId, provider, new AiUsageOptions());
            results[index] = await service.RequestCandidatesAsync(new RequestCandidatesCommand(key, $"corr-{index}"), CancellationToken.None);
        }

        var tasks = new[] { Task.Run(() => Run(0)), Task.Run(() => Run(1)) };
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        start.Set();
        await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Null(result.Problem));
        Assert.Equal(results[0].Value!.SessionId, results[1].Value!.SessionId);
        Assert.Equal(1, provider.InvocationCount(AiOperationRegistry.SuggestCandidates));

        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(1, await verification.RecipeGenerationSessions.CountAsync(item => item.OwnerUserId == ownerId && item.IdempotencyKey == key));
        Assert.Equal(1, await verification.AiUsageLedgerEntries.CountAsync(item =>
            item.OwnerUserId == ownerId
            && item.Operation == AiOperationRegistry.SuggestCandidates
            && item.Status == nameof(AiUsageEntryStatus.Settled)));
    }

    [Fact]
    public async Task ConcurrentSelectCandidate_OneExpandOneRecipe_AndSameKeyDifferentCandidateConflicts()
    {
        var options = CreateOptions();
        await MigrateAsync(options);
        var ownerId = await SeedUserAsync(options);
        var provider = new FakeAiProvider(useProtocolDefaultsWhenUnscripted: true);

        await using (var setup = new ApplicationDbContext(options))
        {
            var setupService = CreateService(setup, ownerId, provider, new AiUsageOptions());
            var sessionResult = await setupService.RequestCandidatesAsync(new RequestCandidatesCommand(Guid.NewGuid(), "setup"), CancellationToken.None);
            Assert.Null(sessionResult.Problem);
            var sessionId = sessionResult.Value!.SessionId;
            var candidateA = sessionResult.Value.Candidates![0].CandidateId;
            var candidateB = sessionResult.Value.Candidates![1].CandidateId;
            var expandBefore = provider.InvocationCount(AiOperationRegistry.ExpandSelected);

            using var ready = new CountdownEvent(2);
            using var start = new ManualResetEventSlim(false);
            var results = new RecipeApplicationResult<RecipeDetailView>[2];
            var selectKeyA = Guid.NewGuid();
            var selectKeyB = Guid.NewGuid();

            async Task Run(int index, string candidateId, Guid selectKey)
            {
                ready.Signal();
                start.Wait();
                await using var context = new ApplicationDbContext(options);
                var service = CreateService(context, ownerId, provider, new AiUsageOptions());
                results[index] = await service.SelectCandidateAsync(new SelectCandidateCommand(sessionId, candidateId, selectKey, $"sel-{index}"), CancellationToken.None);
            }

            var tasks = new[]
            {
                Task.Run(() => Run(0, candidateA, selectKeyA)),
                Task.Run(() => Run(1, candidateB, selectKeyB))
            };
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            start.Set();
            await Task.WhenAll(tasks);

            var successes = results.Where(item => item.Problem is null).ToList();
            var conflicts = results.Where(item => item.Problem?.ErrorCode == "ai_operation_conflict").ToList();
            Assert.Single(successes);
            Assert.Single(conflicts);
            Assert.Equal(expandBefore + 1, provider.InvocationCount(AiOperationRegistry.ExpandSelected));

            await using var verification = new ApplicationDbContext(options);
            Assert.Equal(1, await verification.Recipes.CountAsync(item => item.OwnerUserId == ownerId));
            Assert.Equal(1, await verification.RecipeRevisions.CountAsync(item => item.OwnerUserId == ownerId));
            Assert.Equal(1, await verification.RecipeGenerationSessions.CountAsync(item =>
                item.OwnerUserId == ownerId
                && item.Id == sessionId
                && item.Status == nameof(RecipeGenerationSessionStatus.Selected)));

            var winningKey = results[0].Problem is null ? selectKeyA : selectKeyB;
            var losingCandidate = results[0].Problem is null ? candidateB : candidateA;
            await using var conflictContext = new ApplicationDbContext(options);
            var conflictService = CreateService(conflictContext, ownerId, provider, new AiUsageOptions());
            var conflict = await conflictService.SelectCandidateAsync(
                new SelectCandidateCommand(sessionId, losingCandidate, winningKey, "same-key-diff-candidate"),
                CancellationToken.None);
            Assert.Equal("ai_operation_conflict", conflict.Problem!.ErrorCode);
        }
    }

    [Fact]
    public async Task ConcurrentTryReserve_CeilingPermitsExactlyOneReservationAndOneLedgerRow()
    {
        var options = CreateOptions();
        await MigrateAsync(options);
        var ownerId = await SeedUserAsync(options);
        var ceilings = new AiUsageOptions
        {
            GlobalDailyUnitCeiling = 3,
            UserDailyUnitCeiling = 3,
            UserConcurrencyCeiling = 2
        };

        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim(false);
        var results = new AiUsageReservationResult[2];

        async Task Run(int index)
        {
            ready.Signal();
            start.Wait();
            await using var context = new ApplicationDbContext(options);
            var store = new PostgreSqlAiUsageLedgerStore(context);
            results[index] = await store.TryReserveAsync(
                ownerId,
                AiOperationRegistry.SuggestCandidates,
                estimatedUnits: 3,
                correlationId: $"reserve-{index}",
                now: DateTimeOffset.UtcNow,
                ceilings: ceilings,
                cancellationToken: CancellationToken.None);
        }

        var tasks = new[] { Task.Run(() => Run(0)), Task.Run(() => Run(1)) };
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        start.Set();
        await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(item => item.Outcome == AiUsageReservationOutcome.Reserved));
        Assert.Equal(1, results.Count(item =>
            item.Outcome is AiUsageReservationOutcome.UserBudgetExhausted or AiUsageReservationOutcome.GlobalBudgetExhausted));

        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(1, await verification.AiUsageLedgerEntries.CountAsync(item => item.OwnerUserId == ownerId));
    }

    private DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    private static async Task MigrateAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
    }

    private static async Task<Guid> SeedUserAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        var ownerId = Guid.NewGuid();
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
        return ownerId;
    }

    private static RecipeCookNowApplicationService CreateService(
        ApplicationDbContext context,
        Guid ownerId,
        FakeAiProvider provider,
        AiUsageOptions usageOptions)
    {
        var generationStore = new PostgreSqlRecipeGenerationStore(context);
        var recipeStore = new PostgreSqlRecipeStore(context);
        var ledger = new PostgreSqlAiUsageLedgerStore(context);
        var unitOfWork = new PostgreSqlRecipeCookNowUnitOfWork(context);
        return new RecipeCookNowApplicationService(
            new FixedCurrentUserAccessor(ownerId),
            generationStore,
            recipeStore,
            unitOfWork,
            new EmptyRecipeContextAssembler(),
            provider,
            AiOperationRegistry.CreateDefault(),
            new AiUsageGovernor(ledger, usageOptions, TimeProvider.System),
            TimeProvider.System);
    }

    private sealed class FixedCurrentUserAccessor(Guid userId) : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InternalUser(userId, "https://issuer.test", "subject", DateTimeOffset.UtcNow));
    }

    private sealed class EmptyRecipeContextAssembler : IRecipeContextAssembler
    {
        public Task<RecipeSuggestRequestContext> AssembleSuggestContextAsync(Guid ownerUserId, string requestId, int contextBudgetCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(new RecipeSuggestRequestContext(requestId, [], [], new RecipeUserPresets(2, [], [], []), new RecipeExecutionContext("cook_now", null)));

        public Task<RecipeExpandRequestContext> AssembleExpandContextAsync(Guid ownerUserId, string requestId, SuggestCandidate selectedCandidate, int contextBudgetCharacters, CancellationToken cancellationToken) =>
            Task.FromResult(new RecipeExpandRequestContext(
                requestId,
                selectedCandidate.InventoryUses.Select(use => new RecipeInventoryContextItem(use.InventoryItemId, use.UserName, use.RequiredQuantity, use.Unit, use.AvailabilitySource, use.IngredientRef)).ToList(),
                [],
                new RecipeUserPresets(2, [], [], []),
                new RecipeSelectedCandidateContext(
                    selectedCandidate.CandidateId,
                    selectedCandidate.Name,
                    [selectedCandidate.TargetMealType],
                    selectedCandidate.Servings,
                    selectedCandidate.RequiredEquipmentIds,
                    selectedCandidate.InventoryUses.Select(use => new RecipeInventoryContextItem(use.InventoryItemId, use.UserName, use.RequiredQuantity, use.Unit, use.AvailabilitySource, use.IngredientRef)).ToList(),
                    selectedCandidate.AdditionalIngredients.Select(item => (item.Name, item.RequiredQuantity, item.Unit, item.Optional)).ToList())));
    }
}
