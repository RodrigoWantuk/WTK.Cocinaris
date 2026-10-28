# PLAN-0032: Restore the Green Baseline and Reconcile Documentation Drift

- **Status:** Validating
- **Type:** Operations
- **Priority:** Critical
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28T15:10:00Z
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** Draft PR from `agent/plan-0031-0036-foundation-stack` (one consolidated PR for PLAN-0031–PLAN-0036)
- **Numbering:** Created as PLAN-0029. Renumbered to PLAN-0032 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Related issues:** None
- **Related ADRs:** ADR-0002, ADR-0007
- **Dependencies:** PLAN-0031 findings F-0028-01, F-0028-02, F-0028-03, F-0028-06; stakeholder decision D-1 (product name) for the naming portion only

## Objective

Make `main` buildable and CI-green again without weakening dependency, security or documentation gates, and make the canonical documentation agree with delivered repository state, so feature work (PLAN-0034 onward) starts from a trustworthy baseline.

## Context

On 2026-10-28, PLAN-0031 found that:

- backend restore fails with `NU1903` for `SSH.NET 2024.2.0`, which comes transitively from `Testcontainers.PostgreSql 4.8.1`. `SSH.NET 2026.0.0` and `Testcontainers.PostgreSql 4.16.0` are published;
- frontend `audit:policy` fails with 51 unapproved advisories. Ten declared runtime dependencies are never imported, and the `react-router` allowlist entry references an advisory ID that no longer matches;
- `packages/api-client` has 5 untriaged advisories;
- several plan headers, README status text and `AGENTS.md` disagree with delivered state.

## Scope

### Included

1. **Backend dependency repair.** Upgrade `Testcontainers.PostgreSql` to the latest 4.x, or pin `SSH.NET` to a patched version through central transitive pinning. Run `dotnet list package --vulnerable --include-transitive` and resolve any other findings.
2. **Frontend dependency hygiene.** Remove unused dependencies (`axios`, `zod`, `swr`, `dayjs`, `date-fns`, `lodash`, `recharts`, `framer-motion`, `@hookform/resolvers`, `cra-template`) after re-verifying that they have no import. Upgrade runtime packages with patched versions that are compatible with CRA (`nanoid`, `qs` resolutions, etc.).
3. **Allowlist triage.** Replace the stale `react-router` entry with the current advisory ID. Add time-boxed exceptions only for CRA build/dev-server toolchain advisories. Each entry needs `runtime_or_build`, exploitability, residual risk and `remove_by` ≤ 2027-01-31, with `follow_up_plan: PLAN-0033`. No runtime advisory may be allowlisted without a documented non-exploitability argument.
4. **API client audit.** Triage and fix `packages/api-client` advisories.
5. **Documentation drift (F-0028-03).**
   - `README.md` project-status section: reflect PLAN-0011, PLAN-0020 and PLAN-0023 delivery.
   - Plan headers: PLAN-0014 → `Completed`, PLAN-0017 → `Completed` (with a pointer to the completion handoff), and the PLAN-0008 amendments aligned with the registry. Correct by appending, not by rewriting history.
   - PLAN-0023: remove the duplicated execution-state lines, record completion after the PLAN-0027 Pass, and fill or remove the empty excluded-scope heading.
   - PLAN-0021: make the header status agree with the body. Record that the deterministic inventory/profile tiers may start before the menu contract.
   - `AGENTS.md` §1: align the frontend platform wording with ADR-0007.
   - Product name: apply D-1 to user-facing documentation only. Technical identifiers stay unchanged in this plan.
6. **Environment documentation (F-0028-06).** Document enabling `yarn` through Corepack and the `/opt/dotnet` PATH case in `docs/development/environment.md`.

### Excluded

- the CRA → Vite migration (PLAN-0033);
- technical namespace rename (PLAN-0046, conditional on D-1);
- feature work.

## Requirements and constraints

- Do not disable `NuGetAudit`, lower `TreatWarningsAsErrors`, or bypass `audit:policy`.
- Do not allowlist an advisory to avoid a compatible upgrade.
- Keep `yarn.lock` and `packages.lock`/central versions reproducible. Use pinned exact versions as the repository already does.
- Follow lean validation (PLAN-0007 amendment). This plan touches only manifests and documentation.

## Substantial run delivery target

