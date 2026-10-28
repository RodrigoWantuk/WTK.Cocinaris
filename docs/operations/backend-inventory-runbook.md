# Backend Inventory Slice Runbook

- **Status:** Accepted for PLAN-0003
- **Applies to:** Cocinaris API, PostgreSQL 18, Keycloak 26, and the inventory v1 contract
- **Last updated:** 2026-07-31
- **Related plans:** PLAN-0002, PLAN-0003, PLAN-0005

## Purpose

This runbook is the executable developer and operator workflow for the authenticated inventory
slice. Run commands from the repository root unless a section says otherwise. The local topology
contains only PostgreSQL and Keycloak; RabbitMQ, Redis, object storage, SMTP, Python, Kubernetes,
and AI providers are not dependencies of this slice.

The committed development fixture is synthetic and local-only. Production and shared environments
must use secrets supplied by their approved secret manager. Never copy the fixture password,
database connection, OIDC client secret, cookies, tokens, or browser callback parameters into
issues, pull requests, logs, or validation evidence.

## Prerequisites

Use a supported Linux host, Windows with Docker Desktop Linux containers, or WSL2 as defined in
[`../development/environment.md`](../development/environment.md). Required host tools are:

- Git;
- Docker Engine or Docker Desktop with the Compose plugin;
- .NET 10 SDK selected through `global.json`;
- Node.js 24 LTS for OpenAPI lint and the Keycloak browser smoke;
- `curl`, `jq`, OpenSSL, and a current Chromium browser.

Verify the host before changing or validating the backend:

```text
git --version
docker version
docker compose version
dotnet --info
dotnet --list-sdks
node --version
npm --version
curl --version
jq --version
openssl version
```

The standard local ports are PostgreSQL `5432`, Keycloak `8080`, API HTTP `7080`, and API HTTPS
`7443`, all on loopback. Resolve port conflicts without exposing the services on public network
interfaces.

## Local secrets and dependency startup

Copy the versioned synthetic fixture to the ignored Compose environment file:

```text
cp .env.example .env
docker compose -f infrastructure/compose/compose.dev.yml config --quiet
docker compose -f infrastructure/compose/compose.dev.yml up -d postgres keycloak
docker compose -f infrastructure/compose/compose.dev.yml ps
docker compose -f infrastructure/compose/compose.dev.yml exec -T postgres pg_isready -U cocinaris_dev -d cocinaris
curl --fail --silent http://127.0.0.1:8080/realms/cocinaris/.well-known/openid-configuration > /dev/null
```

`.env` is ignored. Replace its synthetic values for any shared environment and inject production
values through the deployment secret manager. The browser receives none of these values.

The API reads:

| Setting | Purpose | Production rule |
|---|---|---|
| `COCINARIS_DB_CONNECTION` | PostgreSQL connection | Required, non-placeholder secret |
| `COCINARIS_OIDC_AUTHORITY` | Standard OIDC issuer | Required HTTPS authority |
| `COCINARIS_OIDC_CLIENT_ID` | Confidential backend client | Required, non-placeholder |
| `COCINARIS_OIDC_CLIENT_SECRET` | OIDC client authentication | Required secret |
| `COCINARIS_SESSION_KEYRING_PATH` | persistent Data Protection key ring | Required absolute, protected, shared path |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | optional standard telemetry export | Valid absolute OTLP endpoint when set |

Source those values from an ignored local environment file or secret store before starting the API.
Do not paste secrets into a recorded command. `appsettings.Development.json` contains only
non-secret local authority/client metadata. No production credential has a committed default.

## Restore, format, build, test, and audit

```text
dotnet tool restore
dotnet restore apps/backend/Cocinaris.slnx --locked-mode
dotnet format apps/backend/Cocinaris.slnx --verify-no-changes --no-restore
dotnet build apps/backend/Cocinaris.slnx -c Release --no-restore
dotnet test apps/backend/Cocinaris.slnx -c Release --no-build
bash scripts/backend/check-dotnet-vulnerabilities.sh /tmp/cocinaris-vulnerabilities.json
```

The vulnerability policy is fail-closed for every direct or transitive package vulnerability
reported by the selected .NET SDK, independent of advisory severity. The script writes
machine-readable JSON and exits nonzero when any package vulnerability exists. The Backend workflow
runs the same script and retains its JSON with the test and migration evidence. Gitleaks runs as a
separate required job. A false positive or an advisory without an available patch is not silently
ignored: document the package, advisory, exposure analysis, compensating control, owner, and expiry
in a dedicated approved plan before changing the gate.

