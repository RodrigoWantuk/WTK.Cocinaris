# PLAN-0033: Rename the Product and Technical Identifiers to Cocinaris

- **Status:** Completed
- **Type:** Operations
- **Priority:** Critical
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** [PR #46](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/46), merged by the owner at `3d45bee`
- **Numbering:** Created as PLAN-0030. Renumbered to PLAN-0033 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Related ADRs:** ADR-0002, ADR-0004, ADR-0006, ADR-0007
- **Dependencies:** PLAN-0032; owner decision D-1 (resolved 2026-10-28: "Cocinaris in everything")

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

These steps are executed and documented by PLAN-0034 (deployment runbook).

## Acceptance criteria

- [x] `git grep -i kitchenflow` returns matches only in excluded historical paths and in explicit rename notes.
- [x] Backend restore/build/test pass, including integration tests against the renamed development fixtures.
- [x] OpenAPI export, lint and drift checks pass under the new file name; the regenerated client compiles; the frontend mirror is in sync.
- [x] Frontend gates pass (typecheck, lint, Prettier, guards, Jest, production build, bundle inspection, `audit:policy`).
- [x] AI contract validators pass.
- [ ] Operational consequences are documented for deployments.

## Execution state

- **Current run delivery target:** Complete rename with validation in one run.
- **Current checkpoint:** Rename implemented and validated on the owner host. The deployment-side steps (environment keys, service entry point) are documented and executed by PLAN-0034 after the owner merges.
- **Last completed step:** Full CI-equivalent validation on the host: NuGet restore in locked mode; `dotnet format --verify-no-changes`; Release build with 0 warnings and 0 errors; architecture 14/14, unit 52/52 and integration 175/175 tests with the renamed OIDC development fixture; empty-database migration and upgrade; idempotent migration script applied twice (9 history rows); OpenAPI export identical to the snapshot, plus check and Redocly lint. API client generate and drift check pass. Frontend typecheck, lint, Prettier, guards, Jest 45/369, production build, bundle inspection and `audit:policy` pass on Node.js 24.22.1. AI recipe contract tests and drift check pass.
- **Exact next action:** None. Follow-up work is tracked in the PLAN-0031 coverage review roadmap.
- **Blockers:** None.
- **Partially modified areas:** None.
- **Documentation delivered:** Every living document renamed; naming history note in `docs/README.md`.
- **Known failures or limitations:** Completed plans PLAN-0001–PLAN-0027, PLAN-0031/0029 narrative and `docs/evidence/` intentionally keep the historical name.
- **Working tree state:** Committed on `agent/plan-0031-0036-foundation-stack`.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner decision D-1. Scope and historical exclusions defined.

### 2026-10-28T15:40:00Z — Claude Code

- **Checkpoint:** Rename implemented and validated.
- **Changes:** Project folders, `.csproj`, solution, Keycloak realm file and OpenAPI snapshot renamed. Content of 212 living files rewritten (`KitchenFlow`/`kitchenflow`/`KITCHENFLOW` → `Cocinaris`/`cocinaris`/`COCINARIS`). `packages/api-client/src/index.ts` reformatted (shorter identifier). Naming history note added.
- **Validation:** See execution state. All gates green on the owner host.
- **Next action:** PR CI, owner merge, deployment steps in PLAN-0034.

### 2026-10-28T22:20:00Z — Claude Code (PR #44 integration)

- The owner merged PR #44, which had already published PLAN-0028–PLAN-0030. This plan was renumbered from PLAN-0030 to PLAN-0033; `main` was merged into `agent/plan-0031-0036-foundation-stack`.
- Applied the rename to the code merged through PR #44: `KitchenFlow.Modules.Ai` and `KitchenFlow.Modules.Recipes` became `Cocinaris.Modules.*`; namespaces, the `AddCocinarisRecipeAi` registration, the `cocinaris-deepseek` HTTP client, the OpenAPI security scheme (`cocinarisSession`) and tag, the regenerated clients, the live smoke tool and `docs/ai/usage-and-cost-governance.md`. Removed `KitchenFlow.slnx` (the new projects were added to `Cocinaris.slnx`) and `artifacts/kitchenflow-migrations.sql`, a build output committed by PR #44 although `artifacts/` is ignored. `PLAN-0028` and its evidence are historical and keep the old name.
- **Validation on the merged tree:** backend format, build with warnings as errors, unit 76/76, architecture 14/14, integration 178/178 (OIDC fixture); migrations applied twice idempotently (10 rows); OpenAPI export matches and lints; API client drift check; frontend lint, typecheck, guards, Vitest 50 files / 410 tests, production build, build-mode guard, audit policy (0 allowlisted).
- **Not passed locally:** the prototype browser smoke timed out on a page reload after 6 passing journeys, twice, while another project on the host kept the load average near 10 on 2 CPUs. The pre-merge commit `ff5f715` also fails with a navigation timeout under the same load, so this is not a regression of the merge. CI runs the smoke on the PR head.

### 2026-10-28T23:05:00Z — Claude Code

- The owner merged PR #46 (`3d45bee`, 2026-10-28T22:41Z). GitHub Actions did not run on it because the owner account is locked for billing; validation is the owner-host run recorded above. Status set to Completed.
