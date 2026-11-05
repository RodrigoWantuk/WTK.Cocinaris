# PLAN-0063: One Way into Planning ("Planejar")

- **Status:** Validating
- **Type:** Implementation
- **Priority:** High (owner request)
- **Owner:** Claude Code
- **Created:** 2026-11-05
- **Last updated:** 2026-11-05
- **Branch:** `agent/plan-0063-planning-entry`
- **Pull request:** Draft, opened with this file
- **Related issues:** None
- **Related ADRs:** ADR-0012
- **Dependencies:** PLAN-0062 (merged)

## Objective

Make planning one thing with one name. The owner found the entry points confusing: "Compras", "Lista de compras", and "Planejar compras" suggested a place to register loose items, and the guided flow was hard to find.

## Owner decisions (2026-11-05)

| Topic | Decision |
|---|---|
| Tab and central button | "Planejar" |
| Page title | "Planejamento" |
| "Lista de compras" | Becomes "O que comprar" |
| "Cardápio" | Stays |
| Tab behavior | Opens a page that leads with the next step, not the list |

This replaces part of the navigation approved in PLAN-0054 direction 2: menu pages used to light up "Hoje", and the fifth tab was "Compras".

## Scope

### Included

- `PlanningHubPage` at `/app/compras`, with one main action per stage: start, continue a plan, continue a draft, what to buy, market arrival, preparation session, or the menu.
- One tile on the home page, hinting at the next step, replacing "Cardápio" and "Lista de compras".
- The tab, the central button, the tile, and the welcome step all lead to that page.
- Everything about a plan lights up the "Planejar" tab.
- Copy in pt-BR, en, and es.

### Excluded

- Changes to the backend, plans, lists, or reservations.
- URL changes. `/app/compras` and `/app/cardapio` stay, because the offline shell and installed apps depend on them, and people rarely see them.
- Replacing the word "plano" inside the guided flow.

## Hazards found

- **The offline list was saved by opening the tab.** The tab no longer opens the list, so someone could reach the market with nothing saved. The overview now saves the current list whenever the planning page or the home page loads it. A signed-in owner may arrive after the read starts, so the save reads the provider through a ref.
- **More reads on the home page.** The tile adds the menu list, the plan start, the shopping list, and the route link. Any failure falls back to the plain invitation.

## Acceptance criteria

- [x] The tab, the central button, and the home tile reach one page.
- [x] The page leads with the next step, and the tab no longer opens a list.
- [x] Each stage's main action is correct, and a failed read never claims nothing is missing.
- [x] Every planning page lights up "Planejar".
- [x] "Lista de compras" no longer appears as a label. "Cardápio" stays.
- [x] The list opens offline without having been opened first.
- [ ] The owner confirms the new entry feels clear on a phone.

## Execution state

- **Current run delivery target:** The planning entry points, complete.
- **Current checkpoint:** Implemented and validated on the replica, in a draft pull request.
- **Last completed step:** Frontend gates; see the log.
- **Exact next action:** The owner reviews and merges, then asks for a deploy and tries it on a phone.
- **Blockers:** None.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan, the registry, the design system, the product spec, and user journeys.
- **Validation performed:** See the log.
- **Known failures or limitations:** See the log.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-05 — Claude Code

- **Run delivery target:** The whole change, from the owner's report to a draft pull request.
- **Changes:** `PlanningHubPage`, `planningOverview.ts`, `AppNavigation`, `QuickActionMenu`, `MenusPage`, `TodayPage`, the catalogs, and `scripts/e2e/planning-entry-journey.mjs`. `offline-list-journey.mjs` now opens the list by its address when online.
- **Decisions beyond the owner's:**
  - The central button goes to the page, not straight to a new plan, so one name means one place. Someone with a plan in use needs one more tap to start another.
  - The guided flow's button says "Começar um plano", and its title says "Novo plano". The result is still called a "cardápio".
  - `today.quick.plan` hints at the next step on phones too, so the tile's detail line is now visible at every width.
- **Validation performed:**
  - Frontend: typecheck, lint, format, and tests.
  - `planning-entry-journey.mjs` on the proxy replica: 16 of 16, including 320 px and the list opening offline without having been opened.
  - `shopping-plan-journey.mjs`: 18 of 18, before the offline save was added.
  - `offline-list-journey.mjs`: 25 checks passed, none failed.
  - Screens read at 390 px: home, central button, and the page in two stages.
- **Not run:** Screen reader, 200% zoom, a real phone, and the page in en and es in a browser. `frontend-smoke` does not finish on this host (PLAN-0062 log). The backend is untouched, so its gates did not run.
- **Limitations:** Someone offline who never loaded the planning or home page while online still has no saved list. The prototype build keeps its own "Compras" labels, because `lib/i18n.ts` belongs to it. The word "plano" still appears in the guided flow alongside "cardápio".
- **Next action:** See "Exact next action".

## Completion and handoff checklist

- [x] Tests and documentation are current.
- [x] `docs/plan-status.md` matches this plan.
- [ ] The owner has tried it on a phone.