## Migrations

### Create and review

Create a migration only for an approved model change:

```text
dotnet tool restore
dotnet ef migrations add <EnglishMigrationName> \
  --project apps/backend/src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj \
  --startup-project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj
git diff -- apps/backend/src/Cocinaris.Infrastructure/Persistence/Migrations
```

Review generated SQL semantics, owner-consistency constraints, history immutability, locks, data
conversion, compatibility, and privacy before committing.

### Apply to an empty database or upgrade

With the local environment values loaded and PostgreSQL ready:

```text
dotnet ef database update \
  --project apps/backend/src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj \
  --startup-project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj \
  --configuration Release
```

The integration suite creates real PostgreSQL databases, applies an empty-to-latest migration, and
upgrades representative data from every committed prior migration to latest. It also verifies the
resulting constraints and append-only history behavior.

### Generate and verify the deployment artifact

```text
bash scripts/backend/generate-migration-script.sh /tmp/cocinaris-migrations.sql
test -s /tmp/cocinaris-migrations.sql
```

The script is idempotent SQL generated by the repository-pinned EF tool. CI applies the exact
artifact twice to a clean PostgreSQL database and verifies the migration history count. The retained
workflow artifact is named `backend-evidence-<candidate-sha>` and includes the SQL, vulnerability
JSON, and TRX test results for 30 days.

Operators must apply the artifact with `ON_ERROR_STOP` or the equivalent fail-fast database option,
capture the deployment SHA, preserve the output privately, and confirm `/health/ready` after
application rollout. Never log connection strings or migration environment secrets.

### Rollback and forward repair

Application rollback does not imply database rollback. These migrations add authoritative rows,
constraints, functions, and append-only triggers; blindly migrating down can destroy data or remove
safety guarantees.

The default recovery is forward repair:

1. stop or restrict writes when integrity or compatibility is uncertain;
2. capture a verified backup and the failed application/migration SHA;
3. identify whether the migration committed and inspect `__EFMigrationsHistory`;
4. produce a reviewed corrective migration that is safe for both partially and fully upgraded
   environments;
5. rehearse it against a restored copy with representative data;
6. deploy the compatible application and corrective migration;
7. verify readiness, constraints, owner isolation, immutable history, and telemetry;
8. record the incident and evidence without private inventory data.

A destructive down migration requires an explicit incident decision, a verified restore point, and
documented data-loss implications. Never edit migration history manually to make a deployment look
successful.

### Prepared-component migration checks

The prepared-component migration adds immutable preparation batches, including the original declared yield (measured value/unit or qualitative availability), input/output provenance, and per-output prepared metadata under the `inventory` schema. The declared-yield constraint must be fail-closed (`... IS TRUE`) so SQL NULL semantics cannot accept an incomplete mode. Before deployment, verify the migration applies from the prior inventory schema, its idempotent script applies twice to an empty database, invalid direct declared-yield probes fail with PostgreSQL `23514`, valid measured and qualitative probes succeed, and the preparation append-only triggers are present. Provenance inspection returns at most fifty batches in each direction; its separate truncation flag means an older relationship exists and must not be mistaken for complete history. Do not use a down migration to remove committed preparation provenance; stop affected writes, preserve a backup, and deploy a reviewed forward-repair migration if integrity is uncertain. If this still-unmerged migration must be revised before its first production application, regenerate the idempotent script and rerun empty/upgrade checks; after production application, use a forward repair rather than editing migration history.

## HTTPS API and session key ring

The backend cookie uses `Secure`, `HttpOnly`, `SameSite=Lax`, and a `__Host-` name. Local OIDC and
CSRF testing therefore require trusted HTTPS:

```text
dotnet dev-certs https --check --trust
dotnet run --project apps/backend/src/Cocinaris.Api/Cocinaris.Api.csproj \
  --configuration Release --no-build --launch-profile https
```

Linux/browser trust stores may require importing the development certificate into the operating
system and browser stores. When a browser refuses a self-signed ASP.NET development leaf as a trust
anchor, use a developer-local CA with a `localhost`/loopback server certificate, import only that CA
into the operating-system/browser trust stores, and configure Kestrel's certificate path and key
path through ignored local settings. Never commit the CA key, server key, or exported certificate
bundle. The Linux smoke selects the operating-system trust store; it does not disable certificate
validation. Do not disable validation in application code, OIDC, normal smoke or OpenAPI commands,
or CI. The smoke and OpenAPI scripts have explicit untrusted-certificate switches only for
diagnosis; a run using either switch is not accepted evidence.

