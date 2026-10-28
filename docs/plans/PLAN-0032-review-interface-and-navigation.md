# PLAN-0032: Review and Improve Production Interface and Navigation

- **Status:** Ready
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0032-interface-navigation` (stacked on PLAN-0031)
- **Pull request:** Not opened
- **Related ADRs:** ADR-0007
- **Dependencies:** PLAN-0030

## Objective

Review the production frontend (public entry, contextual home, inventory, profile, application shell and navigation) as a whole, after PLAN-0011, PLAN-0016 and PLAN-0020 delivered it in separate slices. Fix navigation, consistency, accessibility and responsive issues that are worth fixing now, without inventing capabilities that do not exist yet.

## Method

- Render every production route with Playwright (Chromium) at 360, 768 and 1280 px against the production build, with the backend API intercepted by synthetic responses, and capture screenshots for review.
- Review information architecture, navigation reachability, labels, active state, focus order, landmarks, headings, empty/error/loading states, and copy against `product/entry-and-contextual-home.md`, `testing/product-foundation-gates.md` (localization and accessibility gates) and architecture principle 17.
- Run axe-core on each route.

## Scope

### Included

- Production shell and navigation: visible, consistent primary navigation for delivered destinations; current-page indication; skip link; mobile behavior.
- Consistency of page titles (`document.title`), headings and landmarks.
- Copy fixes in all three locales.
- Accessibility defects found by review or axe.
- Tests for every behavior change, and updated TSDoc where contracts change.

### Excluded

- New product capabilities (recipes, planning, shopping, cooking) and production links to prototype-only routes.
- Visual redesign or design-system replacement.
- Bundler migration (PLAN-0033).

## Acceptance criteria

- [ ] Review findings recorded in this plan, each with a decision (fixed, deferred with owner plan, or rejected with reason).
- [ ] Every fixed finding has a test.
- [ ] Frontend gates pass; axe reports no serious or critical violations on production routes.
- [ ] Screenshots before and after are stored under `docs/evidence/plan-0032/`.

## Execution state

- **Current checkpoint:** Not started.
- **Exact next action:** Build the production bundle and run the Playwright capture script with synthetic API responses.
- **Blockers:** None.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner instruction.
