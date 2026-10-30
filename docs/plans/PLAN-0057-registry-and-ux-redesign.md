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
| 2 | Design system rewritten for the new direction (emerald accent, layered surfaces, both themes reachable, micro-interactions), then H1–H6 and M1, M4 fixed | Next |
| 3 | Medium findings, consistency pass across screens, owner check on the host | After Phase 2 |

## Decision needed before Phase 2

The accepted design system (2026-10-29) says: plain white ground, a black primary pill, no shadows, gradients, or decorative motion, and color only where it carries meaning. The owner's new guidelines ask for layered neutral surfaces, one accent color (for CTAs, active states, and focus rings), faint rings for depth, and short hover/press motion. These overlap but differ on the primary color, depth, and motion. The owner chooses whether to keep the accepted system and only polish within it, move to the new direction, or combine the two (for example: keep the white ground and rows, add one accent color for focus and active states, and add hover/press feedback, without shadows).

### Owner decision (2026-10-30)

The owner chose **the new guidelines**, replacing the 2026-10-29 visual language, with **emerald** as the single accent color. Phase 2 rewrites `docs/design/design-system.md` accordingly. The emerald accent must stay distinct from the green "you have it" status color (status keeps text labels and a separate token), and it must meet 4.5:1 for text on its fill and 3:1 for focus rings in both themes.

## Acceptance criteria

- [x] Phase 0: every active row in `docs/plan-status.md` matches `main` and the owner's current decisions.
- [x] Phase 1: review covers every route in `app/ProductionApp.tsx`, with severity, location, and a concrete fix per finding.
- [ ] Phase 2: owner-approved direction recorded in `docs/design/design-system.md`; tokens updated in `index.css` and `tailwind.config.js`; critical and high findings fixed with tests; WCAG 2.2 AA contrast and focus in both themes.
- [ ] Phase 3: medium findings fixed or explicitly deferred; deployed and checked by the owner.

## Execution state

- **Current run delivery target:** Phase 0, then Phase 1 in the same branch.
- **Current checkpoint:** Phases 0 and 1 delivered (documentation only). The review is in `docs/design/ux-review-2026-10-30.md`.
- **Validation:** documentation only. The registry was checked against `git log origin/main` and the merged PRs (#47–#71). The review used captures of 19 routes at two sizes in light and forced-dark themes on the proxy replica, and axe-core reported no violations on any of them.
- **Exact next action:** Phase 2: rewrite `docs/design/design-system.md` and the tokens (`index.css`, `tailwind.config.js`) for the emerald accent and layered surfaces, make the theme follow the system with a manual override, update the button, input, and card primitives (hover, press, focus), then fix H1, H2, H3, H6, M1, and M4.
- **Blockers:** None. The owner chose the new guidelines with an emerald accent.

## Progress log

### 2026-10-30T18:00:00Z — Claude Code

- Plan created from the owner's direction. Phase 0 delivered: registry and the affected plans reconciled with `main` at `99481b8`, a reserved-roadmap table added, and the visual-direction question recorded for the owner.

### 2026-10-30T19:30:00Z — Claude Code

- The owner chose the new guidelines with an emerald accent.
- Phase 1 delivered: captured all 19 production routes on the proxy replica (two sizes, both themes) and ran axe (no violations). Wrote the ranked review: 6 high, 8 medium, 3 low findings.
