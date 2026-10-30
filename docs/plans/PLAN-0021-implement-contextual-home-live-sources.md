# PLAN-0021: Implement Contextual Home Live Source Contracts and Adapters

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (deterministic tiers claimed 2026-10-29)
- **Created:** 2026-08-02
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0042-cook-and-reconcile` (deterministic tiers delivered with PLAN-0042 Phase 1; merged and deleted)
- **Pull request:** Deterministic tiers merged via [PR #50](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/50) at `595af8e`
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

Each tier stays blocked until its own prerequisite exists: the deterministic inventory-attention and confirmed-profile tiers may be delivered first, Tier 1 waits for the accepted-menu contract (PLAN-0034 in the PLAN-0028 roadmap), and AI-backed chooser behavior waits for the AI Gateway (PLAN-0033). Do not fake missing tiers with production mock data.

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

- **Current checkpoint:** The production "Hoje" page (`TodayPage`) uses live sources and no fixtures. Each section loads and fails independently:
  - cooking in progress (PLAN-0042);
  - the main "O que cozinho agora?" action;
  - the five most urgent items from the inventory attention list (PLAN-0037);
  - the three most recent saved recipes (archived recipes are excluded since PLAN-0038).

  The sections are composed in the frontend from existing owner-scoped endpoints. No new aggregate endpoint was needed, and the plan allows either approach.
- **Remaining:**
  - confirmed-profile fit and the quick chooser;
  - AI-ranked suggestions through the gateway;
  - source freshness and telemetry codes.
- **Validation:** `Cooking.test.tsx` covers the home page: resume, attention, and recipe sources, with one failing source isolated. The app shell and home route tests pass, and the home section of the browser journey passes.
- **Menu tier (PLAN-0029 Phase 1, on `agent/plan-0029-menu-and-shopping`):** "No cardápio de hoje" is now the first section. It reads `GET /api/v1/planning/today?date=` with the browser's local day, cooks a meal linked to its menu entry, and degrades independently; `Cooking.test.tsx` covers its order and the linked start.
- **Exact next action:** The confirmed-profile tier and the quick chooser.
- **Blockers:** The menu tier depends on PLAN-0029.

## Progress log

### 2026-10-28T15:00:00Z — Claude Code (PLAN-0032 drift reconciliation; numbered PLAN-0029 before the PR #44 renumbering)

- Clarified per-tier gating so the prerequisite paragraph agrees with the `Ready` status and the existing exact next action (deterministic inventory/profile tiers first). No scope or acceptance criterion changed.

### 2026-10-29T05:10:00Z — Claude Code

- Delivered the deterministic tiers with PLAN-0042 Phase 1, following the owner's end-to-end direction. Production `/app/hoje` no longer renders the permanent "unavailable" contextual home.

### 2026-10-29T12:40:00Z — Claude Code

- The deterministic tiers were merged via PR #50. The accepted-menu tier was delivered with PLAN-0029 Phase 1.

### 2026-10-30T18:00:00Z — Claude Code (registry reconciliation, PLAN-0057 Phase 0)

- The "Prepare-se" section on Hoje (PLAN-0029 Phase 4, #71) adds pre-meal reminders after the menu tier.
