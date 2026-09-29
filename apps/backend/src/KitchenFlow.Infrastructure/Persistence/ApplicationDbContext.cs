using KitchenFlow.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace KitchenFlow.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core persistence boundary for KitchenFlow identity, inventory, profiles,
/// recipes, AI usage, audit, and idempotency records. Composite owner keys prevent cross-user
/// links at the database boundary.
/// </summary>
/// <param name="options">Configured PostgreSQL context options.</param>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    /// <summary>Gets the authoritative internal OIDC identity mappings.</summary>
    public DbSet<InternalUser> Users => Set<InternalUser>();

    /// <summary>Gets the user-owned product persistence records.</summary>
    public DbSet<ProductRecord> Products => Set<ProductRecord>();

    /// <summary>Gets the user-owned inventory lot persistence records.</summary>
    public DbSet<LotRecord> Lots => Set<LotRecord>();

    /// <summary>Gets immutable inventory lifecycle transaction records.</summary>
    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    /// <summary>Gets immutable owner-scoped preparation batch records.</summary>
    public DbSet<PreparationBatchRecord> PreparationBatches => Set<PreparationBatchRecord>();

    /// <summary>Gets immutable preparation input provenance records.</summary>
    public DbSet<PreparationInputRecord> PreparationInputs => Set<PreparationInputRecord>();

    /// <summary>Gets immutable preparation output provenance records.</summary>
    public DbSet<PreparationOutputRecord> PreparationOutputs => Set<PreparationOutputRecord>();

    /// <summary>Gets prepared-component metadata attached to authoritative output lots.</summary>
    public DbSet<PreparedLotRecord> PreparedLots => Set<PreparedLotRecord>();

    /// <summary>Gets immutable audit event records.</summary>
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();

    /// <summary>Gets PostgreSQL-backed idempotency records.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Gets owner profile persistence records.</summary>
    public DbSet<UserProfileRecord> UserProfiles => Set<UserProfileRecord>();

    /// <summary>Gets preference and restriction persistence records.</summary>
    public DbSet<PreferenceEntryRecord> PreferenceEntries => Set<PreferenceEntryRecord>();

    /// <summary>Gets equipment persistence records.</summary>
    public DbSet<EquipmentEntryRecord> EquipmentEntries => Set<EquipmentEntryRecord>();

    /// <summary>Gets ordered profile code-list persistence records.</summary>
    public DbSet<ProfileOrderedCodeEntryRecord> ProfileOrderedCodeEntries => Set<ProfileOrderedCodeEntryRecord>();

    /// <summary>Gets privacy-minimizing profile change-history records.</summary>
    public DbSet<ProfileChangeHistoryEntryRecord> ProfileChangeHistoryEntries => Set<ProfileChangeHistoryEntryRecord>();

    /// <summary>Gets the authoritative AI usage ledger records.</summary>
    public DbSet<AiUsageLedgerRecord> AiUsageLedgerEntries => Set<AiUsageLedgerRecord>();

    /// <summary>Gets user-owned recipe identity records.</summary>
    public DbSet<RecipeRecord> Recipes => Set<RecipeRecord>();

    /// <summary>Gets immutable, append-only recipe revision content records.</summary>
    public DbSet<RecipeRevisionRecord> RecipeRevisions => Set<RecipeRevisionRecord>();

    /// <summary>Gets owner-scoped cook-now recipe generation session records.</summary>
    public DbSet<RecipeGenerationSessionRecord> RecipeGenerationSessions => Set<RecipeGenerationSessionRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InternalUser>(entity =>
        {
            entity.ToTable("users", "identity");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        });

        modelBuilder.Entity<ProductRecord>(entity =>
        {
            entity.ToTable("products", "inventory", table =>
            {
                table.HasCheckConstraint("ck_products_display_name", "length(btrim(\"DisplayName\")) > 0");
                table.HasCheckConstraint("ck_products_normalized_search_name", "length(btrim(\"NormalizedSearchName\")) > 0");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.NormalizedSearchName).HasMaxLength(160).IsRequired();
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.OwnerUserId);
        });

        modelBuilder.Entity<LotRecord>(entity =>
        {
            entity.ToTable("lots", "inventory", table =>
            {
                table.HasCheckConstraint("ck_lots_quantity_mode", "(\"MeasuredValue\" IS NOT NULL AND \"MeasuredUnit\" IS NOT NULL AND \"AvailabilityState\" IS NULL) OR (\"MeasuredValue\" IS NULL AND \"MeasuredUnit\" IS NULL AND \"AvailabilityState\" IS NOT NULL)");
                table.HasCheckConstraint("ck_lots_measured_value", "\"MeasuredValue\" IS NULL OR \"MeasuredValue\" >= 0");
                table.HasCheckConstraint("ck_lots_measured_unit", "\"MeasuredUnit\" IS NULL OR \"MeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit')");
                table.HasCheckConstraint("ck_lots_availability_state", "\"AvailabilityState\" IS NULL OR \"AvailabilityState\" IN ('Available', 'Low', 'Unavailable')");
                table.HasCheckConstraint("ck_lots_storage", "(\"StorageLocation\" IN ('Pantry', 'Refrigerator', 'Freezer') AND \"CustomLocation\" IS NULL) OR (\"StorageLocation\" = 'Other' AND \"CustomLocation\" IS NOT NULL AND length(btrim(\"CustomLocation\")) > 0)");
                table.HasCheckConstraint("ck_lots_package_state", "\"PackageState\" IS NULL OR \"PackageState\" IN ('Sealed', 'Opened', 'Unknown')");
                table.HasCheckConstraint("ck_lots_expiration_provenance", "(\"PrintedExpirationDate\" IS NULL AND \"ExpirationProvenance\" IS NULL) OR (\"PrintedExpirationDate\" IS NOT NULL AND \"ExpirationProvenance\" IS NOT NULL AND \"ExpirationProvenance\" = 'UserEntered')");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            entity.Property(x => x.MeasuredValue).HasColumnType("numeric(18,3)");
            entity.Property(x => x.MeasuredUnit).HasMaxLength(20);
            entity.Property(x => x.AvailabilityState).HasMaxLength(20);
            entity.Property(x => x.StorageLocation).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CustomLocation).HasMaxLength(80);
            entity.Property(x => x.PackageState).HasMaxLength(20);
            entity.Property(x => x.ExpirationProvenance).HasMaxLength(30);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.ConcurrencyToken).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(x => x.ConcurrencyToken).IsUnique();
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductRecord>().WithMany().HasForeignKey(x => new { x.ProductId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt, x.Id });
        });

        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.ToTable("transactions", "inventory", table =>
            {
                table.HasCheckConstraint("ck_transactions_type", "\"Type\" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted', 'PreparationInputConsumed', 'PreparationOutputCreated')");
                table.HasCheckConstraint("ck_transactions_previous_quantity", "(\"PreviousMeasuredValue\" IS NULL AND \"PreviousMeasuredUnit\" IS NULL AND \"PreviousAvailabilityState\" IS NULL) OR (\"PreviousMeasuredValue\" IS NOT NULL AND \"PreviousMeasuredValue\" >= 0 AND \"PreviousMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"PreviousAvailabilityState\" IS NULL) OR (\"PreviousMeasuredValue\" IS NULL AND \"PreviousMeasuredUnit\" IS NULL AND \"PreviousAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))");
                table.HasCheckConstraint("ck_transactions_resulting_quantity", "(\"ResultingMeasuredValue\" IS NOT NULL AND \"ResultingMeasuredValue\" >= 0 AND \"ResultingMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"ResultingAvailabilityState\" IS NULL) OR (\"ResultingMeasuredValue\" IS NULL AND \"ResultingMeasuredUnit\" IS NULL AND \"ResultingAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))");
                table.HasCheckConstraint("ck_transactions_reason_code", "\"ReasonCode\" IS NULL OR length(btrim(\"ReasonCode\")) > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PreviousMeasuredValue).HasColumnType("numeric(18,3)");
            entity.Property(x => x.ResultingMeasuredValue).HasColumnType("numeric(18,3)");
            entity.Property(x => x.ReasonCode).HasMaxLength(100);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LotRecord>().WithMany().HasForeignKey(x => new { x.LotId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.LotId, x.OccurredAt });
        });

        modelBuilder.Entity<PreparationBatchRecord>(entity =>
        {
            entity.ToTable("preparation_batches", "inventory", table =>
            {
                table.HasCheckConstraint("ck_preparation_batches_source", "\"SourceType\" IN ('ManualPreparation')");
                table.HasCheckConstraint("ck_preparation_batches_prepared_at", "\"PreparedAt\" <= \"CreatedAt\"");
                table.HasCheckConstraint("ck_preparation_batches_declared_yield", "((\"DeclaredYieldMeasuredValue\" IS NOT NULL AND \"DeclaredYieldMeasuredValue\" > 0 AND \"DeclaredYieldMeasuredUnit\" IN ('Gram', 'Milliliter', 'Unit') AND \"DeclaredYieldAvailabilityState\" IS NULL) OR (\"DeclaredYieldMeasuredValue\" IS NULL AND \"DeclaredYieldMeasuredUnit\" IS NULL AND \"DeclaredYieldAvailabilityState\" IN ('Available', 'Low', 'Unavailable'))) IS TRUE");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            entity.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DeclaredYieldMeasuredValue).HasColumnType("numeric(18,3)");
            entity.Property(x => x.DeclaredYieldMeasuredUnit).HasMaxLength(20);
            entity.Property(x => x.DeclaredYieldAvailabilityState).HasMaxLength(20);
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProductRecord>().WithMany().HasForeignKey(x => new { x.OutputProductId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.PreparedAt, x.Id });
        });

        modelBuilder.Entity<PreparationInputRecord>(entity =>
        {
            entity.ToTable("preparation_inputs", "inventory", table =>
            {
                table.HasCheckConstraint("ck_preparation_inputs_quantity", "\"ConsumedValue\" > 0 AND \"ConsumedUnit\" IN ('Gram', 'Milliliter', 'Unit')");
            });
            entity.HasKey(x => new { x.BatchId, x.InputLotId });
            entity.Property(x => x.ConsumedValue).HasColumnType("numeric(18,3)");
            entity.Property(x => x.ConsumedUnit).HasMaxLength(20).IsRequired();
            entity.HasOne<PreparationBatchRecord>().WithMany().HasForeignKey(x => new { x.BatchId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LotRecord>().WithMany().HasForeignKey(x => new { x.InputLotId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.InputLotId });
        });

        modelBuilder.Entity<PreparationOutputRecord>(entity =>
        {
            entity.ToTable("preparation_outputs", "inventory");
            entity.HasKey(x => new { x.BatchId, x.OutputLotId });
            entity.HasOne<PreparationBatchRecord>().WithMany().HasForeignKey(x => new { x.BatchId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LotRecord>().WithMany().HasForeignKey(x => new { x.OutputLotId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.OutputLotId }).IsUnique();
        });

        modelBuilder.Entity<PreparedLotRecord>(entity =>
        {
            entity.ToTable("prepared_lots", "inventory", table =>
            {
                table.HasCheckConstraint("ck_prepared_lots_lifecycle", "\"LifecycleState\" IN ('Prepared')");
                table.HasCheckConstraint("ck_prepared_lots_evidence", "(\"ShelfLifeSource\" = 'Unknown' AND \"ShelfLifeDate\" IS NULL AND \"ShelfLifeConfidence\" = 'Unknown') OR (\"ShelfLifeSource\" IN ('UserEntered', 'Curated', 'Regional') AND \"ShelfLifeDate\" IS NOT NULL AND \"ShelfLifeConfidence\" IN ('Low', 'Medium', 'High'))");
            });
            entity.HasKey(x => new { x.LotId, x.OwnerUserId });
            entity.Property(x => x.LifecycleState).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ShelfLifeSource).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ShelfLifeConfidence).HasMaxLength(20).IsRequired();
            entity.Property(x => x.ShelfLifeConditions).HasMaxLength(500);
            entity.HasOne<LotRecord>().WithMany().HasForeignKey(x => new { x.LotId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PreparationBatchRecord>().WithMany().HasForeignKey(x => new { x.BatchId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.BatchId });
        });

        modelBuilder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("audit_events", "platform");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records", "platform", table =>
            {
                table.HasCheckConstraint("ck_idempotency_status_code", "\"StatusCode\" BETWEEN 200 AND 299");
                table.HasCheckConstraint("ck_idempotency_completion", "(\"CompletedAt\" IS NULL AND \"ResponseBody\" IS NULL AND \"ETag\" IS NULL) OR (\"CompletedAt\" IS NOT NULL AND \"ResponseBody\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Scope).HasMaxLength(100).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ResponseBody).HasColumnType("jsonb");
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.Scope, x.Key }).IsUnique();
        });

        modelBuilder.Entity<UserProfileRecord>(entity =>
        {
            entity.ToTable("user_profiles", "profiles", table =>
            {
                table.HasCheckConstraint("ck_profiles_language", "\"Language\" IS NULL OR \"Language\" IN ('en', 'pt-BR', 'es')");
                table.HasCheckConstraint("ck_profiles_currency", "\"Currency\" IS NULL OR \"Currency\" IN ('USD', 'BRL', 'EUR')");
                table.HasCheckConstraint("ck_profiles_region", "\"Region\" IS NULL OR \"Region\" IN ('US', 'BR', 'ES')");
                table.HasCheckConstraint("ck_profiles_measurement_system", "\"MeasurementSystem\" IS NULL OR \"MeasurementSystem\" IN ('Metric', 'UsCustomary')");
                table.HasCheckConstraint("ck_profiles_default_adult_count", "\"DefaultAdultCount\" IS NULL OR (\"DefaultAdultCount\" BETWEEN 1 AND 20)");
                table.HasCheckConstraint("ck_profiles_default_child_count", "\"DefaultChildCount\" IS NULL OR (\"DefaultChildCount\" BETWEEN 0 AND 20)");
                table.HasCheckConstraint("ck_profiles_default_serving_count", "\"DefaultServingCount\" IS NULL OR (\"DefaultServingCount\" BETWEEN 1 AND 30)");
            });
            entity.HasKey(x => x.OwnerUserId);
            entity.Property(x => x.DisplayName).HasMaxLength(80);
            entity.Property(x => x.DisplayNamePresence).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(10);
            entity.Property(x => x.Region).HasMaxLength(10);
            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.MeasurementSystem).HasMaxLength(20);
            entity.Property(x => x.TimeZone).HasMaxLength(64);
            entity.Property(x => x.PlanningCadence).HasMaxLength(20);
            entity.Property(x => x.ShoppingCadence).HasMaxLength(20);
            entity.Property(x => x.TermsVersion).HasMaxLength(32);
            entity.Property(x => x.PrivacyVersion).HasMaxLength(32);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.ConcurrencyToken).HasDefaultValueSql("gen_random_uuid()");
            entity.HasIndex(x => x.ConcurrencyToken).IsUnique();
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PreferenceEntryRecord>(entity =>
        {
            entity.ToTable("preference_entries", "profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Category).HasMaxLength(40).IsRequired();
            entity.Property(x => x.StableCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500);
            entity.Property(x => x.Presence).HasMaxLength(20).IsRequired();
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(x => x.OwnerUserId).HasPrincipalKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerUserId, x.Category, x.StableCode }).IsUnique();
        });

        modelBuilder.Entity<EquipmentEntryRecord>(entity =>
        {
            entity.ToTable("equipment_entries", "profiles", table => table.HasCheckConstraint("ck_equipment_capacity", "\"Capacity\" IS NULL OR \"Capacity\" >= 0"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StableCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CustomName).HasMaxLength(80);
            entity.Property(x => x.Capacity).HasColumnType("numeric(18,3)");
            entity.Property(x => x.CapacityUnit).HasMaxLength(20);
            entity.Property(x => x.ConstraintNote).HasMaxLength(200);
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(x => x.OwnerUserId).HasPrincipalKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerUserId, x.StableCode }).IsUnique();
        });

        modelBuilder.Entity<ProfileOrderedCodeEntryRecord>(entity =>
        {
            entity.ToTable("ordered_code_entries", "profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ListName).HasMaxLength(40).IsRequired();
            entity.Property(x => x.StableCode).HasMaxLength(64).IsRequired();
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(x => x.OwnerUserId).HasPrincipalKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerUserId, x.ListName, x.SortOrder });
        });

        modelBuilder.Entity<ProfileChangeHistoryEntryRecord>(entity =>
        {
            entity.ToTable("change_history", "profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SectionChanged).HasMaxLength(40).IsRequired();
            entity.Property(x => x.FieldCodesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(x => x.OwnerUserId).HasPrincipalKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OwnerUserId, x.OccurredAt });
        });

        modelBuilder.Entity<AiUsageLedgerRecord>(entity =>
        {
            entity.ToTable("usage_ledger", "ai", table =>
            {
                table.HasCheckConstraint("ck_usage_ledger_status", "\"Status\" IN ('Reserved', 'Settled', 'Released')");
                table.HasCheckConstraint("ck_usage_ledger_reserved_units", "\"ReservedUnits\" > 0");
                table.HasCheckConstraint(
                    "ck_usage_ledger_settlement",
                    "(\"Status\" = 'Reserved' AND \"SettledUnits\" IS NULL AND \"Provider\" IS NULL AND \"Model\" IS NULL AND \"ClosedAt\" IS NULL) " +
                    "OR (\"Status\" = 'Settled' AND \"SettledUnits\" IS NOT NULL AND \"SettledUnits\" > 0 AND \"Provider\" IS NOT NULL AND \"Model\" IS NOT NULL AND \"ClosedAt\" IS NOT NULL) " +
                    "OR (\"Status\" = 'Released' AND \"SettledUnits\" IS NULL AND \"ClosedAt\" IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(60);
            entity.Property(x => x.Model).HasMaxLength(100);
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.Status });
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<RecipeRecord>(entity =>
        {
            entity.ToTable("recipes", "recipes", table => table.HasCheckConstraint("ck_recipes_current_revision_number", "\"CurrentRevisionNumber\" >= 1"));
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt, x.Id });
        });

        modelBuilder.Entity<RecipeRevisionRecord>(entity =>
        {
            entity.ToTable("recipe_revisions", "recipes", table =>
            {
                table.HasCheckConstraint("ck_recipe_revisions_revision_number", "\"RevisionNumber\" >= 1");
                table.HasCheckConstraint("ck_recipe_revisions_servings", "\"Servings\" BETWEEN 1 AND 24");
                table.HasCheckConstraint("ck_recipe_revisions_name", "length(btrim(\"Name\")) > 0");
                table.HasCheckConstraint("ck_recipe_revisions_source_candidate_id", "length(btrim(\"SourceCandidateId\")) > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.MealTypesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.NormalizedRecipeJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.ThumbnailVisualJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.SourceCandidateId).HasMaxLength(64).IsRequired();
            entity.HasOne<RecipeRecord>().WithMany().HasForeignKey(x => new { x.RecipeId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RecipeId, x.RevisionNumber }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserId, x.RecipeId, x.RevisionNumber });
        });

        modelBuilder.Entity<RecipeGenerationSessionRecord>(entity =>
        {
            entity.ToTable("generation_sessions", "recipes", table =>
            {
                table.HasCheckConstraint("ck_generation_sessions_execution_mode", "\"ExecutionMode\" IN ('cook_now')");
                table.HasCheckConstraint("ck_generation_sessions_status", "\"Status\" IN ('AwaitingCandidates', 'CandidatesReady', 'Expanding', 'Selected', 'Failed')");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.OwnerUserId });
            entity.Property(x => x.ExecutionMode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CandidatesSnapshotJson).HasColumnType("jsonb");
            entity.Property(x => x.SelectedCandidateId).HasMaxLength(64);
            entity.Property(x => x.FailureReason).HasMaxLength(100);
            entity.HasOne<InternalUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RecipeRecord>().WithMany().HasForeignKey(x => new { x.SelectedRecipeId, x.OwnerUserId }).HasPrincipalKey(x => new { x.Id, x.OwnerUserId }).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OwnerUserId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.OwnerUserId, x.SelectIdempotencyKey }).IsUnique().HasFilter("\"SelectIdempotencyKey\" IS NOT NULL");
            entity.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.OwnerUserId, x.UpdatedAt, x.Id });
        });
    }
}

