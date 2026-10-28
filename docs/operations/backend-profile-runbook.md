# Backend Profile Runbook

- **Status:** Active
- **Applies to:** Cocinaris API profile v1 contract, PostgreSQL `profiles` schema, and session-safe projections
- **Related plan:** PLAN-0012

## Scope

This runbook documents the authenticated profile, household, preferences, equipment, and completeness endpoints delivered by `Cocinaris.Modules.Profiles`.

## Endpoints

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/v1/profile` | Progressive profile scaffold or persisted profile |
| PUT/PATCH | `/api/v1/profile` | Durable mutations; first create without `If-Match` |
| GET/PUT | `/api/v1/profile/preferences` | Explicit preference/restriction commands only |
| GET/PUT | `/api/v1/profile/equipment` | Equipment replace |
| GET | `/api/v1/profile/completeness` | Non-blocking completeness summary |
| GET | `/api/v1/session` | Safe projection: display name, language, timezone, measurement system, profile existence/completeness, adult declaration |

Sensitive restrictions, allergies, private notes, and full profile payloads are never returned from `/api/v1/session`.

When `profileExists` is false, GET `/api/v1/profile` returns `version`, `createdAt`, and `updatedAt` as JSON `null` and omits `ETag`. Those timestamps are never synthesized for a missing profile.

## Local validation

```bash
dotnet build apps/backend/Cocinaris.slnx -c Release
dotnet test apps/backend/Cocinaris.slnx -c Release
dotnet ef database update \
  --project apps/backend/src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj \
  --startup-project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj
```

After the API is running with a trusted development certificate or CI loopback URL:

```bash
COCINARIS_OPENAPI_URL=http://127.0.0.1:7080/openapi/v1.json bash scripts/backend/export-openapi.sh
bash scripts/backend/lint-openapi.sh
git diff --exit-code -- packages/contracts/openapi/cocinaris-v1.json
```

## Privacy and audit

Profile change history records section names and field codes only. Allergy and medical-restriction history uses generic markers derived from the validated preference category enum (never the raw request casing, stable code, or note). Non-sensitive preference history uses `CanonicalCategory:CanonicalStableCode`. Equipment history uses trimmed canonical stable codes only.

Export and deletion projection hooks are module-owned and prepared for future Privacy workflows without storing sensitive payloads in audit metadata.

## Migrations

### `20260731185224_InitialProfilesSlice`

Creates the `profiles` schema and baseline tables. Apply before serving profile routes against an existing database.

### `20260801070903_EnforceUniqueProfileEquipmentStableCode`

Converts `IX_equipment_entries_OwnerUserId_StableCode` into a unique index on `(OwnerUserId, StableCode)`.

This migration does **not** delete, merge, or rewrite duplicate equipment rows. Environments that already contain duplicates must remediate data before apply.

#### Preflight

Run against the target database before deployment:

```sql
SELECT
    "OwnerUserId",
    "StableCode",
    COUNT(*) AS duplicate_count
FROM profiles.equipment_entries
GROUP BY "OwnerUserId", "StableCode"
HAVING COUNT(*) > 1;
```

| Preflight result | Action |
|---|---|
| Empty result set | Safe to apply the migration |
| One or more rows | **Block deployment.** Do not apply until duplicates are resolved manually |

#### Failure behavior

- If duplicates remain, PostgreSQL rejects unique index creation and the migration fails on purpose.
- No rows are deleted or merged automatically by the migration.
- Take a backup before any manual remediation.
- Analyze each duplicate group individually (which row is active, which soft-removed, which custom metadata to keep).
- Keep profile equipment writes unavailable during remediation when necessary to prevent new duplicates.
- Rollback of the migration does not restore rows that operators deleted manually during cleanup.

After remediation, re-run the preflight query (must be empty), then apply the migration.

## Operations

- Profile mutations require CSRF on PUT/PATCH routes.
- Updates after the first create require a current `If-Match` ETag.
- Owner isolation is enforced at application and PostgreSQL foreign-key boundaries.
- Migrations are forward-only; apply `InitialProfilesSlice`, then `EnforceUniqueProfileEquipmentStableCode`, before serving profile routes that assume unique equipment identity.
