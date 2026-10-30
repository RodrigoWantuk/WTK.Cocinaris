# PLAN-0057: Registry Reconciliation and UX/Design Redesign

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High (owner direction)
- **Owner:** Claude Code (claimed 2026-10-30)
- **Created:** 2026-10-30
- **Last updated:** 2026-10-30
- **Branch:** `agent/plan-0057-registry-and-ux-redesign`
- **Pull request:** Draft, opened with the first commit
- **Related documents:** [`docs/design/design-system.md`](../design/design-system.md), [`docs/design/ux-audit-2026-10-29.md`](../design/ux-audit-2026-10-29.md), [PLAN-0054](PLAN-0054-everyday-cook-realignment.md) (design system and shell), [`docs/product/everyday-cook-realignment-2026-10-29.md`](../product/everyday-cook-realignment-2026-10-29.md)

## Owner direction (2026-10-30)

"Comece atualizando o registro de planos para o que temos hoje já, ou definições que mudaram. Depois, redesign."

Earlier the same day the owner asked for a general UX and design pass after many feature slices (PLAN-0029, PLAN-0042, PLAN-0055, PLAN-0056) added screens and panels. The owner also wrote design guidelines for UI work: layered neutral surfaces, one restrained accent color, subtle borders and rings, a strict 4/8px spacing grid, short micro-interactions (hover lift, press scale, accent focus ring), mandatory loading/empty/error/success states, and accessible headless primitives. They are kept in the owner's agent skill, outside the repository, and are summarized here so the plan stands on its own.

## Phases

| Phase | Outcome | Status |
| --- | --- | --- |
| 0 | Registry reconciled with `main` at `99481b8`: stale rows fixed (PLAN-0021, PLAN-0029, PLAN-0037, PLAN-0042, PLAN-0053, PLAN-0054, PLAN-0055), PLAN-0029 completed, PLAN-0054 paused (household deferred), PLAN-0053 blocked on the owner's `AGENTS.md` change, and a reserved-roadmap table for IDs without plan files | Delivered |
| 1 | UX and design review of every production screen (phone and desktop, light and dark, keyboard, contrast, states, copy in three languages), ranked by severity, in `docs/design/ux-review-2026-10-30.md` | Delivered: 6 high, 8 medium, 3 low findings; axe clean on 19 routes |
| 2 | Design system rewritten for the new direction (emerald accent, layered surfaces, both themes reachable, micro-interactions), then H1–H6 and M1, M4 fixed | Delivered (H3 partly: see below) |
| 3 | Remaining H3 (task buttons and chips instead of action selects), M2, M3, M5–M8, L1–L3, owner check on the host | Next |

## Decision needed before Phase 2

The accepted design system (2026-10-29) says: plain white ground, a black primary pill, no shadows, gradients, or decorative motion, and color only where it carries meaning. The owner's new guidelines ask for layered neutral surfaces, one accent color (for CTAs, active states, and focus rings), faint rings for depth, and short hover/press motion. These overlap but differ on the primary color, depth, and motion. The owner chooses whether to keep the accepted system and only polish within it, move to the new direction, or combine the two (for example: keep the white ground and rows, add one accent color for focus and active states, and add hover/press feedback, without shadows).

### Owner decision (2026-10-30)

The owner chose **the new guidelines**, replacing the 2026-10-29 visual language, with **emerald** as the single accent color. Phase 2 rewrites `docs/design/design-system.md` accordingly. The emerald accent must stay distinct from the green "you have it" status color (status keeps text labels and a separate token), and it must meet 4.5:1 for text on its fill and 3:1 for focus rings in both themes.

## Phase 2 scope delivered

- **Design system** (`docs/design/design-system.md`, `index.css`):
  - Zinc layered surfaces: ground zinc-50/950, cards white/zinc-900, elevated layers.
  - Emerald accent: primary, focus ring, and selected state, with contrast ratios recorded per token.
  - Input boundaries at 3:1; the `surface` and `tabular` utilities.
  - Buttons in `rounded-xl` with hover feedback, a press scale under `motion-safe`, and a focus ring offset from the page.
  - Inputs that show focus with the `ring` border.
- **Themes (H4):** the theme follows the system by default, with "Tema: Sistema, Claro, Escuro" in the account menu. The choice is stored per device (`app/theme/theme.ts`) and applied before the first render.
- **Account avatar (M4):** a person icon instead of "?".
- **"O que cozinho agora?" (H1):**
  - Plain copy and the "Sugerir receitas" button.
  - Meal kind and time are visible chips; families, cuisine, and pantry items sit behind "Mais opções".
  - Candidates are now "Escolha uma receita" with "Quero esta".