- **Target outcome:** One PR that passes Backend, Frontend and PLAN-0005 workflows on its head and contains the corrected documentation.
- **Minimum acceptable evidence:** local restore/build/test output for backend; lint/typecheck/test/build/audit:policy for frontend; CI run IDs on the PR.
- **Adjacent checkpoints:** If D-2 is approved during the run, register PLAN-0033 with a complete scope.
- **Valid early-stop conditions:** a dependency upgrade that breaks behavior and needs a code change outside this scope; an unresolved D-1.

## Execution phases

### Phase 1: Backend
Upgrade or pin, then `dotnet restore`/`build`/`test` (unit + architecture locally; integration on CI or a Docker host).

### Phase 2: Frontend and API client
Remove unused dependencies, upgrade, regenerate the lockfile, triage the allowlist, then run `yarn lint`, `typecheck`, `test`, `build:production`, all guards, `inspect:production-bundle` and `audit:policy`. Run `yarn audit` in `packages/api-client`.

### Phase 3: Documentation
Apply the F-0028-03 corrections, update the environment guide, then update this plan and the registry.

## Testing and validation plan

| Change | Validation |
|---|---|
| Backend package versions | Full backend build; unit, architecture and integration tests (integration on CI) |
| Frontend dependency removal/upgrade | Existing 369 Jest tests, production build, bundle inspection, guards, audit policy |
| Docs | Link check of edited files; registry/plan consistency |

## Cross-cutting impact

- **Security:** Restores enforcement of the dependency-audit gate. Removing unused `axios` also removes unused network code from the dependency graph.
- **Privacy, food safety, AI:** None.
- **Localization/accessibility:** None expected. The Jest suite covers regressions.
- **Operations:** CI is unblocked for all subsequent plans.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Testcontainers 4.16 changes APIs used by integration fixtures | Fall back to pinning `SSH.NET` only |
| A removed package is used dynamically (string import) | Re-grep including `craco.config.js`, `plugins/`, and `scripts/`, and confirm with the production build and bundle inspection |
| A runtime upgrade is CRA-incompatible | Document a narrow exception and move the upgrade to PLAN-0033 |

## Acceptance criteria

- [ ] `dotnet build KitchenFlow.slnx -c Release` passes with NuGet audit enabled.
- [ ] Frontend `audit:policy` passes. Every remaining exception is build-time or justified, and has `remove_by` and `follow_up_plan`.
- [ ] No unused runtime dependency remains in `apps/frontend/package.json`.
- [ ] `packages/api-client` advisories resolved or justified.
- [ ] All F-0028-03 drift items corrected.
- [ ] Backend, Frontend and PLAN-0005 workflows green on the PR head. Run IDs recorded in the PR.

## Execution state

- **Current run delivery target:** Phases 1–3 in one run.
- **Current checkpoint:** All three phases implemented and locally validated, except the product-name portion of Phase 3, which waits for PLAN-0031 decision D-1. Remaining validation is the CI run on the PR head.
- **Last completed step:** Phase 3. README project status reflects PLAN-0011/0020/0022/0023 delivery and links the PLAN-0031 roadmap. `AGENTS.md` §1 and §6 frontend wording is aligned with ADR-0007. PLAN-0014 and PLAN-0017 header statuses are reconciled with the registry. PLAN-0021 per-tier gating is clarified. PLAN-0023's stale execution state is replaced. `docs/development/environment.md` documents an SDK outside `PATH`, Yarn via Corepack, and the Node.js 20 vs 24 inconsistency.
- **Exact next action:** Final CI on the draft PR head from `agent/plan-0031-0036-foundation-stack`; owner merge.
- **Blockers:** None. Owner merge determines delivery state only.
- **Partially modified areas:** None.
- **Documentation delivered:** Frontend vulnerability/resolution triage; README; AGENTS.md; environment guide; PLAN-0014/0017/0021/0023 reconciliation entries; PLAN-0031 correction entry.
- **Validation performed:** Backend Release build with audit (0/0), vulnerable scan clean, architecture 14/14, unit 52/52. Frontend typecheck, lint, Prettier, four guards, Jest 45/369, production build, bundle inspection, `audit:policy` (empty allowlist), frozen-lockfile install. API client audit 0, generate + drift check, typecheck, format.
- **Known failures or limitations:** Backend integration tests and the PLAN-0005 workflow were not run locally (no Docker); CI on the PR head is required. `deepmerge` low advisory has no patch and is accepted by policy. The frontend CI Node.js 20 vs 24 mismatch is recorded and deferred to PLAN-0033. F-0028-03's "duplicated lines" item was retracted (review artifact).
- **Working tree state:** All changes committed on stacked branch `agent/plan-0031-0036-foundation-stack`.

