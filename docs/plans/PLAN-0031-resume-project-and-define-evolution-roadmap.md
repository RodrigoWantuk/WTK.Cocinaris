# PLAN-0031: Resume the Project and Define the Evolution Roadmap

- **Status:** Completed
- **Type:** Documentation
- **Priority:** Critical
- **Owner:** Claude Code resumption review (2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28T14:05:00Z
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** Draft PR from `agent/plan-0031-0036-foundation-stack` (one consolidated PR for PLAN-0031–PLAN-0036)
- **Numbering:** Created as PLAN-0028. Renumbered to PLAN-0031 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Base at claim:** `main` at `6b667114c7aaec5db927ec5847545db89152f173`
- **Related plans:** PLAN-0008, PLAN-0019, PLAN-0021, PLAN-0022, PLAN-0023, PLAN-0032
- **Related ADRs:** ADR-0002 through ADR-0007

## Objective

Resume the repository after the August 2026 pause (last functional work 2026-08-05; README rename 2026-09-29), establish the verified current state, record defects and documentation drift found on `main`, and leave an ordered, dependency-aware roadmap that closes the accepted first-release loop without inventing a smaller MVP.

This plan succeeds PLAN-0019 as the canonical work queue. PLAN-0019 remains historical.

## Authoritative basis reviewed

- `AGENTS.md`, `README.md`, `docs/README.md`, `docs/plan-status.md`, `docs/plans/README.md`, plan templates;
- product vision, 2026-07-28 stakeholder discovery, initial release definition, closed-loop orchestration, entry/contextual home, user journeys;
- domain inventory lifecycle, planning and shopping, recipes and cooking;
- architecture principles, overview, ADR index and ADR-0001 to ADR-0007;
- AI overview, recipe artifact protocol, usage/cost governance;
- security, privacy, operations, testing strategy and product-foundation gates (structure and gates);
- PLAN-0007 lean-validation amendment, PLAN-0008, PLAN-0019, PLAN-0021, PLAN-0022, PLAN-0023 and the status header of every plan.

## Verified repository state on 2026-10-28

### Delivered on `main`

| Area | State | Source |
|---|---|---|
| Identity and browser session | Keycloak OIDC, backend-managed `__Host-` session, CSRF, internal user ownership | PLAN-0003 |
| Inventory | Products/lots, mandatory quantity or availability, history, provenance, concurrency token, idempotency, 9 migrations | PLAN-0003, PLAN-0016 |
| Prepared components | `POST /api/v1/inventory/preparations`, atomic derived lots with parent consumption | PLAN-0023 |
| Profile | Household, preferences/restrictions, equipment, completeness API and production UI | PLAN-0012, PLAN-0020 |
| Public entry and home | Public entry page; contextual home driven by **mock** source adapters | PLAN-0011 |
| Contracts | OpenAPI + generated TypeScript client with drift gates | PLAN-0003/0016 |
| AI contracts | Recipe artifact protocol `0.3` (suggest/expand/thumbnail) evaluated; **no gateway code** | PLAN-0017, PLAN-0022 |

### Not implemented (first-release scope still open)

Recipes module and library; AI Gateway, quota ledger and provider adapter; planning (simulation/draft/accepted) and reservations; live home sources; shopping; guided cooking, execution and reconciliation; quick completion; localized recovery; multi-day preparation route; troubleshooting; list/receipt parsing; recipe import from URL/image; sharing; notifications (dashboard, web push, email); privacy center (export/deletion); worker host, transactional outbox, RabbitMQ, persistent jobs; object storage; billing; launch operations. Production prepared-component UI is also absent (backend endpoint only). The routes `/app/receitas`, `/app/planejamento`, `/app/compras`, `/app/cozinhar/:id` exist only in the prototype application.

### Local validation performed (host `rwantuk`, .NET SDK 10.0.302, Node 20.19.2)

| Check | Result |
|---|---|
| `dotnet build KitchenFlow.slnx -c Release` | **Fail** — `NU1903` for `SSH.NET 2024.2.0` (GHSA-mggc-4xg6-vcxf, GHSA-q939-rpr3-3284), transitive through `Testcontainers.PostgreSql 4.8.1`; warnings are errors |
| Backend build with `-p:NuGetAudit=false` | Pass |
| Architecture tests | Pass (14/14) |
| Unit tests | Pass (52/52) |
| Integration tests | Not run — Docker is not available on this host |
| Frontend `tsc --noEmit`, ESLint (0 warnings), Prettier, TS-only / nesting / production-isolation guards | Pass |
| Frontend Jest | Pass (45 suites, 369 tests) |
| Frontend production build | Pass |
| Frontend `audit:policy` | **Fail** — 51 unapproved advisories (3 critical, ~30 high) |
| `packages/api-client` `yarn audit` | 5 advisories (1 moderate, 4 high), not yet triaged |

Remote CI state on `main` was not queried because remote operations are restricted to the GitHub App host. Both failures are caused by advisories published after the last green CI, so the Backend and Frontend workflows are expected to fail on any new PR until PLAN-0032 lands.

## Findings

### F-0028-01 (P0) — Backend restore fails on a newly published transitive advisory

`SSH.NET 2024.2.0` (via Testcontainers) is now flagged high severity, and the solution treats `NU1903` as an error. It is a test-only dependency, but it blocks every backend build, including CI. Central transitive pinning is already enabled in `Directory.Packages.props`, so a patched `SSH.NET` pin or a Testcontainers upgrade fixes it.

### F-0028-02 (P0) — Frontend dependency policy fails with 51 unapproved advisories

- `axios` (12 advisories), `zod`, `swr`, `dayjs`, `date-fns`, `lodash`, `recharts`, `framer-motion`, `@hookform/resolvers`, `cra-template` are declared runtime dependencies that **no source file imports**. They are residue from the Emergent/Lovable import and can be removed.
- `react-router` has a new advisory ID (`1138769`) with the same title as the allowlisted `1124282`; the allowlist entry no longer matches anything.
- `nanoid`, `qs` and similar need a version bump or a justified exception.
- The remainder (`fast-uri`, `brace-expansion`, `js-yaml`, `svgo`, `launch-editor`, `webpack-dev-server` chain, `proxy-addr`, `shell-quote`, `body-parser`, `compression`, …) comes from the deprecated Create React App / CRACO toolchain and is build/dev-time only. That justifies time-boxed exceptions, but the structural fix is a new bundler; the ADR backlog already lists CRA/CRACO evolution as an open decision.

### F-0028-03 (P1) — Documentation drift on `main`

- `README.md` (commit `6b66711`) renames the product to **Cocinaris**, while `AGENTS.md`, `docs/`, code namespaces (`KitchenFlow.*`), metrics (`kitchenflow.*`), and environment variables (`KITCHENFLOW_*`) still use KitchenFlow. No accepted document or plan records the rename decision.
- `README.md` still says public entry, contextual home and production profile UI are future work, but PLAN-0011 and PLAN-0020 delivered them.
- Plan headers disagree with the registry: PLAN-0014 (`Implemented — remediation … pending`), PLAN-0017 (`Validating`), and PLAN-0008 amendments.
- PLAN-0023 `Execution state` still says that the PLAN-0027 retest is pending, which it no longer is. (Corrected 2026-10-28: the duplicated lines and empty excluded-scope heading originally reported here were an artifact of the review command, not file content.)
- PLAN-0021 header says `Ready`, but its body says it stays `Blocked` until the prerequisites exist. Its own next action allows the deterministic inventory/profile tiers to start first.
- `AGENTS.md` §1 still says the frontend is "generated and evolved through Lovable", which ADR-0007 superseded (Lovable/Emergent optional).

### F-0028-04 (P1) — The critical path is recipes, not the home

PLAN-0021 is blocked by an accepted menu/planning contract. A menu entry references a recipe UUID and revision, but no recipe module exists. So recipes are the root dependency for planning, the live home Tier 1, shopping, cooking and reconciliation. The previous queue has no recipe implementation plan.

### F-0028-05 (P2) — Missing asynchronous platform foundation

ADR-0003 accepts outbox, RabbitMQ and persistent jobs, and ADR-0002 accepts separate worker deployables. None exists yet. Receipt/list parsing, recipe import, notifications, account deletion and AI background work all depend on this foundation.

### F-0028-06 (P2) — Local tooling gaps on the current host

`dotnet` is installed at `/opt/dotnet` but is not on `PATH`. `yarn` (pinned `1.22.22` through `packageManager`) and Docker are absent. Integration tests, the real-Keycloak smoke and `audit:policy` cannot run here without them. `docs/development/environment.md` should state how to enable Corepack/yarn.

## Stakeholder decisions required

| ID | Decision | Recommendation | Blocks |
|---|---|---|---|
| D-1 | Is **Cocinaris** the accepted product name? If yes, do technical namespaces change too? | Accept Cocinaris as the product/brand name in user-facing copy and docs. Keep `KitchenFlow.*` technical identifiers until a dedicated rename plan, because cookies, metrics, env vars and migrations make a rename an operational change. | PLAN-0032 doc reconciliation (name only), later rename plan |
| D-2 | Replace CRA/CRACO with Vite (ADR-0008)? | Yes. It removes most dev-toolchain advisories structurally and enables React Router 8. | PLAN-0036 (originally reserved as PLAN-0030) |
| D-3 | AI provider for the first gateway adapter and a monthly spend cap for development | Use the PLAN-0022-evaluated route (DeepSeek V4 Flash through OpenRouter, zero-retention settings) with a hard development cap, recorded in a provider ADR | PLAN-0036 |
| D-4 | Confirm the queue order below | — | All |

## Roadmap

> **Historical, superseded.** This section is kept as written on 2026-10-28, when this plan was numbered PLAN-0028. Its plan IDs are the original reservations and do **not** match the current registry. The current roadmap is in the [coverage review amendment](PLAN-0031-amendment-2026-10-28-coverage-review.md).

The order follows the accepted closed-loop implementation order (`product/closed-loop-kitchen-orchestration.md`): prepared components → sequential planning → reconciliation → localized recovery → multi-day route → troubleshooting. Recipes and the AI Gateway are inserted where that order depends on them. Plan IDs are reserved here; each file is created when the plan is claimed, except PLAN-0029, which is created now because it is the immediate next action.

### Stage A — Restore a trustworthy baseline

| Order | Plan | Type | Outcome | Depends on |
|---|---|---|---|---|
| A1 | **PLAN-0029** Restore green baseline and reconcile drift | Operations | Backend restore passes; unused frontend deps removed; runtime advisories patched; dev-toolchain advisories documented with expiry; documentation drift F-0028-03 corrected; CI green on PR head | — |
| A2 | PLAN-0030 Frontend toolchain migration (ADR-0008, CRA → Vite) | Implementation | Same guards, tests, production isolation and bundle inspection on Vite; React Router 8; frontend CI on Node.js 24 like the other workflows | A1, D-2 |

### Stage B — Recipe core and AI foundation (unblocks everything downstream)

| Order | Plan | Type | Outcome | Depends on |
|---|---|---|---|---|
| B1 | PLAN-0031 Recipes module and library | Implementation | Deterministic recipe aggregate: user-owned UUID, normalized revision structure, revisions vs derived recipes, favorites, manual create/edit, owner isolation, migration, OpenAPI, production library/detail/editor UI in three locales | A1 |
| B2 | PLAN-0032 Asynchronous platform foundation | Implementation | Worker host, transactional outbox, RabbitMQ topology ADR, persistent job table, idempotent consumers, dead-letter handling, observability | A1 |
| B3 | PLAN-0033 AI Gateway and recipe suggestion/expansion | Implementation | Gateway pipeline (context budget, schema validation, domain validation, quota reservation/settlement, cost ledger, fallback, redacted telemetry), provider ADR, `recipe.suggest_candidates.v1` and `recipe.expand_selected.v1` saving into PLAN-0031 recipes, deterministic degraded behavior, evaluation fixtures in CI | B1, D-3 (B2 if long operations run in the background) |

### Stage C — Close the planning loop

| Order | Plan | Type | Outcome | Depends on |
|---|---|---|---|---|
| C1 | PLAN-0034 Sequential planning simulation and accepted menu | Implementation | Simulation/draft/accepted states, projected sequential inventory, flexible reservations and locks, package-size inputs, accepted-menu read contract | B1 (B3 for AI candidates) |
| C2 | PLAN-0021 Contextual home live sources (existing) | Implementation | Deterministic inventory/profile tiers can start right after A1. Menu tier after C1, AI chooser after B3 | A1, C1, B3 |
| C3 | PLAN-0035 Shopping requirements and purchase reconciliation | Implementation | Required/recommended/optional/available/expected-to-run-out items, package remainder reasoning, planned vs actual purchase, localized adaptation | C1 |

### Stage D — Cook and reconcile

| Order | Plan | Type | Outcome | Depends on |
|---|---|---|---|---|
| D1 | PLAN-0036 Guided cooking, execution and atomic reconciliation | Implementation | Mise en place, explicit start, staged instructions, execution-local state, completion proposal, atomic reconciliation or explicit pending state, quick completion, leftovers/waste, production prepared-component UI | B1, C1 |
| D2 | PLAN-0037 Localized plan recovery | Implementation | Divergence detection and reversible localized proposals | C1, D1 |
| D3 | PLAN-0038 Multi-day preparation dependency route (live) | Implementation | Replaces the prototype route projection with backend dependency route and due windows | C1, D1 |
| D4 | PLAN-0039 Text troubleshooting and controlled learning | Implementation | Gateway operation with bounded execution context; revise/derive/discard at finalization | B3, D1 |

### Stage E — Inputs, rights, notifications, launch

| Order | Plan | Type | Outcome | Depends on |
|---|---|---|---|---|
| E1 | PLAN-0040 Object storage and AI inventory input (list + receipt photo) | Implementation | Transient upload pipeline with immediate deletion, parsing operations, review before commit | B2, B3 |
| E2 | PLAN-0041 Recipe import (URL/image) and private sharing | Implementation | Hostile-input URL fetching, image parsing, import analysis, immutable snapshot sharing | B1, B3, E1 |
| E3 | PLAN-0042 Attention dashboard and notifications | Implementation | Dashboard, web push, email, postponement, anti-spam preferences | B2, C1, D1 |
| E4 | PLAN-0043 Privacy center | Implementation | Access, export, correction, item/photo deletion, account deletion workflow, retention jobs | B2, all data-owning modules |
| E5 | PLAN-0008 Launch, acquisition, AI economics (existing) | Operations | Phases 1–2 can run as documentation at any time. Billing/ads need their own ADRs | B3 for real cost data |
| E6 | PLAN-0044 Technical rename to Cocinaris (only if D-1 says so) | Operations | Namespaces, cookies, metrics, env vars, with compatibility and rollout notes | D-1 |

### Parallelism rules

- A1 goes first and alone. It touches shared dependency manifests and CI.
- After A1, B1, B2 and the deterministic part of C2 can run in parallel. They touch disjoint modules, but each regenerates the OpenAPI client, so merge them sequentially and regenerate on rebase.
- A2 must not run concurrently with frontend feature plans (B1 UI, C2) because it rewrites the build. Schedule it right after A1 or between frontend deliveries.
- Every stage uses the PLAN-0007 lean validation default. Independent validation applies only to the high-impact cases listed there, which here means C1/D1 reconciliation concurrency, E4 deletion, and B3 quota/cost accounting.

## Scope

### Included

- verified state, defect and drift findings;
- stakeholder decisions required;
- ordered roadmap with dependencies and reserved plan IDs;
- creation of PLAN-0032 as the immediate executable plan;
- registry update.

### Excluded

- fixing the findings (owned by PLAN-0032);
- creating plan files for PLAN-0033 onward;
- changing accepted product, domain or architecture decisions;
- remote Git/GitHub operations (host restriction).

## Acceptance criteria

- [x] Current repository and validation state recorded with commands and results.
- [x] Defects and drift identified with severity and owner plan.
- [x] Stakeholder decisions isolated with recommendations.
- [x] Dependency-aware roadmap covering the full accepted first release.
- [x] Immediate next plan (PLAN-0032) created and registered as `Ready`.
- [x] No accepted behavior reduced or reinterpreted.

## Execution state

- **Current run delivery target:** Decision-ready resumption assessment and roadmap.
- **Current checkpoint:** Assessment, roadmap and PLAN-0032 written on `agent/plan-0031-0036-foundation-stack`.
- **Last completed step:** Registry updated with PLAN-0031 (completed) and PLAN-0032 (ready).
- **Exact next action:** Final CI on the draft PR head from `agent/plan-0031-0036-foundation-stack`; owner merge.
- **Blockers:** None. Owner merge determines delivery state only.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan, PLAN-0032, registry rows.
- **Validation performed:** Local backend build/tests and frontend lint/typecheck/tests/build/audit as listed above.
- **Known failures or limitations:** Integration tests and remote CI state not verified (no Docker; remote restricted).
- **Working tree state:** Committed on `agent/plan-0031-0036-foundation-stack` (owner instructed commit on 2026-10-28); PLAN-0032 execution continues on a stacked `agent/plan-0031-0036-foundation-stack` branch.

## Progress log

### 2026-10-28T14:05:00Z — Claude Code resumption review

- **Run delivery target:** Resume the project, validate `main`, and define the evolution roadmap.
- **Checkpoint:** Assessment and roadmap complete; PLAN-0032 created.
- **Changes:** Added PLAN-0031 and PLAN-0032; updated `docs/plan-status.md`.
- **Validation performed:** See "Local validation performed".
- **Result:** Two P0 baseline failures (backend restore, frontend audit policy), documentation drift, and a missing recipe-module plan on the critical path.
- **Next action:** Owner decisions D-1 to D-4, then PLAN-0032.

### 2026-10-28T15:05:00Z — Claude Code (corrections found while executing PLAN-0032)

- **Correction:** PLAN-0023 had no duplicated lines and its excluded-scope section has content. Only the stale retest blocker was real; F-0028-03 is amended above.
- **New finding:** `.github/workflows/frontend.yml` runs Node.js 20 (end of life), while `docs/development/environment.md` requires Node.js 24 and the Backend/PLAN-0005 workflows use 24.18.0. Added to the frontend toolchain plan (A2, now PLAN-0036) scope.
- **Update:** PLAN-0032 cleared every frontend advisory by upgrades. React Router 8 is no longer required for security (the fix was backported to 7.18.2), so the frontend toolchain plan (now PLAN-0036) is motivated by CRA deprecation, Node.js 24 alignment, and toolchain debt, not by an allowlisted advisory.

### 2026-10-28T14:45:00Z — Claude Code

- Owner resolved D-1 (Cocinaris everywhere) and designated the owner Linux host as the development and deployment environment. A full plan coverage review is recorded in [`PLAN-0031-amendment-2026-10-28-coverage-review.md`](PLAN-0031-amendment-2026-10-28-coverage-review.md), which **replaces the roadmap table above** (renumbered PLAN-0033–PLAN-0052).

### 2026-10-28T22:20:00Z — Claude Code (PR #44 integration)

- The owner merged PR #44, which had already published PLAN-0028–PLAN-0030. This plan was renumbered from PLAN-0028 to PLAN-0031; `main` was merged into `agent/plan-0031-0036-foundation-stack`.
- The roadmap in the coverage review was reconciled with PR #44: PLAN-0028 (AI Gateway, cook-now) is delivered; the reserved recipe-library and AI Gateway plans are reduced to what PR #44 did not deliver (PLAN-0038, PLAN-0040); planning and thumbnails reuse the existing drafts PLAN-0029 and PLAN-0030. The mapping is in the amendment. The superseded roadmap section of this plan keeps its original reserved IDs with a warning. `docs/roadmap/initial-release-delivery-map.md` was updated and no longer names the identity provider.
- **Validation on the merged tree:** backend format, build with warnings as errors, unit 76/76, architecture 14/14, integration 178/178 (OIDC fixture); migrations applied twice idempotently (10 rows); OpenAPI export matches and lints; API client drift check; frontend lint, typecheck, guards, Vitest 50 files / 410 tests, production build, build-mode guard, audit policy (0 allowlisted).
- **Not passed locally:** the prototype browser smoke timed out on a page reload after 6 passing journeys, twice, while another project on the host kept the load average near 10 on 2 CPUs. The pre-merge commit `ff5f715` also fails with a navigation timeout under the same load, so this is not a regression of the merge. CI runs the smoke on the PR head.
