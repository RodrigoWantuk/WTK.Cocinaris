# PLAN-0030: Rename the Product and Technical Identifiers to Cocinaris

- **Status:** In Progress
- **Type:** Operations
- **Priority:** Critical
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0030-rename-cocinaris` (stacked on `agent/plan-0029-green-baseline`)
- **Pull request:** Not opened
- **Related ADRs:** ADR-0002, ADR-0004, ADR-0006, ADR-0007
- **Dependencies:** PLAN-0029; owner decision D-1 (resolved 2026-10-28: "Cocinaris in everything")

## Objective

Replace the provisional name KitchenFlow with **Cocinaris** across product copy, living documentation, code namespaces, assemblies, solution and project files, packages, contracts, configuration keys, cookies, Data Protection discriminators, metrics, scripts, CI workflows and development fixtures. Record the operational consequences for existing deployments.

## Scope

### Included

- .NET: `KitchenFlow.*` projects, folders, assemblies, namespaces, solution (`KitchenFlow.slnx` → `Cocinaris.slnx`), type names containing `KitchenFlow`, EF migration designer/snapshot type strings, package lock files.
- Runtime identifiers:
  - environment variables `KITCHENFLOW_*` → `COCINARIS_*`;
  - connection-string name `KitchenFlow` → `Cocinaris`;
  - cookies `__Host-kitchenflow-*` → `__Host-cocinaris-*`;
  - Data Protection application name, and the cursor purpose `KitchenFlow.Inventory.LotCursor.v1` → `Cocinaris.Inventory.LotCursor.v1`;
  - OpenAPI security scheme and descriptions;
  - meters and metric names (`kitchenflow.*` → `cocinaris.*`).
- Contracts: `packages/contracts/openapi/kitchenflow-v1.json` → `cocinaris-v1.json`; `@kitchenflow/api-client` → `@cocinaris/api-client`; client factory names; regenerated client and frontend mirror; AI contract fixtures and prompt text.
- Frontend: UI copy in all three locales, storage keys, test IDs and fixtures containing the name.
- Development fixtures: Keycloak realm/client names, compose database/user/volume names, CI workflows, scripts.
- Living documentation: README, AGENTS, CONTRIBUTING, SECURITY, LICENSE notice, `docs/` canonical documents (product, domain, architecture/ADRs, ai, security, testing, operations, development, discovery), app and package READMEs, the plan registry header text, active plans.

### Excluded (historical records)

- Completed and superseded plans PLAN-0001 to PLAN-0027 and their amendments, and `docs/evidence/**`. They are immutable execution history. Their commands were correct at the commits they reference. A naming note in `docs/README.md` explains the rename.
- Registry rows of completed plans, which keep their original titles as names of historical documents.
- The database schema names (`identity`, `inventory`, `platform`, `profiles`) and existing migration IDs, which never contained the old name. The deployed database is already named `cocinaris`.

## Operational consequences (deployments)

1. Rename `KITCHENFLOW_*` keys to `COCINARIS_*` in the deployment environment file before starting the new build.
2. The service entry point changes from `KitchenFlow.Api.dll` to `Cocinaris.Api.dll`.
3. The cookie names and the Data Protection application name change, so every existing session ends once and users sign in again. Opaque ETags and pagination cursors issued before the deploy are rejected, and clients refetch, which existing conflict handling already covers.
4. The browser locale preference key changes, so users see the default locale once.
5. Metric names change, so dashboards or alerts on `kitchenflow.*` must be renamed.

These steps are executed and documented by PLAN-0031 (deployment runbook).

## Acceptance criteria

- [ ] `git grep -i kitchenflow` returns matches only in excluded historical paths and in explicit rename notes.
- [ ] Backend restore/build/test pass, including integration tests against the renamed development fixtures.
- [ ] OpenAPI export, lint and drift checks pass under the new file name; the regenerated client compiles; the frontend mirror is in sync.
- [ ] Frontend gates pass (typecheck, lint, Prettier, guards, Jest, production build, bundle inspection, `audit:policy`).
- [ ] AI contract validators pass.
- [ ] Operational consequences are documented for deployments.

## Execution state

- **Current run delivery target:** Complete rename with validation in one run.
- **Current checkpoint:** Plan created; rename not started.
- **Exact next action:** Execute the scripted path and content rename, then rebuild and validate.
- **Blockers:** None.
- **Working tree state:** Plan files uncommitted at creation.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner decision D-1. Scope and historical exclusions defined.
