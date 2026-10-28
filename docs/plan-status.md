# Plan Execution Registry

This file is the canonical, repository-wide view of implementation, testing, research, documentation, and operational plans.

Detailed plans live under [`docs/plans/`](plans/). This registry exists so that a new agent can understand current work, ownership, progress, blockers, and the exact continuation point without reconstructing state from chat history or branch diffs.

## Mandatory agent rule

Before creating **every commit**, an agent must:

1. update the active plan's `Execution state` and `Progress log` sections;
2. update the matching row in this registry;
3. record the checkpoint represented by the commit, substantial run target, documentation delivered, validation performed, blockers, and exact next action;
4. ensure both files describe the repository state that will exist after the commit.

This requirement applies even when the commit is partial, documentation-only, test-only, or created because the agent must stop. A commit made by an agent without the corresponding plan-state update is noncompliant.

The plan and registry updates should be included in the same commit as the work they describe. Do not create a misleading status-only update before the implementation exists.

## Status model

Use exactly one execution status:

- **Draft**: the plan is being written and is not ready to execute.
- **Ready**: scope and acceptance criteria are sufficiently defined for work to start.
- **In Progress**: implementation or test execution is actively underway.
- **Paused**: work stopped intentionally and can continue without resolving a blocker.
- **Blocked**: work cannot continue until a documented dependency or decision is resolved.
- **Validating**: implementation is complete enough for tests, evaluations, or acceptance verification.
- **Completed**: all plan acceptance criteria and required validation are complete.
- **Cancelled**: work was intentionally abandoned and will not be completed.
- **Superseded**: another plan replaces this plan.

Pull-request and merge state are tracked separately in the `Delivery` column. Execution may be `Completed` while delivery is still `PR open` or `Awaiting owner merge`.

## Active plans

Plans with status `Draft`, `Ready`, `In Progress`, `Paused`, `Blocked`, or `Validating` belong here.

| Plan | Title | Type | Status | Delivery | Owner | Branch / PR | Current checkpoint | Exact next action | Blocker | Updated |
|---|---|---|---|---|---|---|---|---|---|---|
| [PLAN-0008](plans/PLAN-0008-define-lean-launch-ai-economics.md) · [execution amendment](plans/PLAN-0008-amendment-2026-08-02-execution-boundaries.md) · [thumbnail amendment](plans/PLAN-0008-amendment-recipe-thumbnail-policy.md) | Define Lean Launch, Acquisition, AI Unit Economics, and Monetization | Operations | Ready | Documentation merged via PR #29 (`1115ba4`); execution not started | Unassigned launch, growth, and AI-economics agent | New `agent/plan-0008-...` branch when claimed | Thumbnail policy docs merged; recipe evaluation consumed from PLAN-0022; implementation of thumbnails deferred to PLAN-0030 | Refresh official provider/platform evidence and produce launch-readiness docs without activating spend or production calls | Production calls, spend, billing, and analytics require later implementation/approval | 2026-08-05 |
| [PLAN-0021](plans/PLAN-0021-implement-contextual-home-live-sources.md) | Implement Contextual Home Live Source Contracts and Adapters | Implementation | Blocked | Not started | Unassigned backend/frontend vertical-slice agent | `agent/plan-0021-contextual-home-live-sources` | Recipe AI contracts finalized; waiting on accepted menu read contract | Keep blocked until PLAN-0029 publishes the accepted-menu read contract | Accepted menu/planning read contract from PLAN-0029 | 2026-08-05 |
| [PLAN-0029](plans/PLAN-0029-implement-sequential-planning-menu-and-shopping.md) | Implement Sequential Planning, Menu, and Shopping Projection | Implementation | Draft | Not started | Unassigned planning/shopping agent | TBD | Draft placeholder created during PLAN-0028 reconciliation | Claim after PLAN-0028 delivers saved recipe revisions | PLAN-0028 recipe identity and revisions | 2026-08-05 |
| [PLAN-0030](plans/PLAN-0030-implement-recipe-thumbnail-generation.md) | Implement Recipe Thumbnail Generation | Implementation | Draft | Not started | Unassigned AI media agent | TBD | Draft placeholder; policy docs already merged via PR #29 | Claim after PLAN-0028, job/storage foundations, and pricing refresh | PLAN-0028, storage/jobs, Gemini/Nano Banana pricing refresh | 2026-08-05 |

## Completed plans

Plans with status `Completed` belong here. Keep the most recent completed plans at the top.

