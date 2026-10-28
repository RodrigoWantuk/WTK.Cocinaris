# PLAN-0021: Implement Contextual Home Live Source Contracts and Adapters

- **Status:** Blocked
- **Type:** Implementation
- **Priority:** High
- **Owner:** Unassigned backend/frontend vertical-slice agent
- **Created:** 2026-08-02
- **Last updated:** 2026-08-05
- **Branch:** `agent/plan-0021-contextual-home-live-sources`
- **Pull request:** Not opened
- **Dependencies:** PLAN-0011 presentation boundary; PLAN-0020 production profile UI; PLAN-0022 recipe contract decisions (Completed); accepted menu/planning read contract from PLAN-0029
- **Related product:** `docs/product/entry-and-contextual-home.md`
- **Parent implementation plan:** PLAN-0011 Phase 3 and Phase 4 transfer

## Objective

Replace the contextual home's synthetic source adapters with versioned backend contracts and production adapters while preserving source priority, independent degradation, privacy minimization, deterministic authority, and user control.

## Why this is separate from PLAN-0011

Public entry and mock-backed presentation can be completed now. Live source integration crosses backend modules, OpenAPI, generated clients, profile/inventory/menu/recipe ownership, AI Gateway boundaries, freshness, and telemetry. Combining those concerns into the initial frontend delivery would encourage invented DTOs and unreviewable scope.

## Intended vertical slice

Implement one coherent home query/orchestration surface that can return source-specific results and failures without flattening domain ownership.

The target sources are:

1. accepted menu entries relevant to local context;
2. deterministic inventory-attention candidates;
3. confirmed-profile fit candidates;
4. request-scoped quick chooser and recommendation capability.

The backend may aggregate projections, but each source retains explicit status, freshness, reason codes, and ownership. One failed source does not erase another.

## Required prerequisites

- PLAN-0011 establishes stable presentation models, states, ordering assertions, and mock fixtures.
- PLAN-0020 exposes and validates production profile settings needed by the home.
- PLAN-0022 finalizes recipe candidate/expansion contract decisions needed by AI-backed suggestion work.
- A planning implementation publishes the accepted-menu read contract before Tier 1 can be claimed live.

Until all prerequisites exist, this plan remains `Blocked`. Do not fake missing tiers with production mock data.

## Included scope

- backend application contract/read model for home source results;
- local datetime and IANA timezone source semantics;
- source-specific capability, freshness, reason, failure, and trace codes;
- deterministic inventory-attention projection;
- confirmed-profile projection without sensitive-data leakage;
- accepted-menu projection once available;
- quick-chooser request/answer schema with no durable profile mutation;
- AI Gateway integration only through a separately registered operation;
- OpenAPI generation and TypeScript client regeneration;
- frontend live adapters and progressive rendering;
- cancellation, idempotency where needed, retry boundaries, ETag/freshness policy;
- privacy-safe telemetry and observability;
- full authorization and cross-user isolation tests;
- supported locales and accessibility;
- independent test plan if AI or cross-module mutation enters scope.

## Excluded scope

- planning implementation itself;
- recipe generation contract research owned by PLAN-0022;
- direct provider calls from frontend or domain modules;
- authoritative ranking or mutation in React;
- sponsored placement;
- precise geolocation;
- silently saving quick-chooser answers;
- returning raw pantry/profile/private recipe content in telemetry.

## Contract minimum

Represent:

- local date/time, timezone, and timezone source;
- source tier and source-specific status;
- stable localized reason codes;
- recipe/preparation identity and revision;
- readiness, missing requirements, preparation dependencies, and uncertainty;
- active/total time and effort where known;
- inventory influence using bounded references;
- capability and recoverable failure code;
- question ID, prompt key, answer options, and request-scoped answer schema;
- data freshness and source version;
- privacy-safe trace identifier.

## Acceptance criteria

- [ ] Every claimed live tier is backed by an accepted authoritative source.
- [ ] Tier ordering remains deterministic and testable.
- [ ] A source failure does not blank successful sources.
- [ ] No source display mutates plans, inventory, profile, shopping, or execution state.
- [ ] Context is minimized and cross-user isolation is proven.
- [ ] AI calls, if any, pass through the AI Gateway with budget and evaluation.
- [ ] OpenAPI and generated-client drift gates pass.
- [ ] Production adapters contain no fallback to prototype fixtures.
- [ ] Accessibility, localization, resilience, and observability are complete.

## Execution state

- **Current checkpoint:** PLAN-0011, PLAN-0020, and PLAN-0022 are complete. PLAN-0028 delivers recipe revisions. PLAN-0021 remains blocked on the accepted menu/planning read contract.
- **Blockers:** Accepted menu/planning read contract from the future sequential-planning slice (PLAN-0029).
- **Exact next action:** Keep blocked until PLAN-0029 publishes the accepted-menu read contract; then claim `agent/plan-0021-contextual-home-live-sources`.