- **Recipe page (H2):** new `GET /api/v1/recipes/{recipeId}/availability`. It uses the cooking proposal's deterministic matching at the recipe's servings and is read-only. Each ingredient shows "Na despensa", "Faltam …", or "Não está na despensa", and assumed basics get no status.
- **Pantry item (H3, part):**
  - Empty fields are hidden, and the printed-date disclaimer shows only with a date.
  - "Usei ou corrigi" comes before "Abri, congelei ou mudei de lugar".
  - Reservations and protection sit behind a disclosure.
  - Delete is a quiet action at the bottom (it still confirms).
  - Panels use `surface`.
  - **Not yet:** task buttons and chips instead of the two action selects (Phase 3, with L2).
- **Entry page (H6):** the copy is written in the cook's words. Steps are numbered 1–5. The legal links read "(em breve)", and the note says the documents will be published before launch.
- **Hoje (M1):**
  - Cooking in progress comes first, on an accent surface.
  - The main-action card is now "Sem ideia do que fazer?" (no longer a repeat of the page question).
  - A reminder shows its due-day chip only when it differs from the meal day.

## Acceptance criteria

- [x] Phase 0: every active row in `docs/plan-status.md` matches `main` and the owner's current decisions.
- [x] Phase 1: review covers every route in `app/ProductionApp.tsx`, with severity, location, and a concrete fix per finding.
- [x] Phase 2: owner-approved direction recorded in `docs/design/design-system.md`; tokens updated in `index.css`; high findings fixed with tests (H3 partly); WCAG 2.2 AA contrast ratios recorded per token; axe clean on all 19 routes in light (dark checked visually).
- [ ] Phase 3: medium findings fixed or explicitly deferred; deployed and checked by the owner.

## Execution state

- **Current run delivery target:** Phases 0–2 in this branch (owner: registry first, then redesign).
- **Current checkpoint:** Phases 0–2 delivered; see "Phase 2 scope delivered".
- **Validation (Phase 2, lean and targeted):**
  - **Backend:** architecture tests 14/14; `RecipeExecutionTests` 5/5; integration tests 12/12. The integration run includes the new `RecipeAvailabilitySaysWhatThePantryCoversWithoutChangingIt` (statuses, no pantry change, another owner gets 404), cooking adjustments, reservations, and the contract tests. `dotnet format` is clean.
  - **OpenAPI:** regenerated with additions only (`/api/v1/recipes/{recipeId}/availability`); the client drift check passed.
  - **Frontend:** typecheck, ESLint, and Prettier are clean. Under `src/features` and `src/app`, 318 of 319 tests passed, including the new theme, account-avatar, and recipe-availability tests.
  - **Flaky test, not caused by this change:** `ContextualHomePage > ignores stale inventory responses after context change` belongs to the prototype home, which this change does not touch. It failed in 1 of 2 full-suite runs and in 1 of 4 single-file runs, and passed 4/4 when run alone. On the base it passed 4/4.
  - **Visual:** the replica captures of all 19 routes at two sizes in both themes were reviewed. axe reported no violations on any route in light; it was not run in dark.
- **Not run:** full suites, a screen-reader pass, and an axe run in the dark theme.
- **Exact next action:** Owner reviews draft PR #72 (and may deploy the branch for a look). Phase 3: task buttons and chips on the pantry item (rest of H3, L2), then M2, M3, M5–M8, L1, L3.
- **Blockers:** None. The owner chose the new guidelines with an emerald accent.

## Progress log

### 2026-10-30T18:00:00Z — Claude Code

- Plan created from the owner's direction. Phase 0 delivered: registry and the affected plans reconciled with `main` at `99481b8`, a reserved-roadmap table added, and the visual-direction question recorded for the owner.

### 2026-10-30T19:30:00Z — Claude Code

- The owner chose the new guidelines with an emerald accent.
- Phase 1 delivered: captured all 19 production routes on the proxy replica (two sizes, both themes) and ran axe (no violations). Wrote the ranked review: 6 high, 8 medium, 3 low findings.

### 2026-10-30T21:00:00Z — Claude Code

- Phase 2 delivered: new design system (Zinc layers, emerald accent, micro-interactions), themes following the system with a manual choice, recipe availability endpoint and statuses, plainer suggestion and entry copy, simpler pantry item page, Hoje order. One pre-existing flaky prototype test recorded.
