using KitchenFlow.Infrastructure.Persistence;
using KitchenFlow.Modules.Identity;
using KitchenFlow.Modules.Inventory.Application;
using KitchenFlow.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace KitchenFlow.IntegrationTests;

public sealed class PostgreSqlMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18.4")
        .WithDatabase("kitchenflow_test")
        .WithUsername("kitchenflow_test")
        .WithPassword("development-only-test-password")
        .Build();

    [Fact]
    public async Task MigrationsCreateRequiredSchemasAndTables()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);

        await context.Database.MigrateAsync();
        Assert.True(await context.Database.CanConnectAsync());
        var constraints = await context.Database.SqlQueryRaw<string>(
            """SELECT conname AS "Value" FROM pg_constraint WHERE connamespace IN ('inventory'::regnamespace, 'platform'::regnamespace)""").ToListAsync();
        string[] requiredConstraints =
        [
            "ck_lots_quantity_mode",
            "ck_lots_measured_value",
            "ck_lots_measured_unit",
            "ck_lots_availability_state",
            "ck_lots_storage",
            "ck_lots_package_state",
            "ck_lots_expiration_provenance",
            "ck_transactions_type",
            "ck_transactions_previous_quantity",
            "ck_transactions_resulting_quantity",
            "ck_transactions_reason_code",
            "ck_preparation_batches_source",
            "ck_preparation_batches_prepared_at",
            "ck_preparation_inputs_quantity",
            "ck_prepared_lots_lifecycle",
            "ck_prepared_lots_evidence",
            "ck_idempotency_completion",
            "ck_idempotency_status_code",
            "FK_lots_products_ProductId_OwnerUserId",
            "FK_transactions_lots_LotId_OwnerUserId",
            "FK_preparation_inputs_lots_InputLotId_OwnerUserId",
            "FK_preparation_outputs_lots_OutputLotId_OwnerUserId",
            "FK_prepared_lots_lots_LotId_OwnerUserId",
            "FK_audit_events_users_ActorUserId",
            "FK_idempotency_records_users_OwnerUserId"
        ];
        Assert.All(requiredConstraints, constraint => Assert.Contains(constraint, constraints));
        var triggers = await context.Database.SqlQueryRaw<string>(
            """SELECT tgname AS "Value" FROM pg_trigger WHERE NOT tgisinternal""").ToListAsync();
        Assert.Contains("transactions_are_append_only", triggers);
        Assert.Contains("audit_events_are_append_only", triggers);
        Assert.Contains("preparation_batches_are_append_only", triggers);
        Assert.Contains("preparation_inputs_are_append_only", triggers);
        Assert.Contains("preparation_outputs_are_append_only", triggers);
        Assert.Contains("prepared_lots_are_append_only", triggers);
        Assert.Contains("recipe_revisions_are_append_only", triggers);
    }

    [Fact]
    public async Task EveryPriorMigrationUpgradesRepresentativeInventoryDataToLatest()
    {
        string[] priorMigrations =
        [
            "20260729013459_InitialInventorySlice",
            "20260729123210_EnforceInventoryReferentialIntegrity",
            "20260729193936_EnforceInventoryHistoryIntegrity",
            "20260731021725_EnforceAppendOnlyHistory",
            "20260731024742_TightenExpirationProvenance",
            "20260731120209_AddInventoryLotConcurrencyToken"
        ];
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        foreach (var priorMigration in priorMigrations)
        {
            await using var context = new ApplicationDbContext(options);
            await context.Database.EnsureDeletedAsync();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(priorMigration);
            var ownerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var lotId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            // Seed through the historical schema rather than the current EF model so this test
            // remains capable of detecting additive-column upgrade defects.
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO identity.users ("Id", "Issuer", "Subject", "CreatedAt") VALUES ({ownerId}, {$"https://issuer.test/{ownerId}"}, {$"subject-{ownerId}"}, {now})""");
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO inventory.products ("Id", "OwnerUserId", "DisplayName", "NormalizedSearchName", "CreatedAt", "UpdatedAt", "IsDeleted") VALUES ({productId}, {ownerId}, {"Upgrade product"}, {"UPGRADE PRODUCT"}, {now}, {now}, {false})""");
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO inventory.lots ("Id", "OwnerUserId", "ProductId", "MeasuredValue", "MeasuredUnit", "StorageLocation", "Version", "CreatedAt", "UpdatedAt") VALUES ({lotId}, {ownerId}, {productId}, {10.125m}, {"Gram"}, {"Pantry"}, {1L}, {now}, {now})""");
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO inventory.transactions ("Id", "OwnerUserId", "LotId", "Type", "ResultingMeasuredValue", "ResultingMeasuredUnit", "OccurredAt") VALUES ({Guid.NewGuid()}, {ownerId}, {lotId}, {"Initial"}, {10.125m}, {"Gram"}, {now})""");
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO platform.audit_events ("Id", "ActorUserId", "EventName", "TargetType", "TargetId", "CorrelationId", "MetadataJson", "OccurredAt") VALUES ({Guid.NewGuid()}, {ownerId}, {"inventory.lot.created"}, {"inventory_lot"}, {lotId}, {"upgrade-test"}, CAST({"{}"} AS jsonb), {now})""");
            await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO platform.idempotency_records ("Id", "OwnerUserId", "Scope", "Key", "RequestHash", "StatusCode", "ResponseBody", "ETag", "CreatedAt", "CompletedAt") VALUES ({Guid.NewGuid()}, {ownerId}, {"inventory.lots.create"}, {Guid.NewGuid()}, {new string('A', 64)}, {201}, CAST({"{}"} AS jsonb), {"1"}, {now}, {now})""");

            await migrator.MigrateAsync();

            Assert.Equal(10.125m, await context.Lots.AsNoTracking().Where(lot => lot.Id == lotId).Select(lot => lot.MeasuredValue).SingleAsync());
            Assert.NotEqual(Guid.Empty, await context.Lots.AsNoTracking().Where(lot => lot.Id == lotId).Select(lot => lot.ConcurrencyToken).SingleAsync());
            Assert.True(await context.Transactions.AsNoTracking().AnyAsync(transaction => transaction.LotId == lotId));
            Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(audit => audit.TargetId == lotId));
            Assert.True(await context.IdempotencyRecords.AsNoTracking().AnyAsync(record => record.OwnerUserId == ownerId));
        }
    }

    [Fact]
    public async Task ConcurrentIdentityProvisioningReturnsOneInternalUser()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var migration = new ApplicationDbContext(options))
        {
            await migration.Database.MigrateAsync();
        }

        var subject = new OidcSubject("https://identity.test/realms/kitchenflow", $"concurrent-{Guid.NewGuid():N}");
        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var users = await Task.WhenAll(
            new PostgreSqlInternalUserStore(firstContext).FindOrCreateAsync(subject, DateTimeOffset.UtcNow, CancellationToken.None),
            new PostgreSqlInternalUserStore(secondContext).FindOrCreateAsync(subject, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Equal(users[0].Id, users[1].Id);
        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(1, await verification.Users.CountAsync(user => user.Issuer == subject.Issuer && user.Subject == subject.Subject));
    }

    [Fact]
    public async Task IdentityProvisioningDoesNotMisclassifyUnrelatedPersistenceFailure()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var store = new PostgreSqlInternalUserStore(context);
        var subject = new OidcSubject($"https://identity.test/{new string('i', 501)}", Guid.NewGuid().ToString("N"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => store.FindOrCreateAsync(subject, DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, ((PostgresException)exception.InnerException!).SqlState);
    }

    [Fact]
    public async Task MigrationsEnforceInventoryHistoryAndIdempotencyIntegrity()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        context.Products.Add(new ProductRecord
        {
            Id = productId,
            OwnerUserId = ownerId,
            DisplayName = "Migration test product",
            NormalizedSearchName = "MIGRATION TEST PRODUCT",
            CreatedAt = now,
            UpdatedAt = now
        });
        context.Lots.Add(new LotRecord
        {
            Id = lotId,
            OwnerUserId = ownerId,
            ProductId = productId,
            MeasuredValue = 10m,
            MeasuredUnit = "Gram",
            StorageLocation = "Pantry",
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        context.Transactions.Add(new TransactionRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            LotId = lotId,
            Type = "Unsupported",
            ResultingMeasuredValue = 10m,
            ResultingMeasuredUnit = "Gram",
            OccurredAt = now
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Scope = "inventory.lots.create",
            Key = Guid.NewGuid(),
            RequestHash = new string('A', 64),
            StatusCode = 201,
            ResponseBody = null,
            ETag = "\"opaque\"",
            CreatedAt = now,
            CompletedAt = now
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task PostgreSqlEnforcesEveryLotModeMetadataAndOwnerConstraint()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.AddRange(
            new InternalUser(firstOwner, $"https://issuer.test/{firstOwner}", $"subject-{firstOwner}", now),
            new InternalUser(secondOwner, $"https://issuer.test/{secondOwner}", $"subject-{secondOwner}", now));
        context.Products.AddRange(
            new ProductRecord { Id = firstProduct, OwnerUserId = firstOwner, DisplayName = "First product", NormalizedSearchName = "FIRST PRODUCT", CreatedAt = now, UpdatedAt = now },
            new ProductRecord { Id = secondProduct, OwnerUserId = secondOwner, DisplayName = "Second product", NormalizedSearchName = "SECOND PRODUCT", CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync();

        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, MeasuredValue = 1m, MeasuredUnit = "Gram", AvailabilityState = "Low", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_quantity_mode");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, MeasuredValue = 1m, MeasuredUnit = "Pound", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_measured_unit");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, AvailabilityState = "Maybe", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_availability_state");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, AvailabilityState = "Low", StorageLocation = "Other", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_storage");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, AvailabilityState = "Low", StorageLocation = "Pantry", CustomLocation = "Shelf", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_storage");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, AvailabilityState = "Low", StorageLocation = "Pantry", PackageState = "Damaged", Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_package_state");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = firstOwner, ProductId = firstProduct, AvailabilityState = "Low", StorageLocation = "Pantry", PrintedExpirationDate = new DateOnly(2026, 12, 31), Version = 1, CreatedAt = now, UpdatedAt = now }, "ck_lots_expiration_provenance");
        await AssertRejectedAsync(new LotRecord { Id = Guid.NewGuid(), OwnerUserId = secondOwner, ProductId = firstProduct, AvailabilityState = "Low", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now }, null);

        var validLotId = Guid.NewGuid();
        context.Lots.Add(new LotRecord { Id = validLotId, OwnerUserId = firstOwner, ProductId = firstProduct, MeasuredValue = LotQuantity.MaximumMeasuredValue, MeasuredUnit = "Gram", StorageLocation = "Other", CustomLocation = "Top shelf", PackageState = "Opened", PrintedExpirationDate = new DateOnly(2026, 12, 31), ExpirationProvenance = "UserEntered", Version = 1, CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync();
        Assert.Equal(LotQuantity.MaximumMeasuredValue, await context.Lots.AsNoTracking().Where(lot => lot.Id == validLotId).Select(lot => lot.MeasuredValue).SingleAsync());

        async Task AssertRejectedAsync(LotRecord lot, string? expectedConstraint)
        {
            context.Lots.Add(lot);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            if (expectedConstraint is not null)
            {
                Assert.Equal(expectedConstraint, postgres.ConstraintName);
            }

            context.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task InventoryWriteStoreRethrowsUnrelatedForeignKeyFailureAndRollsBackWholeCreate()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var missingOwnerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.True(ProductName.TryCreate("Atomic test product", out var productName));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var product = Product.Create(missingOwnerId, productName!, now);
        var lot = InventoryLot.Create(missingOwnerId, product.Id, new LotQuantity.Measured(10m, CanonicalUnit.Gram), storage!, null, null, null, now);
        var transaction = InventoryTransaction.Create(lot.Id, missingOwnerId, InventoryTransactionType.Initial, null, lot.Quantity, null, null, Guid.NewGuid(), now);
        var response = new InventoryLotView(lot.Id, product.Id, product.DisplayName, new InventoryQuantity(10m, "Gram", null), "Pantry", null, null, null, null, lot.ConcurrencyToken, now, now);
        var idempotency = new InventoryIdempotencyWrite(Guid.NewGuid(), "inventory.lots.create", new string('A', 64), InventoryApplicationSuccess.Created, response, now);
        var store = new PostgreSqlInventoryLotWriteStore(context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.SaveCreatedAsync(new InventoryLotCreationWrite(missingOwnerId, product, lot, transaction, "atomicity-test", idempotency), CancellationToken.None));

        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
        Assert.Empty(context.ChangeTracker.Entries());

        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(0, await verification.Products.CountAsync());
        Assert.Equal(0, await verification.Lots.CountAsync());
        Assert.Equal(0, await verification.Transactions.CountAsync());
        Assert.Equal(0, await verification.AuditEvents.CountAsync());
        Assert.Equal(0, await verification.IdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task InventoryWriteStoreRethrowsUnrelatedMutationFailureAndRollsBackEverySideEffect()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        context.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Atomic mutation product", NormalizedSearchName = "ATOMIC MUTATION PRODUCT", CreatedAt = now, UpdatedAt = now });
        context.Lots.Add(new LotRecord { Id = lotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 10m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.True(ProductName.TryCreate("Atomic mutation product", out var productName));
        Assert.True(LotStorage.TryCreate(StorageLocation.Pantry, null, out var storage));
        var product = Product.Restore(productId, ownerId, productName!, now, now, false);
        var concurrencyToken = Guid.NewGuid();
        var lot = InventoryLot.Restore(lotId, ownerId, productId, new LotQuantity.Measured(10m, CanonicalUnit.Gram), storage!, null, null, null, 1, concurrencyToken, now, now, null);
        var transaction = lot.AdjustMeasured(InventoryTransactionType.Consume, 1m, "test", null, Guid.NewGuid(), now.AddMinutes(1));
        var invalidTransaction = InventoryTransaction.Create(lotId, Guid.NewGuid(), transaction.Type, transaction.PreviousQuantity, transaction.ResultingQuantity, transaction.ReasonCode, transaction.Note, transaction.IdempotencyKey, transaction.OccurredAt);
        var response = new InventoryLotView(lot.Id, product.Id, product.DisplayName, new InventoryQuantity(9m, "Gram", null), "Pantry", null, null, null, null, lot.ConcurrencyToken, lot.CreatedAt, lot.UpdatedAt);
        var idempotency = new InventoryIdempotencyWrite(transaction.IdempotencyKey!.Value, "inventory.lots.adjust", new string('B', 64), InventoryApplicationSuccess.Succeeded, response, now.AddMinutes(1));
        var store = new PostgreSqlInventoryLotWriteStore(context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            store.SaveMutationAsync(new InventoryLotMutationWrite(ownerId, lot, product, 1, invalidTransaction, "inventory.lot.adjusted", "{}", "atomicity-test", idempotency), CancellationToken.None));

        Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
        Assert.Empty(context.ChangeTracker.Entries());

        await using var verification = new ApplicationDbContext(options);
        var persistedLot = await verification.Lots.AsNoTracking().SingleAsync();
        Assert.Equal(10m, persistedLot.MeasuredValue);
        Assert.Equal(1, persistedLot.Version);
        Assert.Equal(0, await verification.Transactions.CountAsync());
        Assert.Equal(0, await verification.AuditEvents.CountAsync());
        Assert.Equal(0, await verification.IdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task DatabaseRejectsMutationAndDeletionOfImmutableHistory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        context.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Immutable product", NormalizedSearchName = "IMMUTABLE PRODUCT", CreatedAt = now, UpdatedAt = now });
        context.Lots.Add(new LotRecord { Id = lotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 10m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now });
        context.Transactions.Add(new TransactionRecord { Id = transactionId, OwnerUserId = ownerId, LotId = lotId, Type = "Initial", ResultingMeasuredValue = 10m, ResultingMeasuredUnit = "Gram", OccurredAt = now });
        context.AuditEvents.Add(new AuditEventRecord { Id = auditId, ActorUserId = ownerId, EventName = "inventory.lot.created", TargetType = "inventory_lot", TargetId = lotId, CorrelationId = "immutability-test", MetadataJson = "{}", OccurredAt = now });
        await context.SaveChangesAsync();

        var transactionUpdate = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory.transactions SET \"ReasonCode\" = 'changed' WHERE \"Id\" = {transactionId}"));
        var auditDelete = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM platform.audit_events WHERE \"Id\" = {auditId}"));

        Assert.Equal("55000", transactionUpdate.SqlState);
        Assert.Equal("55000", auditDelete.SqlState);
        Assert.Equal("Initial", await context.Transactions.AsNoTracking().Where(item => item.Id == transactionId).Select(item => item.Type).SingleAsync());
        Assert.True(await context.AuditEvents.AsNoTracking().AnyAsync(item => item.Id == auditId));
    }

    [Fact]
    public async Task PreparationProvenanceIsOwnerConsistentAndAppendOnly()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var parentLotId = Guid.NewGuid();
        var secondParentLotId = Guid.NewGuid();
        var outputLotId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.AddRange(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now), new InternalUser(otherOwnerId, $"https://issuer.test/{otherOwnerId}", $"subject-{otherOwnerId}", now));
        context.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Prepared stock", NormalizedSearchName = "PREPARED STOCK", CreatedAt = now, UpdatedAt = now });
        context.Lots.AddRange(
            new LotRecord { Id = parentLotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 5m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now },
            new LotRecord { Id = secondParentLotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 5m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, CreatedAt = now, UpdatedAt = now },
            new LotRecord { Id = outputLotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 3m, MeasuredUnit = "Gram", StorageLocation = "Refrigerator", Version = 1, CreatedAt = now, UpdatedAt = now });
        context.PreparationBatches.Add(new PreparationBatchRecord { Id = batchId, OwnerUserId = ownerId, OutputProductId = productId, DeclaredYieldMeasuredValue = 3m, DeclaredYieldMeasuredUnit = "Gram", SourceType = "ManualPreparation", PreparedAt = now, CreatedAt = now });
        context.PreparationInputs.Add(new PreparationInputRecord { BatchId = batchId, OwnerUserId = ownerId, InputLotId = parentLotId, ConsumedValue = 2m, ConsumedUnit = "Gram" });
        context.PreparationOutputs.Add(new PreparationOutputRecord { BatchId = batchId, OwnerUserId = ownerId, OutputLotId = outputLotId });
        context.PreparedLots.Add(new PreparedLotRecord { LotId = outputLotId, OwnerUserId = ownerId, BatchId = batchId, LifecycleState = "Prepared", PreparedAt = now, ShelfLifeSource = "Unknown", ShelfLifeConfidence = "Unknown" });
        await context.SaveChangesAsync();

        var inputMutation = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory.preparation_inputs SET \"ConsumedValue\" = 1 WHERE \"BatchId\" = {batchId}"));
        var outputDeletion = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM inventory.preparation_outputs WHERE \"BatchId\" = {batchId}"));
        Assert.Equal("55000", inputMutation.SqlState);
        Assert.Equal("55000", outputDeletion.SqlState);

        var ownerViolation = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO inventory.preparation_inputs (\"BatchId\", \"InputLotId\", \"OwnerUserId\", \"ConsumedValue\", \"ConsumedUnit\") VALUES ({batchId}, {secondParentLotId}, {otherOwnerId}, {1m}, {"Gram"})"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ownerViolation.SqlState);
    }

    [Fact]
    public async Task PreparationDeclaredYieldConstraintFailsClosedForEveryInvalidModeAndAcceptsValidModes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        context.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Yield constraint product", NormalizedSearchName = "YIELD CONSTRAINT PRODUCT", CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync();

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (var invalid in new (decimal? Value, string? Unit, string? Availability)[]
        {
            (null, null, null), (1m, null, null), (null, "Gram", null), (1m, "Gram", "Available"),
            (0m, "Gram", null), (-1m, "Gram", null), (1m, "Cup", null), (null, null, "Unknown")
        })
        {
            await AssertDeclaredYieldInsertAsync(connection, ownerId, productId, invalid.Value, invalid.Unit, invalid.Availability, shouldSucceed: false);
        }

        foreach (var valid in new (decimal? Value, string? Unit, string? Availability)[]
        {
            (1m, "Gram", null), (1m, "Milliliter", null), (1m, "Unit", null),
            (null, null, "Available"), (null, null, "Low"), (null, null, "Unavailable")
        })
        {
            await AssertDeclaredYieldInsertAsync(connection, ownerId, productId, valid.Value, valid.Unit, valid.Availability, shouldSucceed: true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(49, false)]
    [InlineData(50, false)]
    [InlineData(51, true)]
    [InlineData(55, true)]
    public async Task PreparationProvenanceReportsItsIndependentBoundAndTruncationState(int relatedBatchCount, bool expectedTruncated)
    {
        var seed = await SeedProvenanceGraphAsync(relatedBatchCount);
        await using var context = CreateContext();
        var store = new PostgreSqlInventoryPreparationStore(context);

        var parent = await store.GetLotProvenanceAsync(seed.OwnerId, seed.ParentLotId, CancellationToken.None);
        var output = await store.GetLotProvenanceAsync(seed.OwnerId, seed.FirstOutputLotId, CancellationToken.None);

        Assert.NotNull(parent);
        Assert.Equal(Math.Min(relatedBatchCount, 50), parent!.ConsumedBy.Count);
        Assert.Equal(expectedTruncated, parent.ConsumedByTruncated);
        Assert.Empty(parent.ProducedBy);
        Assert.False(parent.ProducedByTruncated);
        Assert.Equal(parent.ConsumedBy.OrderByDescending(item => item.PreparedAt).ThenByDescending(item => item.BatchId), parent.ConsumedBy);
        if (relatedBatchCount == 0)
        {
            Assert.Null(output);
            return;
        }

        Assert.NotNull(output);
        Assert.Empty(output!.ConsumedBy);
        Assert.False(output.ConsumedByTruncated);
        Assert.Single(output.ProducedBy);
        Assert.False(output.ProducedByTruncated);
    }

    [Fact]
    public async Task UniqueEquipmentStableCodeMigrationFailsClosedWhenDuplicatesExist()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260731185224_InitialProfilesSlice");

        var ownerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO identity.users ("Id", "Issuer", "Subject", "CreatedAt") VALUES ({ownerId}, {$"https://issuer.test/{ownerId}"}, {$"subject-{ownerId}"}, {now})""");
        await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO profiles.user_profiles ("OwnerUserId", "DisplayNamePresence", "DefaultAdultCountPresence", "DefaultChildCountPresence", "DefaultServingCountPresence", "LanguagePresence", "RegionPresence", "CurrencyPresence", "MeasurementSystemPresence", "TimeZonePresence", "PlanningCadencePresence", "ShoppingCadencePresence", "OverallSkillPresence", "ConfidencePresence", "PreferredInstructionDetailPresence", "OrdinaryPrepMinutesPresence", "ExceptionalPrepMinutesPresence", "EffortTolerancePresence", "CleanupTolerancePresence", "RepeatMealPreferencePresence", "ReheatingPreferencePresence", "LeftoverPreferencePresence", "FreezingPreferencePresence", "Version", "ConcurrencyToken", "CreatedAt", "UpdatedAt") VALUES ({ownerId}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {"Absent"}, {1L}, {Guid.NewGuid()}, {now}, {now})""");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO profiles.equipment_entries ("Id", "OwnerUserId", "StableCode", "IsRemoved", "SortOrder", "CreatedAt", "UpdatedAt") VALUES ({firstId}, {ownerId}, {"oven"}, {false}, {0}, {now}, {now})""");
        await context.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO profiles.equipment_entries ("Id", "OwnerUserId", "StableCode", "IsRemoved", "SortOrder", "CreatedAt", "UpdatedAt") VALUES ({secondId}, {ownerId}, {"oven"}, {true}, {1}, {now}, {now})""");

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());
        Assert.Contains("unique", exception.ToString(), StringComparison.OrdinalIgnoreCase);

        var remaining = await context.Database.SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM profiles.equipment_entries""").ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Contains(firstId, remaining);
        Assert.Contains(secondId, remaining);

        await context.Database.ExecuteSqlInterpolatedAsync($"""DELETE FROM profiles.equipment_entries WHERE "Id" = {secondId}""");
        await migrator.MigrateAsync();

        var indexes = await context.Database.SqlQueryRaw<string>("""SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = 'profiles' AND tablename = 'equipment_entries' AND indexname = 'IX_equipment_entries_OwnerUserId_StableCode'""").ToListAsync();
        Assert.Contains(indexes, definition => definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("""SELECT COUNT(*)::int AS "Value" FROM profiles.equipment_entries""").SingleAsync());
    }

    [Fact]
    public async Task PreparationStoreRollsBackParentConsumptionWhenTheOutputGraphCannotBePersisted()
    {
        var seed = await SeedPreparationParentAsync();
        var now = DateTimeOffset.UtcNow;
        await using var context = CreateContext();
        var store = new PostgreSqlInventoryPreparationStore(context);
        var parent = RestorePreparationLot(seed.LotId, seed.OwnerId, seed.ProductId, new LotQuantity.Measured(10m, CanonicalUnit.Gram), StorageLocation.Pantry, seed.ConcurrencyToken, now);
        var inputTransaction = parent.AdjustMeasured(InventoryTransactionType.PreparationInputConsumed, 4m, "preparation", null, Guid.NewGuid(), now);
        ProductName.TryCreate("Unpersisted output", out var outputName);
        var missingProduct = Product.Create(seed.OwnerId, outputName!, now);
        var outputLot = InventoryLot.Create(seed.OwnerId, missingProduct.Id, new LotQuantity.Measured(4m, CanonicalUnit.Gram), PreparationStorage(StorageLocation.Refrigerator), PackageState.Opened, null, null, now);
        var outputTransaction = InventoryTransaction.Create(outputLot.Id, seed.OwnerId, InventoryTransactionType.PreparationOutputCreated, null, outputLot.Quantity, "preparation", null, null, now);
        var batch = PreparationBatch.Create(seed.OwnerId, missingProduct.Id, now, now);
        var response = new PreparationBatchView(batch.Id, "ManualPreparation", missingProduct.Id, missingProduct.DisplayName, new InventoryQuantity(4m, "Gram", null), now, [], [], now);
        var write = new PreparationWrite(seed.OwnerId, batch, missingProduct, null, new InventoryQuantity(4m, "Gram", null), [new PreparationInputWrite(parent, seed.Product, seed.Version, 4m, inputTransaction)], [new PreparationOutputWrite(outputLot, outputTransaction, new PreparedShelfLifeEvidence(null, PreparedShelfLifeEvidenceSource.Unknown, ShelfLifeEvidenceConfidence.Unknown, null))], new PreparationIdempotencyWrite(Guid.NewGuid(), new string('A', 64), response, now), "preparation-rollback-test");

        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(write, CancellationToken.None));

        await using var verification = CreateContext();
        var parentRecord = await verification.Lots.AsNoTracking().SingleAsync(item => item.Id == seed.LotId);
        Assert.Equal(10m, parentRecord.MeasuredValue);
        Assert.Equal(seed.Version, parentRecord.Version);
        Assert.Empty(await verification.PreparationBatches.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.PreparedLots.AsNoTracking().ToListAsync());
        Assert.Empty(await verification.IdempotencyRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PreparationStoreConcurrentDifferentKeysApplyOnlyOneParentConsumption()
    {
        var seed = await SeedPreparationParentAsync();
        var preparedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var results = await Task.WhenAll(
            CreatePreparationWorkflow(firstContext, seed.OwnerId).PrepareAsync(CreatePreparationCommand(seed, Guid.NewGuid(), preparedAt), CancellationToken.None),
            CreatePreparationWorkflow(secondContext, seed.OwnerId).PrepareAsync(CreatePreparationCommand(seed, Guid.NewGuid(), preparedAt), CancellationToken.None));

        Assert.Single(results, result => result.Problem is null);
        Assert.Single(results, result => result.Problem?.ErrorCode == "precondition_failed");
        await using var verification = CreateContext();
        var parent = await verification.Lots.AsNoTracking().SingleAsync(item => item.Id == seed.LotId);
        Assert.Equal(7m, parent.MeasuredValue);
        Assert.Equal(seed.Version + 1, parent.Version);
        Assert.Equal(1, await verification.PreparationBatches.CountAsync());
        Assert.Equal(1, await verification.PreparedLots.CountAsync());
    }

    [Fact]
    public async Task PreparationStoreConcurrentSameKeyReplaysTheOneAuthoritativePreparation()
    {
        var seed = await SeedPreparationParentAsync();
        var key = Guid.NewGuid();
        var preparedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var results = await Task.WhenAll(
            CreatePreparationWorkflow(firstContext, seed.OwnerId).PrepareAsync(CreatePreparationCommand(seed, key, preparedAt), CancellationToken.None),
            CreatePreparationWorkflow(secondContext, seed.OwnerId).PrepareAsync(CreatePreparationCommand(seed, key, preparedAt), CancellationToken.None));

        Assert.All(results, result => Assert.Null(result.Problem));
        Assert.Single(results.Select(result => result.Value!.BatchId).Distinct());
        Assert.Contains(results, result => result.Idempotency == InventoryIdempotencyDisposition.Replayed);
        await using var verification = CreateContext();
        Assert.Equal(7m, await verification.Lots.AsNoTracking().Where(item => item.Id == seed.LotId).Select(item => item.MeasuredValue).SingleAsync());
        Assert.Equal(1, await verification.PreparationBatches.CountAsync());
        Assert.Equal(1, await verification.IdempotencyRecords.CountAsync(item => item.Key == key));
    }

    private ApplicationDbContext CreateContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task<PreparationParentSeed> SeedPreparationParentAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var token = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var product = new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Preparation parent", NormalizedSearchName = "PREPARATION PARENT", CreatedAt = now, UpdatedAt = now };
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"subject-{ownerId}", now));
        context.Products.Add(product);
        context.Lots.Add(new LotRecord { Id = lotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 10m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, ConcurrencyToken = token, CreatedAt = now, UpdatedAt = now });
        await context.SaveChangesAsync();
        ProductName.TryCreate(product.DisplayName, out var name);
        return new PreparationParentSeed(ownerId, productId, lotId, token, 1, Product.Restore(productId, ownerId, name!, now, now, false));
    }

    private async Task<ProvenanceGraphSeed> SeedProvenanceGraphAsync(int relatedBatchCount)
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        var ownerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var parentLotId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        context.Users.Add(new InternalUser(ownerId, $"https://issuer.test/{ownerId}", $"provenance-{ownerId}", now));
        context.Products.Add(new ProductRecord { Id = productId, OwnerUserId = ownerId, DisplayName = "Provenance product", NormalizedSearchName = "PROVENANCE PRODUCT", CreatedAt = now, UpdatedAt = now });
        context.Lots.Add(new LotRecord { Id = parentLotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 1000m, MeasuredUnit = "Gram", StorageLocation = "Pantry", Version = 1, ConcurrencyToken = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now });
        var firstOutputLotId = Guid.Empty;
        for (var index = 0; index < relatedBatchCount; index++)
        {
            var batchId = Guid.NewGuid();
            var outputLotId = Guid.NewGuid();
            firstOutputLotId = index == 0 ? outputLotId : firstOutputLotId;
            var preparedAt = now.AddMinutes(-index - 1);
            context.PreparationBatches.Add(new PreparationBatchRecord { Id = batchId, OwnerUserId = ownerId, OutputProductId = productId, DeclaredYieldMeasuredValue = 1m, DeclaredYieldMeasuredUnit = "Gram", SourceType = "ManualPreparation", PreparedAt = preparedAt, CreatedAt = now });
            context.PreparationInputs.Add(new PreparationInputRecord { BatchId = batchId, OwnerUserId = ownerId, InputLotId = parentLotId, ConsumedValue = 1m, ConsumedUnit = "Gram" });
            context.Lots.Add(new LotRecord { Id = outputLotId, OwnerUserId = ownerId, ProductId = productId, MeasuredValue = 1m, MeasuredUnit = "Gram", StorageLocation = "Refrigerator", Version = 1, ConcurrencyToken = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now });
            context.PreparationOutputs.Add(new PreparationOutputRecord { BatchId = batchId, OwnerUserId = ownerId, OutputLotId = outputLotId });
            context.PreparedLots.Add(new PreparedLotRecord { LotId = outputLotId, OwnerUserId = ownerId, BatchId = batchId, LifecycleState = "Prepared", PreparedAt = preparedAt, ShelfLifeSource = "Unknown", ShelfLifeConfidence = "Unknown" });
        }

        await context.SaveChangesAsync();
        return new ProvenanceGraphSeed(ownerId, parentLotId, firstOutputLotId);
    }

    private static PreparedComponentApplicationWorkflow CreatePreparationWorkflow(ApplicationDbContext context, Guid ownerId) => new(new PreparationTestCurrentUser(ownerId), new PostgreSqlInventoryPreparationStore(context), TimeProvider.System);

    private static PrepareComponentsCommand CreatePreparationCommand(PreparationParentSeed parent, Guid key, DateTimeOffset preparedAt) => new(null, "Prepared component", 3m, "Gram", null, [new PreparationInputCommand(parent.LotId, 3m, "Gram", InventoryVersionPrecondition.Valid(parent.ConcurrencyToken))], [new PreparationOutputCommand(3m, "Gram", null, "Refrigerator", null, "Opened", null, "Unknown", "Unknown", null)], preparedAt, key, "preparation-concurrency-test");

    private static InventoryLot RestorePreparationLot(Guid lotId, Guid ownerId, Guid productId, LotQuantity quantity, StorageLocation storageLocation, Guid token, DateTimeOffset now) => InventoryLot.Restore(lotId, ownerId, productId, quantity, PreparationStorage(storageLocation), PackageState.Sealed, null, null, 1, token, now, now, null);

    private static LotStorage PreparationStorage(StorageLocation location)
    {
        LotStorage.TryCreate(location, null, out var storage);
        return storage!;
    }

    private static async Task AssertDeclaredYieldInsertAsync(NpgsqlConnection connection, Guid ownerId, Guid productId, decimal? value, string? unit, string? availability, bool shouldSucceed)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO inventory.preparation_batches ("Id", "OwnerUserId", "OutputProductId", "DeclaredYieldMeasuredValue", "DeclaredYieldMeasuredUnit", "DeclaredYieldAvailabilityState", "SourceType", "PreparedAt", "CreatedAt")
            VALUES (@id, @owner, @product, @value, @unit, @availability, 'ManualPreparation', NOW(), NOW())
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("product", productId);
        command.Parameters.AddWithValue("value", value.HasValue ? value.Value : DBNull.Value);
        command.Parameters.AddWithValue("unit", unit ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("availability", availability ?? (object)DBNull.Value);

        if (shouldSucceed)
        {
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            return;
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    private sealed record PreparationParentSeed(Guid OwnerId, Guid ProductId, Guid LotId, Guid ConcurrencyToken, long Version, Product Product);

    private sealed record ProvenanceGraphSeed(Guid OwnerId, Guid ParentLotId, Guid FirstOutputLotId);

    private sealed class PreparationTestCurrentUser(Guid ownerId) : ICurrentUserAccessor
    {
        public Task<InternalUser> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(new InternalUser(ownerId, "https://issuer.test", "preparation-store-test", DateTimeOffset.UtcNow));
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}
