using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.UnitTests;

/// <summary>Tests deterministic preparation orchestration without HTTP or PostgreSQL dependencies.</summary>
public sealed class PreparedComponentApplicationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PrepareConsumesParentsCreatesPortionsAndReplaysWithoutSecondConsumption()
    {
        var fixture = new Fixture();
        var result = await fixture.Workflow.PrepareAsync(fixture.Command(), CancellationToken.None);

        Assert.Null(result.Problem);
        Assert.Equal(InventoryApplicationSuccess.Created, result.Success);
        Assert.Equal(2m, Assert.IsType<LotQuantity.Measured>(fixture.ParentLot.Quantity).Value);
        Assert.Equal(2, result.Value!.Outputs.Count);
        Assert.Equal(5m, result.Value.DeclaredYield.MeasuredValue);
        Assert.All(result.Value.Outputs, output => Assert.Equal("Prepared", output.PreparedMetadata.LifecycleState));

        var replay = await fixture.Workflow.PrepareAsync(fixture.Command(), CancellationToken.None);
        Assert.Null(replay.Problem);
        Assert.Equal(InventoryIdempotencyDisposition.Replayed, replay.Idempotency);
        Assert.Equal(2m, Assert.IsType<LotQuantity.Measured>(fixture.ParentLot.Quantity).Value);
        Assert.Equal(result.Value.BatchId, replay.Value!.BatchId);
    }

    [Fact]
    public async Task PrepareRejectsForeignOrMissingParentsBeforeVersionAndQuantityDetails()
    {
        var fixture = new Fixture(includeParent: false);
        var command = fixture.Command() with
        {
            Inputs = [new PreparationInputCommand(Guid.NewGuid(), 999m, "Gram", InventoryVersionPrecondition.Valid(Guid.NewGuid()))]
        };

        var result = await fixture.Workflow.PrepareAsync(command, CancellationToken.None);

        Assert.Equal("resource_not_found", result.Problem!.ErrorCode);
        Assert.Null(fixture.Store.Saved);
    }

    [Fact]
    public async Task PrepareRequiresEveryInputVersionAndExactYieldPartition()
    {
        var fixture = new Fixture();
        var missingVersion = await fixture.Workflow.PrepareAsync(fixture.Command() with
        {
            Inputs = [new PreparationInputCommand(fixture.ParentLot.Id, 3m, "Gram", InventoryVersionPrecondition.Missing)]
        }, CancellationToken.None);
        Assert.Equal("precondition_required", missingVersion.Problem!.ErrorCode);

        var invalidPartition = await fixture.Workflow.PrepareAsync(fixture.Command() with
        {
            IdempotencyKey = Guid.NewGuid(),
            Outputs = [fixture.Output(4m), fixture.Output(2m)]
        }, CancellationToken.None);
        Assert.Equal("domain_rule_violated", invalidPartition.Problem!.ErrorCode);
        Assert.Null(fixture.Store.Saved);
    }

    [Fact]
    public async Task PrepareDoesNotMutateDetachedParentsWhenLaterOutputValidationFails()
    {
        var fixture = new Fixture();

        var result = await fixture.Workflow.PrepareAsync(fixture.Command() with
        {
            Outputs = [fixture.Output(2m) with { StorageLocation = "Other", CustomLocation = null }, fixture.Output(3m)]
        }, CancellationToken.None);

        Assert.Equal("validation_failed", result.Problem!.ErrorCode);
        Assert.Equal(5m, Assert.IsType<LotQuantity.Measured>(fixture.ParentLot.Quantity).Value);
        Assert.Null(fixture.Store.Saved);
    }

    [Fact]
    public async Task PrepareReplaysTheWinnerWhenTheParentIsStaleAfterTheInitialIdempotencyMiss()
    {
        var fixture = new Fixture();
        var command = fixture.Command();
        var winner = await fixture.Workflow.PrepareAsync(command, CancellationToken.None);
        Assert.Null(winner.Problem);

        // Model the HTTP race: the duplicate misses before loading the now-updated parent, then
        // observes the winner record only after owner-scoped parent resolution.
        fixture.Store.DelayStoredReplayUntilSecondLookup();
        var replay = await fixture.Workflow.PrepareAsync(command, CancellationToken.None);

        Assert.Null(replay.Problem);
        Assert.Equal(InventoryIdempotencyDisposition.Replayed, replay.Idempotency);
        Assert.Equal(winner.Value!.BatchId, replay.Value!.BatchId);
        Assert.Equal(2m, Assert.IsType<LotQuantity.Measured>(fixture.ParentLot.Quantity).Value);
    }

    [Fact]
    public async Task PrepareRejectsSameKeyReuseForADifferentSemanticOutputGraph()
    {
        var fixture = new Fixture();
        var command = fixture.Command();
        var first = await fixture.Workflow.PrepareAsync(command, CancellationToken.None);
        Assert.Null(first.Problem);

        var reuse = await fixture.Workflow.PrepareAsync(command with { Outputs = [fixture.Output(5m)] }, CancellationToken.None);

        Assert.Equal("idempotency_key_reused", reuse.Problem!.ErrorCode);
        Assert.Equal(2m, Assert.IsType<LotQuantity.Measured>(fixture.ParentLot.Quantity).Value);
    }

    private sealed class Fixture
    {
        private readonly Guid _ownerId = Guid.NewGuid();
        private readonly Product _parentProduct;
        public Fixture(bool includeParent = true)
        {
            ProductName.TryCreate("Dry beans", out var parentName);
            _parentProduct = Product.Create(_ownerId, parentName!, Now);
            ParentLot = InventoryLot.Create(_ownerId, _parentProduct.Id, new LotQuantity.Measured(5m, CanonicalUnit.Gram), Storage(), PackageState.Sealed, null, null, Now);
            Store = new TestStore(includeParent ? [new PreparationInputState(ParentLot, _parentProduct)] : []);
            Workflow = new PreparedComponentApplicationWorkflow(new CurrentUser(_ownerId), Store, new FixedTimeProvider(Now));
        }

        public InventoryLot ParentLot { get; }
        public TestStore Store { get; }
        public PreparedComponentApplicationWorkflow Workflow { get; }

        public PrepareComponentsCommand Command() => new(null, "Cooked beans", 5m, "Gram", null,
            [new PreparationInputCommand(ParentLot.Id, 3m, "Gram", InventoryVersionPrecondition.Valid(ParentLot.ConcurrencyToken))],
            [Output(2m), Output(3m)], Now, Guid.NewGuid(), "unit-test");

        public PreparationOutputCommand Output(decimal value) => new(value, "Gram", null, "Refrigerator", null, "Opened", new DateOnly(2026, 8, 7), "UserEntered", "High", "Promptly refrigerated");

        private static LotStorage Storage()
        {
            LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage);
            return storage!;
        }
    }

    private sealed class CurrentUser(Guid ownerId) : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(new InternalUser(ownerId, "https://issuer.test", "prepared-component", Now));
    }

    private sealed class TestStore(IReadOnlyList<PreparationInputState> inputs) : IInventoryPreparationStore
    {
        private PreparationIdempotencyRead? _idempotency;
        private PreparationIdempotencyRead? _delayedIdempotency;
        private int _remainingSuppressedLookups;
        public PreparationWrite? Saved { get; private set; }
        public Task<PreparationIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, Guid key, CancellationToken cancellationToken)
        {
            if (_delayedIdempotency is not null)
            {
                if (_remainingSuppressedLookups-- > 0)
                {
                    return Task.FromResult<PreparationIdempotencyRead?>(null);
                }

                return Task.FromResult<PreparationIdempotencyRead?>(_delayedIdempotency);
            }

            return Task.FromResult(_idempotency);
        }
        public void DelayStoredReplayUntilSecondLookup()
        {
            _delayedIdempotency = _idempotency;
            _idempotency = null;
            _remainingSuppressedLookups = 1;
        }
        public Task<IReadOnlyList<PreparationInputState>> LoadActiveInputsAsync(Guid ownerUserId, IReadOnlyList<Guid> lotIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PreparationInputState>>(inputs.Where(item => lotIds.Contains(item.Lot.Id)).ToList());
        public Task<Product?> FindActiveProductAsync(Guid ownerUserId, Guid productId, CancellationToken cancellationToken) => Task.FromResult<Product?>(null);
        public Task<PreparationWriteOutcome> SaveAsync(PreparationWrite write, CancellationToken cancellationToken)
        {
            Saved = write;
            _idempotency = new PreparationIdempotencyRead(write.Idempotency.RequestHash, write.Idempotency.Response, write.Idempotency.CreatedAt);
            return Task.FromResult(PreparationWriteOutcome.Saved);
        }
        public Task<PreparationBatchView?> GetAsync(Guid ownerUserId, Guid batchId, CancellationToken cancellationToken) => Task.FromResult<PreparationBatchView?>(null);
        public Task<InventoryLotProvenanceView?> GetLotProvenanceAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken) => Task.FromResult<InventoryLotProvenanceView?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