| Plan | Title | Type | Delivery | Result | Completed |
|---|---|---|---|---|---|
| [PLAN-0028](plans/PLAN-0028-implement-recipe-ai-gateway-and-generation.md) | Implement Recipe AI Gateway and Cook-Now Generation Vertical Slice | Implementation | [PR #44](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/44) ready for owner review (unmerged) | **Completed** — cook-now vertical slice + merge-blocker remediations (atomic reserve, Expanding claims, transactional finalize, full-schema DeepSeek + repair); focused concurrency tests Passed; live synthetic smoke Passed (3 calls, ~US$ 0.00243) | 2026-08-06 |
| [PLAN-0022](plans/PLAN-0022-evaluate-and-finalize-recipe-ai-contracts.md) · [lean amendment](plans/PLAN-0022-amendment-2026-08-05-lean-evaluation.md) | Evaluate and Finalize Recipe AI Artifact Contracts | Research | **Merged** through PR #43 at `cfec79546fcd5e476f7198cdc788a6ce251d35c5` | **Completed** — protocol `0.3` Revised; deterministic validators Passed; lean live campaign 4 scenarios / 8 provider calls (~US$ 0.012867); thinking truncated; fallback schema Fail / semantic Pass; accounting corrected; statistical latency deferred | 2026-08-05 |
| [PLAN-0027](plans/PLAN-0027-independently-retest-prepared-component-remediation.md) | Independently Retest Prepared-Component Remediation | Testing | **Completed — Pass**. PR #42 incorporated through PR #39 | **Pass** at SUT `9bff2e130afb4a0f31ea0b84925362f546d1179e`; F-0026-01/02/03 Passed; F-0027-01 resolved after assessment | 2026-08-05 |
| [PLAN-0023](plans/PLAN-0023-implement-prepared-components-and-derived-lots.md) | Implement Prepared Components and Derived Inventory Lots | Implementation | **Merged** through PR #39 at `7912d4676ffc1f06ac193b7d6788c3a910ed2bd1` (functional SUT `9bff2e130afb4a0f31ea0b84925362f546d1179e`) | Completed after PLAN-0027 **Pass**; no longer awaiting owner review | 2026-08-05 |
| [PLAN-0026](plans/PLAN-0026-independently-validate-prepared-components.md) | Independently Validate Prepared Components and Derived Lots | Testing | **Completed — Fail**. PR #40 closed without merge (immutable historical evidence) | **Fail** at SUT `7e24fa2f86350d8a566de0b9f2f1cdba984080ff`; F-0026-01/P1, F-0026-02/P1, F-0026-03/P2 | 2026-08-05 |
| [PLAN-0025](plans/PLAN-0025-independently-retest-plan-0020-profile-remediation.md) | Independently Retest PLAN-0020 Profile Remediation | Testing | PR #38 merged into the PLAN-0020 branch and included in PR #35 | **Pass** at immutable SUT `06bd95baacaabaa099170de1ba41187a8e885dea`; all F-0024 findings passed, full gates passed | 2026-08-03 |
| [PLAN-0020](plans/PLAN-0020-implement-profile-household-equipment-frontend.md) | Implement Profile, Household, Preferences, and Equipment Frontend | Implementation | Merged through PR #35 at `f166ce21020f6704d3fcd99b4b6d195b33638155` | Completed after independent PLAN-0025 **Pass** at `06bd95b` | 2026-08-04T01:24:28Z |
| [PLAN-0024](plans/PLAN-0024-independently-validate-plan-0020-profile-frontend.md) | Independently Validate PLAN-0020 Profile Frontend | Testing | PR #36 closed without merge (immutable historical Fail evidence) | **Fail** at SUT `5733bb4de957b53469a28bc60c472a90f0955907` (P1 F-0024-01/02); evidence tip `b549a97` | 2026-08-04T01:24:28Z |
| [PLAN-0011](plans/PLAN-0011-implement-entry-and-contextual-home.md) · [amendment](plans/PLAN-0011-amendment-2026-08-02-next-execution.md) | Implement the Public Entry and Contextual Home Experience | Implementation | Merged via [PR #34](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/34) at `eb9e92c21ac817e497235168786daeb3f35c30cd` | Phase 1+2 + remediations + chooser contract hardening; immutable functional tip `9079887`; CI tip `9017e7e` | 2026-08-02 |
| [PLAN-0019](plans/PLAN-0019-reconcile-roadmap-and-next-work.md) | Reconcile the Active Roadmap and Define the Next Work Queue | Documentation | Merged via PR #33 (`444969ccd95526cb19730cfed8d016c9f299a7b7`) | Canonical documentation reviewed; stale delivery truth reconciled; active scopes separated; PLAN-0020 through PLAN-0023 created; PLAN-0011 selected as next assignment | 2026-08-02 |
| [PLAN-0017](plans/PLAN-0017-define-ai-recipe-artifact-protocol.md) · [completion handoff](plans/PLAN-0017-completion-and-research-handoff.md) | Define AI Recipe Artifact Protocol and Model Evaluation Pack | Documentation | Merged via PR #31 (`97b531b`) | Protocol-definition package completed and merged; empirical benchmark/strict-contract work transferred to PLAN-0022 without rewriting history | 2026-08-02 |
| [PLAN-0016](plans/PLAN-0016-implement-production-session-and-inventory-frontend.md) | Implement Production Session and Authenticated Inventory Frontend | Implementation | Merged via PR #25 at `e35c453acccd79f01398e51b7fe8ee4cb94f44a3` | Completed production session/inventory frontend, independent remediations, semantic create idempotency, history-refresh isolation, and green Backend/Frontend/PLAN-0005 gates | 2026-08-02 |
| [PLAN-0018](plans/PLAN-0018-independent-retest-plan-0016.md) | Independently Retest PLAN-0016 Authenticated Inventory Remediations | Testing | PR #27 merged into the PLAN-0016 branch and later included in PR #25 | **Fail** at historical SUT `814af25`. Later PLAN-0016 remediations independently passed; this historical result remains immutable | 2026-08-02 |
| [PLAN-0005](plans/PLAN-0005-test-authenticated-inventory-slice.md) | Independently Validate the Authenticated Inventory Slice | Testing | Merged via PR #19 at `60d98dd9e2e7c460d670e701c027a44f25cdfedc` | **Conditional Pass** at merge. Residual #20/#21/#22/#24/#26 later passed under PLAN-0016 and were closed | 2026-08-01 |
| [PLAN-0015](plans/PLAN-0015-remediate-frontend-baseline.md) | Remediate and Validate the Imported Frontend Baseline | Implementation | Merged via PR #16; completion evidence via PR #18 | Fail-closed Chromium/Firefox native zoom passed; 22/22 scenarios passed; manual visual/NVDA/VoiceOver deferred as non-blocking | 2026-08-01 |
| [PLAN-0012](plans/PLAN-0012-implement-profile-household-equipment-backend.md) | Implement Account, Household, Profile and Equipment Backend Slice | Implementation | Merged via PR #12 (`49985f94d336e6079f1979a2140555f1beab765c`) | Owner-isolated profile/equipment backend; 191 tests and final Backend CI evidence | 2026-08-01 |
| [PLAN-0014](plans/PLAN-0014-integrate-emergent-frontend.md) | Integrate Emergent Frontend and Establish Production Frontend Baseline | Implementation | Merged via PR #14; documentation via PR #15 | Official monorepo frontend established; later remediated by PLAN-0015 | 2026-07-31 |
| [PLAN-0013](plans/PLAN-0013-define-closed-loop-kitchen-orchestration.md) | Define Closed-Loop Kitchen Orchestration | Documentation | Merged via PR #13 | Accepted localized recovery, prepared components, sequential simulation, reconciliation, uncertainty, controlled learning, and multi-day dependency routes | 2026-07-31 |
| [PLAN-0003](plans/PLAN-0003-implement-backend-inventory-slice.md) | Implement Backend Foundation and Inventory Core | Implementation | Merged via PR #9 | Final backend inventory candidate passed CI, Keycloak smoke, migrations, OpenAPI, and 115 tests | 2026-07-31 |
| [PLAN-0010](plans/PLAN-0010-define-entry-and-contextual-home.md) | Define the Public Entry and Contextual Home Experience | Documentation | Merged via PR #11 | Accepted public briefing, contextual home, source ordering, local time, accessibility, privacy telemetry, and AI degradation | 2026-07-30 |
| [PLAN-0007](plans/PLAN-0007-strengthen-agent-delivery-standards.md) | Strengthen Agent Documentation and Delivery Standards | Documentation | Merged via PR #8 | Mandatory complete documentation, code comments/XML/TSDoc, and substantial coherent delivery standards | 2026-07-29 |
| [PLAN-0006](plans/PLAN-0006-refine-lovable-design-brief.md) | Refine the Lovable Product Design Brief | Documentation | Merged via PR #7 | Visual direction, SPA behavior, prototype scenarios, gallery, and production-isolation requirements | 2026-07-29 |
| [PLAN-0002](plans/PLAN-0002-specify-authenticated-inventory-vertical-slice.md) | Specify the First Authenticated Inventory Vertical Slice | Documentation | Merged via PR #6 | First-slice specification, backend/frontend/test plans, and canonical environment | 2026-07-29 |
| [PLAN-0001](plans/PLAN-0001-document-product-foundation.md) | Document the KitchenFlow Product Foundation | Documentation | Merged via PR #5 | Product, domain, architecture, AI, privacy, security, operations, and test-gate foundation | 2026-07-28 |

## Cancelled or superseded plans

| Plan | Title | Status | Replacement or reason | Updated |
|---|---|---|---|---|
| [PLAN-0004](plans/PLAN-0004-implement-lovable-inventory-ux.md) | Implement Lovable Application Shell and Inventory UX | Superseded | Replaced by PLAN-0014; monorepo frontend is authoritative | 2026-07-31T20:15:00Z |

## Registry maintenance rules

- Every plan appears exactly once in this file.
- Link the plan ID to its Markdown file.
- Use ISO 8601 dates and UTC timestamps when time precision matters.
- `Current checkpoint` must state the last verified state, not an intention.
- `Exact next action` must be concrete enough for a different agent to execute immediately.
- `Delivery` must state the repository delivery state.
- A blocked row must identify the blocking dependency or decision.
- When responsibility changes, update the plan and add a handoff entry.
- Move rows between sections instead of duplicating them.
- Preserve historical completed, cancelled, and superseded entries.
- Reconcile delivery state and branch deletion after merge.
