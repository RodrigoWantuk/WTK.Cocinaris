# PLAN-0035: Review and Improve Production Interface and Navigation

- **Status:** Validating
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** Draft PR from `agent/plan-0031-0036-foundation-stack` (one consolidated PR for PLAN-0031–PLAN-0036)
- **Numbering:** Created as PLAN-0032. Renumbered to PLAN-0035 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Related ADRs:** ADR-0007
- **Dependencies:** PLAN-0033

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
- Bundler migration (PLAN-0036).

## Acceptance criteria

- [x] Review findings recorded in this plan, each with a decision (fixed, deferred with owner plan, or rejected with reason).
- [x] Every fixed finding has a test: Jest for behavior and copy (UI-02, 04–07, 12, 15–17, guard for UI-01/03); the axe and overflow capture for visual and structural accessibility (UI-08–11, 13, 14).
- [x] Frontend gates pass; axe reports no serious or critical violations on production routes.
- [x] Screenshots before and after are stored under `docs/evidence/plan-0035/` (360 px kept; full reports for all viewports).

## Findings and decisions

Captured with `apps/frontend/scripts/capture-production-review.js` (production build, synthetic contract-shaped API responses, Chromium, 360/768/1280 px, axe-core at 1280 px). Reports: `docs/evidence/plan-0035/{before,after}/report.json`.

| ID | Severity | Finding | Decision |
|---|---|---|---|
| UI-01 | **P0 privacy** | `public/index.html` (all builds, and the live deployment) loaded `assets.emergent.sh/scripts/emergent-main.js` and initialized PostHog **with session recording** under an Emergent project key, so authenticated pages (pantry, profile, restrictions) could be recorded by a third party | **Fixed.** Entry page rewritten with same-origin content only; new `yarn guard:third-party-html` in CI; deploy script refuses external origins; live file hot-fixed 2026-10-28 15:29Z (backup in `/var/backups/wtk-cocinaris/`) |
| UI-02 | P1 | Page title "Emergent \| Fullstack App" on every page; Emergent meta description | **Fixed.** Localized per-route `document.title` (`page.title.*`), Cocinaris description |
| UI-03 | P1 privacy | Fonts loaded from Google Fonts (`@import` in CSS and a `<link>`), disclosing every visitor to a third party | **Fixed.** Lora and Manrope bundled via `@fontsource-variable` 5.3.0 (OFL-1.1); license audit updated |
| UI-04 | P1 | Each `/app` destination mounted its own session and inventory providers, so navigation refetched the session and blanked the shell ("…") | **Fixed.** Single `/app` layout route (session, inventory, auth guard, unsaved-changes coordinator, shell). `ProfileProvider` stays scoped to `/app/perfil*`. Session fetches during navigation went from 2 to 0 in the capture |
| UI-05 | P1 | Home showed developer text to users ("…(PLAN-0021)… /app/despensa") and three identical "this source is not available yet" sections, contradicting the "skip without error" rule | **Fixed.** One consolidated notice with links to pantry and profile when all personalized sources are unavailable; product spec updated |
| UI-06 | P1 | Lot history rendered raw enums ("Transaction · Initial"): the catalog expected `LifecycleTransaction` and lacked `Initial`, `Deleted` and `Preparation*` types the backend emits | **Fixed.** Labels added in three locales; contract test lists every backend kind/type |
| UI-07 | P1 | Countable quantities showed "units" in Portuguese and Spanish (locale catalogs spread English, hiding missing keys) | **Fixed.** `pcs`/`un.`/`ud.`; new test fails when a pt-BR/es value silently equals English unless allowlisted as language-neutral |
| UI-08 | P2 a11y | axe critical: profile code-list `<select>` without accessible name | **Fixed.** `aria-label` and `aria-describedby` |
| UI-09 | P2 a11y | axe critical: preference category tabs had no tab panel (`aria-controls` pointed nowhere) | **Fixed.** Category content wrapped in `TabsContent` |
| UI-10 | P2 a11y | axe serious: muted text 4.33:1 on muted surfaces | **Fixed.** `--muted-foreground` lightness 42% → 39% (≥ 4.87:1) |
| UI-11 | P2 a11y | axe serious: non-`dt`/`dd` child inside the lot detail `<dl>` | **Fixed.** Disclaimer moved after the list |
| UI-12 | P2 a11y | No skip link; access page without `main` landmark; unknown `/app/*` page without `h1` | **Fixed.** Skip link to `#main-content`; `main` on access states; "Coming soon" page with `h1` and a link home |
| UI-13 | P2 | Horizontal scroll at 360 px on entry (header CTA arrow), profile data and equipment (code-list selects) | **Fixed.** Responsive header CTA; full-width selects on narrow screens |
| UI-14 | P2 | Mobile header used three rows; `main` used 32 px side padding at 360 px | **Fixed.** Two-row header (brand + account controls, then navigation); 16 px gutters on small screens |
| UI-15 | P3 | Session loading state was an unlabeled "…" | **Fixed.** Localized status message |
| UI-16 | P3 | Developer copy in access and unavailable messages ("backend-managed challenge", "no fallback to mock data") and in the pantry subtitle ("loaded from the API") | **Fixed.** User-facing copy in three locales |
| UI-17 | P3 | Public entry showed a "reserved space for future video" box and a technical footer line | **Fixed.** Removed. Media remains optional per spec; the legal placeholder links remain until legal review |
| UI-18 | P3 | "Help me choose" stays visible on the home although the production chooser source is unavailable | **Deferred** to PLAN-0021, which owns the chooser source and its unavailable state |
| UI-19 | P3 | Lot list does not show printed date or package state, which would help attention | **Deferred** to PLAN-0037 (attention engine defines what to surface without implying safety) |
| UI-20 | Info | Cloudflare injects its Web Analytics script at the edge (`static.cloudflareinsights.com`); not part of the repository | **Owner action:** disable Web Analytics for the zone if session privacy requires it (documented in the deployment runbook) |

