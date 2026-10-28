# PLAN-0029: Implement Sequential Planning, Menu, and Shopping Projection

- **Status:** Draft
- **Type:** Implementation
- **Priority:** High
- **Owner:** Unassigned planning/shopping vertical-slice agent
- **Created:** 2026-08-05
- **Last updated:** 2026-08-05
- **Branch:** `agent/plan-0029-sequential-planning-menu-shopping` (when claimed)
- **Pull request:** Not opened
- **Dependency:** PLAN-0028 recipe identity and saved recipe revisions

## Objective

Deliver the first sequential planning slice that turns saved recipes into draft and accepted menu state with shopping projection and the accepted-menu read contract required to unblock PLAN-0021.

## Included scope

- non-authoritative projected inventory;
- sequential candidate evaluation against dated meal slots;
- draft and accepted menu state;
- fixed and flexible menu entries;
- accepted-menu read contract for contextual home;
- shopping projection;
- package-size and expected-surplus reasoning aligned with PLAN-0022 package-confidence rules;
- owner isolation, idempotency, and OpenAPI contracts.

## Excluded scope

- cook-now AI Gateway (PLAN-0028);
- thumbnail generation (PLAN-0030);
- guided cooking execution;
- billing.

## Acceptance criteria (draft)

- [ ] Accepted-menu read contract exists and is consumable by PLAN-0021.
- [ ] Draft vs accepted menu semantics are deterministic and tested.
- [ ] Shopping projection does not mutate physical inventory.
- [ ] Package surplus exposure respects confidence `high` only.

## Execution state

- **Current checkpoint:** Draft placeholder created during PLAN-0028 roadmap reconciliation.
- **Blockers:** PLAN-0028 must deliver saved recipe revisions.
- **Exact next action:** After PLAN-0028 merges, claim this plan and specify API/contracts before implementation.