## Progress log

### 2026-10-28T14:05:00Z — Claude Code resumption review (PLAN-0031)

- Created this plan from PLAN-0031 findings. No execution yet.

### 2026-10-28T14:20:00Z — Claude Code

- **Run delivery target:** Phases 1–3.
- **Checkpoint:** Phase 1 (backend dependency repair) complete.
- **Changes included in the commit:** `Directory.Packages.props` Testcontainers 4.16.0; integration lock file; eight integration fixtures use the image-parameter builder constructor.
- **Documentation and code-documentation delivered:** Plan and registry state. No public API changed.
- **Validation performed:** Release build with audit on (0/0); vulnerable-package scan clean; architecture 14/14; unit 52/52. Integration tests not run locally (no Docker).
- **Next action:** Phase 2 frontend dependency hygiene.

### 2026-10-28T14:45:00Z — Claude Code

- **Checkpoint:** Phase 2 (frontend and API-client dependency hygiene) complete.
- **Changes included in the commit:** Removed unused `@hookform/resolvers`, `axios`, `cra-template`, `date-fns`, `dayjs`, `framer-motion`, `lodash`, `@types/lodash`, `recharts`, `swr`, `zod`. `react-router-dom`/`react-router` 7.18.5. Resolution bumps for fast-uri, qs, follow-redirects, shell-quote, serialize-javascript, nested svgo and js-yaml. Re-resolved in-range lock entries. Emptied `audit-allowlist.json`. API-client lock refreshed.
- **Documentation delivered:** Frontend vulnerability and resolution triage documents.
- **Validation performed:** Frontend full local gate set green; API client audit 0, drift check passed.
- **Next action:** Phase 3 documentation drift.

### 2026-10-28T15:10:00Z — Claude Code

- **Checkpoint:** Phase 3 (documentation drift) complete except D-1 naming. Plan moves to `Validating`, pending PR CI.
- **Changes included in the commit:** README status, AGENTS.md frontend wording, environment guide, plan header/state reconciliations (PLAN-0014, PLAN-0017, PLAN-0021, PLAN-0023), PLAN-0031 correction entry, registry.
- **Validation performed:** Documentation-only commit; no rerun of code gates needed (PLAN-0007 amendment).
- **Next action:** Publish both branches from the GitHub App host and run CI on the PR head.

### 2026-10-28T14:45:00Z — Claude Code (correction)

- The progress-log timestamps 14:20, 14:45 and 15:10 above were estimated, not measured. The commit timestamps are authoritative (`955bbc7`, `0ff6d2a`, `5a16b9a`, all before 14:45Z).
- Docker became available on the host (PLAN-0034). Backend integration tests with Testcontainers 4.16.0 ran: 164/175 passed. The 11 failures are login/logout tests that need the OIDC development fixture from the compose stack, which CI starts first. They are revalidated under PLAN-0033/PLAN-0034 with the fixture running.

### 2026-10-28T22:20:00Z — Claude Code (PR #44 integration)

- The owner merged PR #44, which had already published PLAN-0028–PLAN-0030. This plan was renumbered from PLAN-0029 to PLAN-0032; `main` was merged into `agent/plan-0031-0036-foundation-stack`.
- No change in scope. Renumbered with the rest of the stack, and the PR #44 integration test with Testcontainers now uses the image-argument `PostgreSqlBuilder` constructor (the parameterless one is obsolete in 4.16.0 and fails the warnings-as-errors build).
- **Validation on the merged tree:** backend format, build with warnings as errors, unit 76/76, architecture 14/14, integration 178/178 (OIDC fixture); migrations applied twice idempotently (10 rows); OpenAPI export matches and lints; API client drift check; frontend lint, typecheck, guards, Vitest 50 files / 410 tests, production build, build-mode guard, audit policy (0 allowlisted).
- **Not passed locally:** the prototype browser smoke timed out on a page reload after 6 passing journeys, twice, while another project on the host kept the load average near 10 on 2 CPUs. The pre-merge commit `ff5f715` also fails with a navigation timeout under the same load, so this is not a regression of the merge. CI runs the smoke on the PR head.
