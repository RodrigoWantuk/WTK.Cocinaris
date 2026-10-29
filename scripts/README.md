# Repository Scripts

This directory will contain repeatable repository automation that is not owned by a specific application.

Candidate responsibilities include:

- local environment setup;
- formatting and validation entry points;
- contract generation and compatibility checks;
- documentation validation;
- test orchestration;
- database and fixture utilities;
- release and deployment support.

## Rules

- Scripts must be documented, idempotent where practical, and safe by default.
- Destructive operations must require explicit confirmation or an explicit noninteractive flag.
- Scripts must return meaningful exit codes and avoid hiding failures.
- Secrets must be provided through approved environment or secret-management mechanisms.
- CI and local development should call the same underlying commands when practical.
- Platform-specific scripts must have a documented reason and supported environment.

## Backend validation scripts

- `backend/export-openapi.sh` exports the running API contract to the checked-in OpenAPI snapshot.
- `backend/check-dotnet-vulnerabilities.sh` writes the .NET direct/transitive vulnerability result
  as JSON and fails when any advisory is reported. CI retains this evidence for the candidate SHA.
- `backend/generate-migration-script.sh` restores the repository tool manifest and creates a
  nonempty idempotent PostgreSQL migration artifact from the complete EF Core migration chain.
- `backend/check-openapi.sh` verifies that a running API has not drifted from that snapshot.
- `backend/lint-openapi.sh` validates the checked-in OpenAPI 3.1 snapshot with the pinned Redocly configuration.
- `backend/smoke-keycloak.mjs` drives the real Authorization Code plus PKCE flow in two isolated Chromium profiles. It verifies the backend-managed cookie session, absence of provider tokens from `/api/v1/session`, CSRF-protected lot creation, and a `404` cross-user ownership boundary. Start the local Compose services, apply migrations, and start the HTTPS API before running it. The two development-fixture passwords are supplied only through `COCINARIS_SMOKE_PASSWORD_A` and `COCINARIS_SMOKE_PASSWORD_B`; never put them in a command history, source file, CI log, or committed environment file.

The Keycloak smoke expects a browser that trusts the local HTTPS certificate. On Linux it selects
the operating-system trust store so an explicitly installed development CA is honored; this does
not bypass validation. `COCINARIS_SMOKE_ALLOW_UNTRUSTED_LOCAL_CERTIFICATE=1` is available only to
diagnose a local trust bootstrap problem and is not accepted validation evidence or a CI setting.

The OpenAPI scripts likewise require a trusted local certificate by default.
`COCINARIS_OPENAPI_ALLOW_UNTRUSTED_LOCAL_CERTIFICATE=1` is a diagnostic-only local escape hatch;
CI uses its loopback HTTP endpoint and accepted local evidence does not use this setting.

See the [`backend inventory runbook`](../docs/operations/backend-inventory-runbook.md) for exact
commands, deployment evidence, forward-repair policy, and troubleshooting.

## Host-local gates and proxy replica (PLAN-0053)

- `ci/run-local-gates.sh` runs the backend, contract, frontend, browser-smoke and secret-scan gates on a clean worktree of a commit. It replaces the GitHub Actions workflows, which do not run for this repository.
- `e2e/proxy-replica.sh start|stop` runs the API behind a separate Caddy that uses the deployment template, on `https://localhost:7443` with the local OIDC fixture.
- `e2e/profile-journey.mjs` signs in through the replica and saves on every profile page. It catches defects that only appear behind the deployment proxy.
- `e2e/cooking-journey.mjs` (PLAN-0042) signs in through the replica, creates a pantry lot, seeds one saved recipe in the development database (the replica does not call the AI provider), cooks it in the browser, confirms with leftovers, and checks the pantry and the "Hoje" resume card. Same password variable as the profile journey. Each run uses a unique product and recipe name, so lots from earlier runs do not affect its checks.
- `e2e/library-journey.mjs` (PLAN-0038) signs in through the replica and, in the browser:
  - writes a recipe in the editor;
  - favorites it;
  - cooks it;
  - saves the cooking adjustments as version 2 from the finished screen;
  - creates a variation and archives it.

  It checks the history and the Favoritas and Arquivadas filters. It does not need the AI provider. Same password variable as the profile journey. Apply migrations to the development database first.
- `e2e/planning-journey.mjs` (PLAN-0029) seeds a pantry lot and a recipe through the API, then in the browser:
  - creates a menu and plans the recipe for today and tomorrow;
  - accepts the menu and finds today's meal on "Hoje";
  - checks the shopping list (700 g to buy) and records a purchase;
  - cooks the meal from "Hoje" and checks that it is marked cooked and left "Hoje".

  Names are unique per run. Same password variable; apply migrations to the development database first.
- `e2e/reservations-journey.mjs` (PLAN-0029 Phase 3) seeds a 500 g lot, two recipes of 300 g each, and an accepted menu with both. It then checks:
  - the meals' readiness;
  - the lot page's reservations;
  - a 100 g protection widening the shortfall;
  - the cooking review warning that names the planned meals;
  - that nothing changed physically.

  Same password variable.

## Operations (PLAN-0040)

- `ops/ai-usage-report.sh [ceiling-usd]` runs on the owner host as root and reads only the AI usage ledger. It reports:
  - this UTC month's provider spend against the monthly ceiling;
  - calls by operation, model, and outcome, with attempts and cost;
  - provider latency p50/p95/max;
  - answers accepted with quality flags.

  It prints no prompts, outputs, users, or secrets.