/// <summary>Persistence representation of a product owned by one internal user.</summary>
public sealed class ProductRecord
{
    /// <summary>Gets or sets the product identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the human-readable product name.</summary>
    public required string DisplayName { get; set; }
    /// <summary>Gets or sets the normalized product search name.</summary>
    public required string NormalizedSearchName { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the latest product metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets whether the product is soft-deleted.</summary>
    public bool IsDeleted { get; set; }
}

/// <summary>Persistence representation of one user-owned physical inventory lot.</summary>
public sealed class LotRecord
{
    /// <summary>Gets or sets the lot identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the associated user-owned product identifier.</summary>
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the measured amount using PostgreSQL <c>numeric(18,3)</c>, if measured.</summary>
    public decimal? MeasuredValue { get; set; }
    /// <summary>Gets or sets the canonical unit when <see cref="MeasuredValue"/> is present.</summary>
    public string? MeasuredUnit { get; set; }
    /// <summary>Gets or sets the qualitative availability state when the lot is not measured.</summary>
    public string? AvailabilityState { get; set; }
    /// <summary>Gets or sets the required storage location.</summary>
    public required string StorageLocation { get; set; }
    /// <summary>Gets or sets the required custom location for the <c>Other</c> location.</summary>
    public string? CustomLocation { get; set; }
    /// <summary>Gets or sets the optional package state.</summary>
    public string? PackageState { get; set; }
    /// <summary>Gets or sets the optional user-entered printed expiration calendar date.</summary>
    public DateOnly? PrintedExpirationDate { get; set; }
    /// <summary>Gets or sets the provenance of the printed expiration date.</summary>
    public string? ExpirationProvenance { get; set; }
    /// <summary>Gets or sets optional private notes, which must never be emitted in telemetry.</summary>
    public string? Notes { get; set; }
    /// <summary>Gets or sets the internal optimistic-concurrency version; it is never exposed directly.</summary>
    public long Version { get; set; }
    /// <summary>Gets or sets the opaque externally observable concurrency token.</summary>
    public Guid ConcurrencyToken { get; set; }
    /// <summary>Gets or sets the UTC creation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC instant of the last mutation.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Gets or sets the UTC soft-deletion instant, if deleted.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Immutable persistence representation of an inventory lifecycle transition.</summary>
public sealed class TransactionRecord
{
    /// <summary>Gets or sets the transaction identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the associated lot identifier.</summary>
    public Guid LotId { get; set; }
    /// <summary>Gets or sets the stable lifecycle transaction type.</summary>
    public required string Type { get; set; }
    /// <summary>Gets or sets the prior measured amount, if the prior state was measured.</summary>
    public decimal? PreviousMeasuredValue { get; set; }
    /// <summary>Gets or sets the prior canonical measured unit, if applicable.</summary>
    public string? PreviousMeasuredUnit { get; set; }
    /// <summary>Gets or sets the prior qualitative availability state, if applicable.</summary>
    public string? PreviousAvailabilityState { get; set; }
    /// <summary>Gets or sets the resulting measured amount, if the resulting state is measured.</summary>
    public decimal? ResultingMeasuredValue { get; set; }
    /// <summary>Gets or sets the resulting canonical measured unit, if applicable.</summary>
    public string? ResultingMeasuredUnit { get; set; }
    /// <summary>Gets or sets the resulting qualitative availability state, if applicable.</summary>
    public string? ResultingAvailabilityState { get; set; }
    /// <summary>Gets or sets the normalized reason code supplied for the transition.</summary>
    public string? ReasonCode { get; set; }
    /// <summary>Gets or sets the optional private transition note.</summary>
    public string? Note { get; set; }
    /// <summary>Gets or sets the client idempotency key when a command supplied one.</summary>
    public Guid? IdempotencyKey { get; set; }
    /// <summary>Gets or sets the UTC instant at which the transition occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Immutable persistence representation of one manual preparation transaction.</summary>
public sealed class PreparationBatchRecord
{
    /// <summary>Gets or sets the preparation batch identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the product shared by all v1 output portions.</summary>
    public Guid OutputProductId { get; set; }
    /// <summary>Gets or sets the immutable declared measured yield, when this batch has measured output.</summary>
    public decimal? DeclaredYieldMeasuredValue { get; set; }
    /// <summary>Gets or sets the immutable canonical measured-yield unit, when applicable.</summary>
    public string? DeclaredYieldMeasuredUnit { get; set; }
    /// <summary>Gets or sets the immutable declared qualitative yield state, when applicable.</summary>
    public string? DeclaredYieldAvailabilityState { get; set; }
    /// <summary>Gets or sets the controlled source type.</summary>
    public required string SourceType { get; set; }
    /// <summary>Gets or sets when the component was prepared.</summary>
    public DateTimeOffset PreparedAt { get; set; }
    /// <summary>Gets or sets when the authoritative batch was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Immutable provenance representation of one consumed parent quantity.</summary>
public sealed class PreparationInputRecord
{
    /// <summary>Gets or sets the parent preparation batch identifier.</summary>
    public Guid BatchId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the consumed input lot identifier.</summary>
    public Guid InputLotId { get; set; }
    /// <summary>Gets or sets the actual positive consumed value.</summary>
    public decimal ConsumedValue { get; set; }
    /// <summary>Gets or sets the canonical unit for the consumed value.</summary>
    public required string ConsumedUnit { get; set; }
}

/// <summary>Immutable provenance representation of one produced output lot.</summary>
public sealed class PreparationOutputRecord
{
    /// <summary>Gets or sets the parent preparation batch identifier.</summary>
    public Guid BatchId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the output lot identifier.</summary>
    public Guid OutputLotId { get; set; }
}

/// <summary>Prepared-specific metadata that supplements, rather than weakens, the authoritative lot record.</summary>
public sealed class PreparedLotRecord
{
    /// <summary>Gets or sets the output lot identifier.</summary>
    public Guid LotId { get; set; }
    /// <summary>Gets or sets the authoritative owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the creating preparation batch identifier.</summary>
    public Guid BatchId { get; set; }
    /// <summary>Gets or sets the controlled lifecycle state.</summary>
    public required string LifecycleState { get; set; }
    /// <summary>Gets or sets when the component was prepared.</summary>
    public DateTimeOffset PreparedAt { get; set; }
    /// <summary>Gets or sets the optional advisory shelf-life date.</summary>
    public DateOnly? ShelfLifeDate { get; set; }
    /// <summary>Gets or sets the shelf-life evidence source.</summary>
    public required string ShelfLifeSource { get; set; }
    /// <summary>Gets or sets the shelf-life evidence confidence.</summary>
    public required string ShelfLifeConfidence { get; set; }
    /// <summary>Gets or sets optional private handling conditions.</summary>
    public string? ShelfLifeConditions { get; set; }
}

/// <summary>Immutable audit trail record for an authorized action.</summary>
public sealed class AuditEventRecord
{
    /// <summary>Gets or sets the audit event identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the internal actor identifier.</summary>
    public Guid ActorUserId { get; set; }
    /// <summary>Gets or sets the stable event name.</summary>
    public required string EventName { get; set; }
    /// <summary>Gets or sets the audited resource type.</summary>
    public required string TargetType { get; set; }
    /// <summary>Gets or sets the audited resource identifier.</summary>
    public Guid TargetId { get; set; }
    /// <summary>Gets or sets the request correlation identifier without credentials or request bodies.</summary>
    public required string CorrelationId { get; set; }
    /// <summary>Gets or sets non-sensitive JSON audit metadata.</summary>
    public required string MetadataJson { get; set; }
    /// <summary>Gets or sets the UTC event instant.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Persistence record used to replay an already completed idempotent command.</summary>
public sealed class IdempotencyRecord
{
    /// <summary>Gets or sets the record identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the authoritative command owner identifier.</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>Gets or sets the command scope, which partitions idempotency keys.</summary>
    public required string Scope { get; set; }
    /// <summary>Gets or sets the client-provided UUID idempotency key.</summary>
    public Guid Key { get; set; }
    /// <summary>Gets or sets the canonical request hash used to detect key reuse conflicts.</summary>
    public required string RequestHash { get; set; }
    /// <summary>Gets or sets the replayable HTTP status code.</summary>
    public int StatusCode { get; set; }
    /// <summary>Gets or sets the replayable response body, if the command completed.</summary>
    public string? ResponseBody { get; set; }
    /// <summary>Gets or sets the replayable opaque ETag, if the command completed.</summary>
    public string? ETag { get; set; }
    /// <summary>Gets or sets the UTC reservation instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Gets or sets the UTC completion instant, or <see langword="null"/> while processing.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
