using Cocinaris.Modules.Identity;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;

namespace Cocinaris.UnitTests;

/// <summary>Tests every Inventory application contract without web, database, or token services.</summary>
public sealed class InventoryApplicationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SevenUseCasesReturnStableTransportNeutralFailures()
    {
        var workflow = CreateWorkflow();

        var list = await new ListInventoryLotsHandler(workflow).ListAsync(new ListInventoryLotsQuery(101, null, null, null, null), CancellationToken.None);
        var get = await new GetInventoryLotHandler(workflow).GetAsync(Guid.NewGuid(), CancellationToken.None);
        var create = await new CreateInventoryLotHandler(workflow).CreateAsync(new CreateInventoryLotCommand("Tomato", 1m, "Gram", null, "Pantry", null, null, null, null, null, "test"), CancellationToken.None);
        var update = await new UpdateInventoryLotHandler(workflow).UpdateAsync(new UpdateInventoryLotCommand(Guid.NewGuid(), null, "Other", null, null, null, null, InventoryVersionPrecondition.Valid(Guid.NewGuid()), "test"), CancellationToken.None);
        var adjust = await new AdjustInventoryLotHandler(workflow).AdjustAsync(new AdjustInventoryLotCommand(Guid.NewGuid(), "Consume", 1m, null, "meal", null, null, InventoryVersionPrecondition.Valid(Guid.NewGuid()), "test"), CancellationToken.None);
        var delete = await new DeleteInventoryLotHandler(workflow).DeleteAsync(new DeleteInventoryLotCommand(Guid.NewGuid(), InventoryVersionPrecondition.Missing, "test"), CancellationToken.None);
        var history = await new GetInventoryLotHistoryHandler(workflow).HistoryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("validation_failed", list.Problem!.ErrorCode);
        Assert.Equal("resource_not_found", get.Problem!.ErrorCode);
        Assert.Equal("validation_failed", create.Problem!.ErrorCode);
        // Ownership-scoped absence precedes metadata/precondition failures (nondisclosing 404).
        Assert.Equal("resource_not_found", update.Problem!.ErrorCode);
        Assert.Equal("validation_failed", adjust.Problem!.ErrorCode);
        Assert.Equal("resource_not_found", delete.Problem!.ErrorCode);
        Assert.Equal("resource_not_found", history.Problem!.ErrorCode);
    }

    [Fact]
    public async Task IncompleteIdempotencyReservationReturnsStableInProgressOutcome()
    {
        var incomplete = new InventoryIdempotencyRead(new string('A', 64), InventoryApplicationSuccess.Created, null, null);
        var workflow = CreateWorkflow(new TestWriteStore(incomplete));
        var result = await new CreateInventoryLotHandler(workflow).CreateAsync(
            new CreateInventoryLotCommand("Tomato", 1m, "Gram", null, "Pantry", null, null, null, null, Guid.NewGuid(), "test"),
            CancellationToken.None);

        Assert.Equal("idempotency_in_progress", result.Problem!.ErrorCode);
    }

    private static InventoryLotApplicationWorkflow CreateWorkflow(IInventoryLotWriteStore? writeStore = null) => new(new TestCurrentUser(), new TestReadStore(), writeStore ?? new TestWriteStore(), TimeProvider.System, new InventoryLotLifecycleUseCase());

    private sealed class TestCurrentUser : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(new InternalUser(Guid.NewGuid(), "https://test", "user", Now));
    }

    private sealed class TestReadStore : IInventoryLotReadStore
    {
        public Task<InventoryLotReadModel?> FindActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken) => Task.FromResult<InventoryLotReadModel?>(null);
        public Task<IReadOnlyList<InventoryHistoryReadModel>?> GetHistoryAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<InventoryHistoryReadModel>?>(null);
        public Task<InventoryLotReadPage> ListAsync(InventoryLotReadQuery query, CancellationToken cancellationToken) => Task.FromResult(new InventoryLotReadPage([], null));
    }

    private sealed class TestWriteStore(InventoryIdempotencyRead? idempotency = null) : IInventoryLotWriteStore
    {
        public Task<InventoryIdempotencyRead?> FindIdempotencyAsync(Guid ownerUserId, string scope, Guid key, CancellationToken cancellationToken) => Task.FromResult(idempotency);
        public Task<InventoryLotMutationState?> LoadActiveAsync(Guid ownerUserId, Guid lotId, CancellationToken cancellationToken) => Task.FromResult<InventoryLotMutationState?>(null);
        public Task<InventoryWriteOutcome> SaveCreatedAsync(InventoryLotCreationWrite write, CancellationToken cancellationToken) => Task.FromResult(InventoryWriteOutcome.Saved);
        public Task<InventoryWriteOutcome> SaveMutationAsync(InventoryLotMutationWrite write, CancellationToken cancellationToken) => Task.FromResult(InventoryWriteOutcome.Saved);
    }
}
