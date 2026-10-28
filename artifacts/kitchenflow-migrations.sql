CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'platform') THEN
            CREATE SCHEMA platform;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'inventory') THEN
            CREATE SCHEMA inventory;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
            CREATE SCHEMA identity;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE platform.audit_events (
        "Id" uuid NOT NULL,
        "ActorUserId" uuid NOT NULL,
        "EventName" character varying(100) NOT NULL,
        "TargetType" character varying(100) NOT NULL,
        "TargetId" uuid NOT NULL,
        "CorrelationId" character varying(100) NOT NULL,
        "MetadataJson" jsonb NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_audit_events" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE platform.idempotency_records (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "Scope" character varying(100) NOT NULL,
        "Key" uuid NOT NULL,
        "RequestHash" character varying(128) NOT NULL,
        "StatusCode" integer NOT NULL,
        "ResponseBody" jsonb,
        "ETag" text,
        "CreatedAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        CONSTRAINT "PK_idempotency_records" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE inventory.lots (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "ProductId" uuid NOT NULL,
        "MeasuredValue" numeric(18,3),
        "MeasuredUnit" character varying(20),
        "AvailabilityState" character varying(20),
        "StorageLocation" character varying(30) NOT NULL,
        "CustomLocation" character varying(80),
        "PackageState" character varying(20),
        "PrintedExpirationDate" date,
        "ExpirationProvenance" character varying(30),
        "Notes" character varying(1000),
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "DeletedAt" timestamp with time zone,
        CONSTRAINT "PK_lots" PRIMARY KEY ("Id"),
        CONSTRAINT ck_lots_quantity_mode CHECK (("MeasuredValue" IS NOT NULL AND "MeasuredUnit" IS NOT NULL AND "AvailabilityState" IS NULL) OR ("MeasuredValue" IS NULL AND "MeasuredUnit" IS NULL AND "AvailabilityState" IS NOT NULL))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE inventory.products (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "DisplayName" character varying(160) NOT NULL,
        "NormalizedSearchName" character varying(160) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "IsDeleted" boolean NOT NULL,
        CONSTRAINT "PK_products" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE inventory.transactions (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "LotId" uuid NOT NULL,
        "Type" character varying(30) NOT NULL,
        "PreviousMeasuredValue" numeric(18,3),
        "PreviousMeasuredUnit" text,
        "PreviousAvailabilityState" text,
        "ResultingMeasuredValue" numeric(18,3),
        "ResultingMeasuredUnit" text,
        "ResultingAvailabilityState" text,
        "ReasonCode" character varying(100),
        "Note" character varying(1000),
        "IdempotencyKey" uuid,
        "OccurredAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_transactions" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE TABLE identity.users (
        "Id" uuid NOT NULL,
        "Issuer" character varying(500) NOT NULL,
        "Subject" character varying(500) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE UNIQUE INDEX "IX_idempotency_records_OwnerUserId_Scope_Key" ON platform.idempotency_records ("OwnerUserId", "Scope", "Key");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE INDEX "IX_lots_OwnerUserId_UpdatedAt_Id" ON inventory.lots ("OwnerUserId", "UpdatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE INDEX "IX_products_OwnerUserId" ON inventory.products ("OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE INDEX "IX_transactions_OwnerUserId_LotId_OccurredAt" ON inventory.transactions ("OwnerUserId", "LotId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    CREATE UNIQUE INDEX "IX_users_Issuer_Subject" ON identity.users ("Issuer", "Subject");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729013459_InitialInventorySlice') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260729013459_InitialInventorySlice', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.products ADD CONSTRAINT "AK_products_Id_OwnerUserId" UNIQUE ("Id", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT "AK_lots_Id_OwnerUserId" UNIQUE ("Id", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    CREATE INDEX "IX_transactions_LotId_OwnerUserId" ON inventory.transactions ("LotId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    CREATE INDEX "IX_lots_ProductId_OwnerUserId" ON inventory.lots ("ProductId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_availability_state CHECK ("AvailabilityState" IS NULL OR "AvailabilityState" IN ('Available', 'Low', 'Unavailable'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_expiration_provenance CHECK (("PrintedExpirationDate" IS NULL AND "ExpirationProvenance" IS NULL) OR ("PrintedExpirationDate" IS NOT NULL AND "ExpirationProvenance" = 'UserEntered'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_measured_unit CHECK ("MeasuredUnit" IS NULL OR "MeasuredUnit" IN ('Gram', 'Milliliter', 'Unit'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_measured_value CHECK ("MeasuredValue" IS NULL OR "MeasuredValue" >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_package_state CHECK ("PackageState" IS NULL OR "PackageState" IN ('Sealed', 'Opened', 'Unknown'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_storage CHECK (("StorageLocation" IN ('Pantry', 'Refrigerator', 'Freezer') AND "CustomLocation" IS NULL) OR ("StorageLocation" = 'Other' AND "CustomLocation" IS NOT NULL AND length(btrim("CustomLocation")) > 0));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    CREATE INDEX "IX_audit_events_ActorUserId" ON platform.audit_events ("ActorUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE platform.audit_events ADD CONSTRAINT "FK_audit_events_users_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE platform.idempotency_records ADD CONSTRAINT "FK_idempotency_records_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT "FK_lots_products_ProductId_OwnerUserId" FOREIGN KEY ("ProductId", "OwnerUserId") REFERENCES inventory.products ("Id", "OwnerUserId") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT "FK_lots_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.products ADD CONSTRAINT "FK_products_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT "FK_transactions_lots_LotId_OwnerUserId" FOREIGN KEY ("LotId", "OwnerUserId") REFERENCES inventory.lots ("Id", "OwnerUserId") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT "FK_transactions_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729123210_EnforceInventoryReferentialIntegrity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260729123210_EnforceInventoryReferentialIntegrity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT ck_transactions_previous_quantity CHECK (("PreviousMeasuredValue" IS NULL AND "PreviousMeasuredUnit" IS NULL AND "PreviousAvailabilityState" IS NULL) OR ("PreviousMeasuredValue" IS NOT NULL AND "PreviousMeasuredValue" >= 0 AND "PreviousMeasuredUnit" IN ('Gram', 'Milliliter', 'Unit') AND "PreviousAvailabilityState" IS NULL) OR ("PreviousMeasuredValue" IS NULL AND "PreviousMeasuredUnit" IS NULL AND "PreviousAvailabilityState" IN ('Available', 'Low', 'Unavailable')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT ck_transactions_reason_code CHECK ("ReasonCode" IS NULL OR length(btrim("ReasonCode")) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT ck_transactions_resulting_quantity CHECK (("ResultingMeasuredValue" IS NOT NULL AND "ResultingMeasuredValue" >= 0 AND "ResultingMeasuredUnit" IN ('Gram', 'Milliliter', 'Unit') AND "ResultingAvailabilityState" IS NULL) OR ("ResultingMeasuredValue" IS NULL AND "ResultingMeasuredUnit" IS NULL AND "ResultingAvailabilityState" IN ('Available', 'Low', 'Unavailable')));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT ck_transactions_type CHECK ("Type" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.products ADD CONSTRAINT ck_products_display_name CHECK (length(btrim("DisplayName")) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE inventory.products ADD CONSTRAINT ck_products_normalized_search_name CHECK (length(btrim("NormalizedSearchName")) > 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE platform.idempotency_records ADD CONSTRAINT ck_idempotency_completion CHECK (("CompletedAt" IS NULL AND "ResponseBody" IS NULL AND "ETag" IS NULL) OR ("CompletedAt" IS NOT NULL AND "ResponseBody" IS NOT NULL));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    ALTER TABLE platform.idempotency_records ADD CONSTRAINT ck_idempotency_status_code CHECK ("StatusCode" BETWEEN 200 AND 299);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260729193936_EnforceInventoryHistoryIntegrity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260729193936_EnforceInventoryHistoryIntegrity', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731021725_EnforceAppendOnlyHistory') THEN
    CREATE FUNCTION platform.reject_immutable_history_mutation()
    RETURNS trigger
    LANGUAGE plpgsql
    AS $$
    BEGIN
        RAISE EXCEPTION USING
            ERRCODE = '55000',
            MESSAGE = format('%I.%I is append-only', TG_TABLE_SCHEMA, TG_TABLE_NAME);
    END;
    $$;

    CREATE TRIGGER transactions_are_append_only
    BEFORE UPDATE OR DELETE ON inventory.transactions
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

    CREATE TRIGGER audit_events_are_append_only
    BEFORE UPDATE OR DELETE ON platform.audit_events
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731021725_EnforceAppendOnlyHistory') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260731021725_EnforceAppendOnlyHistory', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731024742_TightenExpirationProvenance') THEN
    ALTER TABLE inventory.lots DROP CONSTRAINT ck_lots_expiration_provenance;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731024742_TightenExpirationProvenance') THEN
    ALTER TABLE inventory.lots ADD CONSTRAINT ck_lots_expiration_provenance CHECK (("PrintedExpirationDate" IS NULL AND "ExpirationProvenance" IS NULL) OR ("PrintedExpirationDate" IS NOT NULL AND "ExpirationProvenance" IS NOT NULL AND "ExpirationProvenance" = 'UserEntered'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731024742_TightenExpirationProvenance') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260731024742_TightenExpirationProvenance', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731120209_AddInventoryLotConcurrencyToken') THEN
    ALTER TABLE inventory.lots ADD "ConcurrencyToken" uuid NOT NULL DEFAULT (gen_random_uuid());
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731120209_AddInventoryLotConcurrencyToken') THEN
    CREATE UNIQUE INDEX "IX_lots_ConcurrencyToken" ON inventory.lots ("ConcurrencyToken");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731120209_AddInventoryLotConcurrencyToken') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260731120209_AddInventoryLotConcurrencyToken', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'profiles') THEN
            CREATE SCHEMA profiles;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE TABLE profiles.user_profiles (
        "OwnerUserId" uuid NOT NULL,
        "DisplayName" character varying(80),
        "DisplayNamePresence" character varying(20) NOT NULL,
        "DefaultAdultCount" integer,
        "DefaultAdultCountPresence" text NOT NULL,
        "DefaultChildCount" integer,
        "DefaultChildCountPresence" text NOT NULL,
        "DefaultServingCount" integer,
        "DefaultServingCountPresence" text NOT NULL,
        "Language" character varying(10),
        "LanguagePresence" text NOT NULL,
        "Region" character varying(10),
        "RegionPresence" text NOT NULL,
        "Currency" character varying(10),
        "CurrencyPresence" text NOT NULL,
        "MeasurementSystem" character varying(20),
        "MeasurementSystemPresence" text NOT NULL,
        "TimeZone" character varying(64),
        "TimeZonePresence" text NOT NULL,
        "PlanningCadence" character varying(20),
        "PlanningCadencePresence" text NOT NULL,
        "ShoppingCadence" character varying(20),
        "ShoppingCadencePresence" text NOT NULL,
        "OverallSkill" text,
        "OverallSkillPresence" text NOT NULL,
        "Confidence" text,
        "ConfidencePresence" text NOT NULL,
        "PreferredInstructionDetail" text,
        "PreferredInstructionDetailPresence" text NOT NULL,
        "OrdinaryPrepMinutes" integer,
        "OrdinaryPrepMinutesPresence" text NOT NULL,
        "ExceptionalPrepMinutes" integer,
        "ExceptionalPrepMinutesPresence" text NOT NULL,
        "EffortTolerance" text,
        "EffortTolerancePresence" text NOT NULL,
        "CleanupTolerance" text,
        "CleanupTolerancePresence" text NOT NULL,
        "RepeatMealPreference" text,
        "RepeatMealPreferencePresence" text NOT NULL,
        "ReheatingPreference" text,
        "ReheatingPreferencePresence" text NOT NULL,
        "LeftoverPreference" text,
        "LeftoverPreferencePresence" text NOT NULL,
        "FreezingPreference" text,
        "FreezingPreferencePresence" text NOT NULL,
        "AdultDeclared" boolean,
        "TermsVersion" character varying(32),
        "PrivacyVersion" character varying(32),
        "TermsAcceptedAt" timestamp with time zone,
        "ConcurrencyToken" uuid NOT NULL DEFAULT (gen_random_uuid()),
        "Version" bigint NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_user_profiles" PRIMARY KEY ("OwnerUserId"),
        CONSTRAINT ck_profiles_currency CHECK ("Currency" IS NULL OR "Currency" IN ('USD', 'BRL', 'EUR')),
        CONSTRAINT ck_profiles_default_adult_count CHECK ("DefaultAdultCount" IS NULL OR ("DefaultAdultCount" BETWEEN 1 AND 20)),
        CONSTRAINT ck_profiles_default_child_count CHECK ("DefaultChildCount" IS NULL OR ("DefaultChildCount" BETWEEN 0 AND 20)),
        CONSTRAINT ck_profiles_default_serving_count CHECK ("DefaultServingCount" IS NULL OR ("DefaultServingCount" BETWEEN 1 AND 30)),
        CONSTRAINT ck_profiles_language CHECK ("Language" IS NULL OR "Language" IN ('en', 'pt-BR', 'es')),
        CONSTRAINT ck_profiles_measurement_system CHECK ("MeasurementSystem" IS NULL OR "MeasurementSystem" IN ('Metric', 'UsCustomary')),
        CONSTRAINT ck_profiles_region CHECK ("Region" IS NULL OR "Region" IN ('US', 'BR', 'ES')),
        CONSTRAINT "FK_user_profiles_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE TABLE profiles.change_history (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "SectionChanged" character varying(40) NOT NULL,
        "FieldCodesJson" jsonb NOT NULL,
        "CorrelationId" character varying(100) NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_change_history" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_change_history_user_profiles_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES profiles.user_profiles ("OwnerUserId") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE TABLE profiles.equipment_entries (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "StableCode" character varying(64) NOT NULL,
        "CustomName" character varying(80),
        "Capacity" numeric(18,3),
        "CapacityUnit" character varying(20),
        "ConstraintNote" character varying(200),
        "IsRemoved" boolean NOT NULL,
        "SortOrder" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_equipment_entries" PRIMARY KEY ("Id"),
        CONSTRAINT ck_equipment_capacity CHECK ("Capacity" IS NULL OR "Capacity" >= 0),
        CONSTRAINT "FK_equipment_entries_user_profiles_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES profiles.user_profiles ("OwnerUserId") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE TABLE profiles.ordered_code_entries (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "ListName" character varying(40) NOT NULL,
        "StableCode" character varying(64) NOT NULL,
        "SortOrder" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ordered_code_entries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ordered_code_entries_user_profiles_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES profiles.user_profiles ("OwnerUserId") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE TABLE profiles.preference_entries (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "Category" character varying(40) NOT NULL,
        "StableCode" character varying(64) NOT NULL,
        "Note" character varying(500),
        "Presence" character varying(20) NOT NULL,
        "SortOrder" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_preference_entries" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_preference_entries_user_profiles_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES profiles.user_profiles ("OwnerUserId") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE INDEX "IX_change_history_OwnerUserId_OccurredAt" ON profiles.change_history ("OwnerUserId", "OccurredAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE INDEX "IX_equipment_entries_OwnerUserId_StableCode" ON profiles.equipment_entries ("OwnerUserId", "StableCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE INDEX "IX_ordered_code_entries_OwnerUserId_ListName_SortOrder" ON profiles.ordered_code_entries ("OwnerUserId", "ListName", "SortOrder");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE UNIQUE INDEX "IX_preference_entries_OwnerUserId_Category_StableCode" ON profiles.preference_entries ("OwnerUserId", "Category", "StableCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    CREATE UNIQUE INDEX "IX_user_profiles_ConcurrencyToken" ON profiles.user_profiles ("ConcurrencyToken");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260731185224_InitialProfilesSlice') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260731185224_InitialProfilesSlice', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260801070903_EnforceUniqueProfileEquipmentStableCode') THEN
    DROP INDEX profiles."IX_equipment_entries_OwnerUserId_StableCode";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260801070903_EnforceUniqueProfileEquipmentStableCode') THEN
    CREATE UNIQUE INDEX "IX_equipment_entries_OwnerUserId_StableCode" ON profiles.equipment_entries ("OwnerUserId", "StableCode");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260801070903_EnforceUniqueProfileEquipmentStableCode') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260801070903_EnforceUniqueProfileEquipmentStableCode', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    ALTER TABLE inventory.transactions DROP CONSTRAINT ck_transactions_type;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE TABLE inventory.preparation_batches (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "OutputProductId" uuid NOT NULL,
        "DeclaredYieldMeasuredValue" numeric(18,3),
        "DeclaredYieldMeasuredUnit" character varying(20),
        "DeclaredYieldAvailabilityState" character varying(20),
        "SourceType" character varying(40) NOT NULL,
        "PreparedAt" timestamp with time zone NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_preparation_batches" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_preparation_batches_Id_OwnerUserId" UNIQUE ("Id", "OwnerUserId"),
        CONSTRAINT ck_preparation_batches_declared_yield CHECK ((("DeclaredYieldMeasuredValue" IS NOT NULL AND "DeclaredYieldMeasuredValue" > 0 AND "DeclaredYieldMeasuredUnit" IN ('Gram', 'Milliliter', 'Unit') AND "DeclaredYieldAvailabilityState" IS NULL) OR ("DeclaredYieldMeasuredValue" IS NULL AND "DeclaredYieldMeasuredUnit" IS NULL AND "DeclaredYieldAvailabilityState" IN ('Available', 'Low', 'Unavailable'))) IS TRUE),
        CONSTRAINT ck_preparation_batches_prepared_at CHECK ("PreparedAt" <= "CreatedAt"),
        CONSTRAINT ck_preparation_batches_source CHECK ("SourceType" IN ('ManualPreparation')),
        CONSTRAINT "FK_preparation_batches_products_OutputProductId_OwnerUserId" FOREIGN KEY ("OutputProductId", "OwnerUserId") REFERENCES inventory.products ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_preparation_batches_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE TABLE inventory.preparation_inputs (
        "BatchId" uuid NOT NULL,
        "InputLotId" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "ConsumedValue" numeric(18,3) NOT NULL,
        "ConsumedUnit" character varying(20) NOT NULL,
        CONSTRAINT "PK_preparation_inputs" PRIMARY KEY ("BatchId", "InputLotId"),
        CONSTRAINT ck_preparation_inputs_quantity CHECK ("ConsumedValue" > 0 AND "ConsumedUnit" IN ('Gram', 'Milliliter', 'Unit')),
        CONSTRAINT "FK_preparation_inputs_lots_InputLotId_OwnerUserId" FOREIGN KEY ("InputLotId", "OwnerUserId") REFERENCES inventory.lots ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_preparation_inputs_preparation_batches_BatchId_OwnerUserId" FOREIGN KEY ("BatchId", "OwnerUserId") REFERENCES inventory.preparation_batches ("Id", "OwnerUserId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE TABLE inventory.preparation_outputs (
        "BatchId" uuid NOT NULL,
        "OutputLotId" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        CONSTRAINT "PK_preparation_outputs" PRIMARY KEY ("BatchId", "OutputLotId"),
        CONSTRAINT "FK_preparation_outputs_lots_OutputLotId_OwnerUserId" FOREIGN KEY ("OutputLotId", "OwnerUserId") REFERENCES inventory.lots ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_preparation_outputs_preparation_batches_BatchId_OwnerUserId" FOREIGN KEY ("BatchId", "OwnerUserId") REFERENCES inventory.preparation_batches ("Id", "OwnerUserId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE TABLE inventory.prepared_lots (
        "LotId" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "BatchId" uuid NOT NULL,
        "LifecycleState" character varying(30) NOT NULL,
        "PreparedAt" timestamp with time zone NOT NULL,
        "ShelfLifeDate" date,
        "ShelfLifeSource" character varying(30) NOT NULL,
        "ShelfLifeConfidence" character varying(20) NOT NULL,
        "ShelfLifeConditions" character varying(500),
        CONSTRAINT "PK_prepared_lots" PRIMARY KEY ("LotId", "OwnerUserId"),
        CONSTRAINT ck_prepared_lots_evidence CHECK (("ShelfLifeSource" = 'Unknown' AND "ShelfLifeDate" IS NULL AND "ShelfLifeConfidence" = 'Unknown') OR ("ShelfLifeSource" IN ('UserEntered', 'Curated', 'Regional') AND "ShelfLifeDate" IS NOT NULL AND "ShelfLifeConfidence" IN ('Low', 'Medium', 'High'))),
        CONSTRAINT ck_prepared_lots_lifecycle CHECK ("LifecycleState" IN ('Prepared')),
        CONSTRAINT "FK_prepared_lots_lots_LotId_OwnerUserId" FOREIGN KEY ("LotId", "OwnerUserId") REFERENCES inventory.lots ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_prepared_lots_preparation_batches_BatchId_OwnerUserId" FOREIGN KEY ("BatchId", "OwnerUserId") REFERENCES inventory.preparation_batches ("Id", "OwnerUserId") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    ALTER TABLE inventory.transactions ADD CONSTRAINT ck_transactions_type CHECK ("Type" IN ('Initial', 'Consume', 'Discard', 'Correct', 'AvailabilityChanged', 'Deleted', 'PreparationInputConsumed', 'PreparationOutputCreated'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_batches_OutputProductId_OwnerUserId" ON inventory.preparation_batches ("OutputProductId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_batches_OwnerUserId_PreparedAt_Id" ON inventory.preparation_batches ("OwnerUserId", "PreparedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_inputs_BatchId_OwnerUserId" ON inventory.preparation_inputs ("BatchId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_inputs_InputLotId_OwnerUserId" ON inventory.preparation_inputs ("InputLotId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_inputs_OwnerUserId_InputLotId" ON inventory.preparation_inputs ("OwnerUserId", "InputLotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_outputs_BatchId_OwnerUserId" ON inventory.preparation_outputs ("BatchId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_preparation_outputs_OutputLotId_OwnerUserId" ON inventory.preparation_outputs ("OutputLotId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE UNIQUE INDEX "IX_preparation_outputs_OwnerUserId_OutputLotId" ON inventory.preparation_outputs ("OwnerUserId", "OutputLotId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_prepared_lots_BatchId_OwnerUserId" ON inventory.prepared_lots ("BatchId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE INDEX "IX_prepared_lots_OwnerUserId_BatchId" ON inventory.prepared_lots ("OwnerUserId", "BatchId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    CREATE TRIGGER preparation_batches_are_append_only
    BEFORE UPDATE OR DELETE ON inventory.preparation_batches
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

    CREATE TRIGGER preparation_inputs_are_append_only
    BEFORE UPDATE OR DELETE ON inventory.preparation_inputs
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

    CREATE TRIGGER preparation_outputs_are_append_only
    BEFORE UPDATE OR DELETE ON inventory.preparation_outputs
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();

    CREATE TRIGGER prepared_lots_are_append_only
    BEFORE UPDATE OR DELETE ON inventory.prepared_lots
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260804013603_AddPreparedComponentTransactions') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260804013603_AddPreparedComponentTransactions', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'recipes') THEN
            CREATE SCHEMA recipes;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ai') THEN
            CREATE SCHEMA ai;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE TABLE recipes.recipes (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "CurrentRevisionNumber" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_recipes" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_recipes_Id_OwnerUserId" UNIQUE ("Id", "OwnerUserId"),
        CONSTRAINT ck_recipes_current_revision_number CHECK ("CurrentRevisionNumber" >= 1),
        CONSTRAINT "FK_recipes_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE TABLE ai.usage_ledger (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "Operation" character varying(100) NOT NULL,
        "Status" character varying(20) NOT NULL,
        "ReservedUnits" integer NOT NULL,
        "SettledUnits" integer,
        "PromptTokens" integer,
        "CompletionTokens" integer,
        "Provider" character varying(60),
        "Model" character varying(100),
        "CorrelationId" character varying(100) NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "ClosedAt" timestamp with time zone,
        CONSTRAINT "PK_usage_ledger" PRIMARY KEY ("Id"),
        CONSTRAINT ck_usage_ledger_reserved_units CHECK ("ReservedUnits" > 0),
        CONSTRAINT ck_usage_ledger_settlement CHECK (("Status" = 'Reserved' AND "SettledUnits" IS NULL AND "Provider" IS NULL AND "Model" IS NULL AND "ClosedAt" IS NULL) OR ("Status" = 'Settled' AND "SettledUnits" IS NOT NULL AND "SettledUnits" > 0 AND "Provider" IS NOT NULL AND "Model" IS NOT NULL AND "ClosedAt" IS NOT NULL) OR ("Status" = 'Released' AND "SettledUnits" IS NULL AND "ClosedAt" IS NOT NULL)),
        CONSTRAINT ck_usage_ledger_status CHECK ("Status" IN ('Reserved', 'Settled', 'Released')),
        CONSTRAINT "FK_usage_ledger_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE TABLE recipes.generation_sessions (
        "Id" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "IdempotencyKey" uuid NOT NULL,
        "ExecutionMode" character varying(20) NOT NULL,
        "Status" character varying(30) NOT NULL,
        "CandidatesSnapshotJson" jsonb,
        "SelectedCandidateId" character varying(64),
        "SelectedRecipeId" uuid,
        "SelectIdempotencyKey" uuid,
        "FailureReason" character varying(100),
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "ExpiresAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_generation_sessions" PRIMARY KEY ("Id"),
        CONSTRAINT "AK_generation_sessions_Id_OwnerUserId" UNIQUE ("Id", "OwnerUserId"),
        CONSTRAINT ck_generation_sessions_execution_mode CHECK ("ExecutionMode" IN ('cook_now')),
        CONSTRAINT ck_generation_sessions_status CHECK ("Status" IN ('AwaitingCandidates', 'CandidatesReady', 'Expanding', 'Selected', 'Failed')),
        CONSTRAINT "FK_generation_sessions_recipes_SelectedRecipeId_OwnerUserId" FOREIGN KEY ("SelectedRecipeId", "OwnerUserId") REFERENCES recipes.recipes ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_generation_sessions_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE TABLE recipes.recipe_revisions (
        "Id" uuid NOT NULL,
        "RecipeId" uuid NOT NULL,
        "OwnerUserId" uuid NOT NULL,
        "RevisionNumber" integer NOT NULL,
        "Name" character varying(80) NOT NULL,
        "MealTypesJson" jsonb NOT NULL,
        "Servings" integer NOT NULL,
        "NormalizedRecipeJson" jsonb NOT NULL,
        "ThumbnailVisualJson" jsonb NOT NULL,
        "SourceCandidateId" character varying(64) NOT NULL,
        "SourceGenerationSessionId" uuid NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_recipe_revisions" PRIMARY KEY ("Id"),
        CONSTRAINT ck_recipe_revisions_name CHECK (length(btrim("Name")) > 0),
        CONSTRAINT ck_recipe_revisions_revision_number CHECK ("RevisionNumber" >= 1),
        CONSTRAINT ck_recipe_revisions_servings CHECK ("Servings" BETWEEN 1 AND 24),
        CONSTRAINT ck_recipe_revisions_source_candidate_id CHECK (length(btrim("SourceCandidateId")) > 0),
        CONSTRAINT "FK_recipe_revisions_recipes_RecipeId_OwnerUserId" FOREIGN KEY ("RecipeId", "OwnerUserId") REFERENCES recipes.recipes ("Id", "OwnerUserId") ON DELETE RESTRICT,
        CONSTRAINT "FK_recipe_revisions_users_OwnerUserId" FOREIGN KEY ("OwnerUserId") REFERENCES identity.users ("Id") ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_generation_sessions_OwnerUserId_CreatedAt" ON recipes.generation_sessions ("OwnerUserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE UNIQUE INDEX "IX_generation_sessions_OwnerUserId_IdempotencyKey" ON recipes.generation_sessions ("OwnerUserId", "IdempotencyKey");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE UNIQUE INDEX "IX_generation_sessions_OwnerUserId_SelectIdempotencyKey" ON recipes.generation_sessions ("OwnerUserId", "SelectIdempotencyKey") WHERE "SelectIdempotencyKey" IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_generation_sessions_OwnerUserId_UpdatedAt_Id" ON recipes.generation_sessions ("OwnerUserId", "UpdatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_generation_sessions_SelectedRecipeId_OwnerUserId" ON recipes.generation_sessions ("SelectedRecipeId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_recipe_revisions_OwnerUserId_RecipeId_RevisionNumber" ON recipes.recipe_revisions ("OwnerUserId", "RecipeId", "RevisionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_recipe_revisions_RecipeId_OwnerUserId" ON recipes.recipe_revisions ("RecipeId", "OwnerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE UNIQUE INDEX "IX_recipe_revisions_RecipeId_RevisionNumber" ON recipes.recipe_revisions ("RecipeId", "RevisionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_recipes_OwnerUserId_CreatedAt_Id" ON recipes.recipes ("OwnerUserId", "CreatedAt", "Id");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_usage_ledger_CreatedAt" ON ai.usage_ledger ("CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_usage_ledger_OwnerUserId_CreatedAt" ON ai.usage_ledger ("OwnerUserId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE INDEX "IX_usage_ledger_OwnerUserId_Status" ON ai.usage_ledger ("OwnerUserId", "Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    CREATE TRIGGER recipe_revisions_are_append_only
    BEFORE UPDATE OR DELETE ON recipes.recipe_revisions
    FOR EACH ROW EXECUTE FUNCTION platform.reject_immutable_history_mutation();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260806011628_AddRecipeAiGatewaySlice') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260806011628_AddRecipeAiGatewaySlice', '10.0.4');
    END IF;
END $EF$;
COMMIT;

