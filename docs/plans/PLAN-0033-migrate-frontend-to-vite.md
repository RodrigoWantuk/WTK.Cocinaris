# PLAN-0033: Migrate the Frontend Toolchain from CRA/CRACO to Vite

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0033-vite-migration` (stacked on PLAN-0032)
- **Pull request:** Not opened
- **Related ADRs:** ADR-0007; new ADR-0008
- **Dependencies:** PLAN-0032; owner decision D-2 (approved 2026-10-28)

## Objective

Replace Create React App 5 and CRACO, which are deprecated and pull a large unmaintained webpack/babel/jest toolchain, with Vite for development and builds and Vitest for tests. Keep every external contract of `apps/frontend` unchanged: script names, frontend modes, production isolation, guards, output layout under `build/static/{js,css}`, dev server port 3000, and CI steps.

## Scope

### Included

- Vite configuration with production/prototype/test modes, mandatory mode for builds, path aliases, production module replacements for prototype-only modules, and the `build/static` output layout.
- Vitest with jsdom, `mockReset` semantics equal to CRA's `resetMocks`, the jest-dom matchers, and the existing setup file; mechanical conversion of `jest.*` APIs to `vi.*`.
- Root `index.html` (same-origin rules from PLAN-0032 preserved; guard updated).
- Removal of `react-scripts`, `@craco/craco`, the CRA babel plugin, `craco.config.js`, the Emergent webpack health-check plugin, `react-app-env.d.ts`, and every `resolutions` entry that existed only for CRA transitive dependencies.
- ADR-0008, frontend README, environment and dependency-triage documentation.

### Excluded

- React Router 8 (no longer needed for security; separate decision).
- UI changes.

## Acceptance criteria

- [ ] `yarn start`, `yarn build`, `yarn build:production`, `yarn build:prototype`, `yarn test`, `yarn typecheck`, `yarn lint` work with the same names and semantics; builds without a mode fail.
- [ ] Production build contains no prototype modules (`inspect:production-bundle`, `guard:production-isolation`) and no third-party origins (`guard:third-party-html`).
- [ ] All existing tests pass under Vitest without weakening assertions.
- [ ] `audit:policy` passes and the remaining dependency tree is substantially smaller.
- [ ] Browser smoke and the PLAN-0032 review capture run against the Vite build.
- [ ] Deploy dry run builds the Vite output.
- [ ] ADR-0008 and documentation updated.

## Execution state

- **Current checkpoint:** Plan created; migration not started.
- **Exact next action:** Add Vite/Vitest, write `vite.config.ts`, move `index.html`, convert tests.
- **Blockers:** None.

## Progress log

### 2026-10-28T19:00:00Z — Claude Code

- Plan created after owner approval of D-2. Versions chosen (published more than two weeks earlier, mutually compatible, Node.js 24): Vite 8.3.4, `@vitejs/plugin-react` 6.1.2, Vitest 5.0.4, jsdom 30.1.2.