Data Protection keys make session cookies, cursors, and ETags opaque and tamper-evident. Every API
replica in one environment must share the same durable protected key ring and application name.
Protect the path with least-privilege filesystem or managed-key permissions, back it up consistently,
and rotate keys through Data Protection. Deleting or splitting the ring invalidates sessions and can
make issued cursors/ETags unusable; it does not alter authoritative inventory data.

## Health and identity-provider monitoring

```text
curl --fail --silent https://localhost:7443/health/live
curl --fail --silent https://localhost:7443/health/ready
curl --fail --silent http://127.0.0.1:8080/realms/cocinaris/.well-known/openid-configuration > /dev/null
```

- `/health/live` proves the request pipeline is alive.
- `/health/ready` proves validated runtime configuration and PostgreSQL connectivity.
- readiness intentionally does not fetch OIDC metadata, so monitor Keycloak discovery and login
  independently. This avoids cascading API unready status during an identity-provider outage while
  still surfacing authentication failure through its own telemetry.

Health failures use privacy-safe Problem Details. Do not add raw exception, connection, token,
cookie, request-body, user, note, or product values to probes or logs.

## OpenAPI generation and drift

Start the API, then run:

```text
bash scripts/backend/export-openapi.sh
bash scripts/backend/check-openapi.sh
bash scripts/backend/lint-openapi.sh
git diff --exit-code -- packages/contracts/openapi/cocinaris-v1.json
```

Export intentionally rewrites the checked-in snapshot from the running API. Review the diff before
committing. `check-openapi.sh` compares runtime output with the snapshot, Redocly validates OpenAPI
3.1, and integration contract tests verify route security, headers, status codes, schemas, examples,
and Problem Details. PLAN-0004 may generate a frontend client only from the final stable PLAN-0003
candidate SHA.

## Accepted Keycloak browser smoke

Use the two deterministic development users from the realm fixture. Supply their synthetic
passwords through a private process environment without placing them in shell history, then run:

```text
node scripts/backend/smoke-keycloak.mjs
```

The script creates isolated Chromium profiles and proves Authorization Code Flow with PKCE,
backend-managed session cookies, absence of provider tokens in the session response, CSRF-protected
lot creation, two distinct internal users, owner access, and `404` for cross-user access. Its success
line contains no credentials, cookies, tokens, identifiers, request bodies, or product names.

## Troubleshooting

### PostgreSQL is not ready

Inspect `docker compose ... ps` and the PostgreSQL logs. Confirm Docker resources, port `5432`, the
synthetic local environment, and volume ownership. Do not delete the named volume until deciding
whether its data is required for migration evidence or recovery.

### Keycloak discovery or redirect fails

Confirm Keycloak health, realm import, system clock, authority hostname, confidential client
settings, exact `https://localhost:7443` redirect URI, and trusted local certificate. Do not weaken
PKCE, state, nonce, correlation cookies, Secure cookies, or redirect validation.

### API readiness is 503

Read the safe `errorCode`, then check validated database, OIDC, key-ring, session, and idempotency
configuration and PostgreSQL connectivity. OIDC reachability is diagnosed separately.

### A stale write returns 412

Fetch the current lot representation and use its new opaque ETag. Do not synthesize or parse ETags.
Missing `If-Match` returns 428. A token for another lot is not accepted.

### An idempotency request conflicts

Retry an identical completed request with the same UUID to receive the stored semantic response.
Reuse with a different canonical payload returns 409. A defensive in-progress response is retryable;
do not generate duplicate business commands to bypass it.

### OpenAPI drift is reported

Start the candidate API with the intended configuration, export, inspect the semantic diff, rerun
contract tests and lint, and commit the generated snapshot with the implementation. Never hand-edit
the generated snapshot to conceal runtime drift.

## Known limitations

- The first slice has no idempotency cleanup worker. The validated retention option defines the
  future cleanup boundary; completed records remain PostgreSQL-authoritative until that operation is
  implemented by a later approved plan.
- Readiness validates OIDC configuration but not provider reachability; external monitoring is
  required.
- Local Keycloak uses development mode and loopback HTTP. Production requires hardened Keycloak
  operation and TLS.
- OpenTelemetry export is optional. When unset, the slice has no collector dependency.
- ETag ciphertext can change after process restart while the persistent key ring continues to
  validate previously issued tokens for the same lot/version.
- Database rollback is not automated; reviewed forward repair is the default recovery policy.