## Execution state

- **Current run delivery target:** Review and fix worthwhile interface and navigation issues in one run.
- **Current checkpoint:** All fixed findings implemented and validated locally. Capture after the changes:
  - axe at 1280 px: 7 violations (2 critical, 3 serious) → **0**;
  - horizontal overflow at 360 px: 3 routes → **0**;
  - third-party request hosts: 4 → **0**;
  - per-route titles present;
  - session refetches while navigating: **0**.
- **Last completed step:** Frontend gates on Node.js 24: typecheck, lint, Prettier, five guards, Jest 46 suites / 387 tests, production build, bundle inspection, `audit:policy`.
- **Exact next action:** Final CI on the draft PR head from `agent/plan-0031-0036-foundation-stack`; owner merge.
- **Blockers:** None. Owner merge determines delivery state only.
- **Known failures or limitations:**
  - Manual screen-reader passes (NVDA/VoiceOver) remain owned by PLAN-0052.
  - The live deployment still runs the pre-rename build, with only the entry page hot-fixed. Fonts there still come from Google until this branch is merged and deployed.
- **Working tree state:** Committed on `agent/plan-0031-0036-foundation-stack`.

## Progress log

### 2026-10-28T14:45:00Z — Claude Code

- Plan created from owner instruction.

### 2026-10-28T18:45:00Z — Claude Code

- **Checkpoint:** Review captured, 17 findings fixed, 2 deferred with owners, 1 owner action.
- **Changes:** `public/index.html`, `scripts/guard-third-party-html.js` (+ CI step), bundled fonts, `ProductionApp.tsx` layout route/title/skip link/header, home consolidated notice, inventory and shell catalogs, history labels, code-list select, preference tab panel, muted token, lot detail list, entry page cleanup. New tests: `ProductionShellNavigation.test.tsx`, catalog fallback and history-contract tests, updated home/entry/i18n tests. Capture script and evidence.
- **Validation:** All frontend gates green; capture metrics as in execution state.
- **Next action:** Deploy dry run on this head.

### 2026-10-28T18:15:00Z — Claude Code

- Deploy dry run of `4c1cc72` (this plan's implementation head) succeeded: release built with API, web and migration script; the entry page has only same-origin content and the title "Cocinaris". The pre-flight correctly reported the two rename-migration steps a real deploy needs (unit update, `KITCHENFLOW_*` keys). The unactivated dry-run release was removed afterwards; the live deployment was not touched.
- Corrected the acceptance note: UI-09 is covered by the axe capture, not by a dedicated Jest test.

### 2026-10-28T22:20:00Z — Claude Code (PR #44 integration)

- The owner merged PR #44, which had already published PLAN-0028–PLAN-0030. This plan was renumbered from PLAN-0032 to PLAN-0035; `main` was merged into `agent/plan-0031-0036-foundation-stack`.
- PR #44 added the recipe routes as separate top-level routes, each with its own session and shell, which is the pattern UI-01 removed. They are now children of the single authenticated layout route, and pages have titles (`page.title.recipes`, `recipeGenerate`, `recipeDetail`) in the three locales. The shell test uses `/app/compras` for the "coming soon" case because `/app/receitas` now exists. Evidence moved to `docs/evidence/plan-0035/`.
- **Validation on the merged tree:** backend format, build with warnings as errors, unit 76/76, architecture 14/14, integration 178/178 (OIDC fixture); migrations applied twice idempotently (10 rows); OpenAPI export matches and lints; API client drift check; frontend lint, typecheck, guards, Vitest 50 files / 410 tests, production build, build-mode guard, audit policy (0 allowlisted).
- **Not passed locally:** the prototype browser smoke timed out on a page reload after 6 passing journeys, twice, while another project on the host kept the load average near 10 on 2 CPUs. The pre-merge commit `ff5f715` also fails with a navigation timeout under the same load, so this is not a regression of the merge. CI runs the smoke on the PR head.
