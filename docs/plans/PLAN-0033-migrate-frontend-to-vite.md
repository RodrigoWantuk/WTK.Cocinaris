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

- [x] `yarn start`, `yarn build`, `yarn build:production`, `yarn build:prototype`, `yarn test`, `yarn typecheck`, `yarn lint` work with the same names and semantics; builds without a mode fail.
- [x] Production build contains no prototype modules (`inspect:production-bundle`, `guard:production-isolation`) and no third-party origins (`guard:third-party-html`).
- [x] All existing tests pass under Vitest without weakening assertions.
- [x] `audit:policy` passes and the remaining dependency tree is substantially smaller.
- [x] Browser smoke and the PLAN-0032 review capture run against the Vite build.
- [ ] Deploy dry run builds the Vite output.
- [x] ADR-0008 and documentation updated.

## Execution state

- **Current run delivery target:** Complete migration with validation.
- **Current checkpoint:** Migration implemented and validated locally on Node.js 24.22.1. Remaining: deploy dry run of the committed head.
  - Builds: production and prototype in about 2.5 s; a build without a mode fails.
  - Bundle: inspection confirms production isolation (631 KB main chunk, down from 677 KB).
  - Guards: all five pass.
  - Tests: Vitest 46 files / 387 tests.
  - Other gates: lint, typecheck and Prettier clean; `audit:policy` passes; API client drift check passes.
  - Browser smoke: 28/28 against the Vite dev server.
  - PLAN-0032 review capture on the Vite build: 0 axe violations, 0 overflow, 0 external hosts, 0 session refetches.
  - Dependencies: lockfile entries 1,454 → 555; resolutions more than 40 → 1; clean `--frozen-lockfile` install with no incompatible-resolution warnings.
- **Defects found and fixed during the migration:**
  - **Production bug (API errors):** `readProblemDetails` re-read response bodies that openapi-fetch had already consumed. Browsers reject `clone()` on a consumed body, so live inventory and profile errors lost their `errorCode` and field errors: a 409 `profile_already_exists` showed as a generic conflict, and 400 validation errors as messages without fields. CRA's test fetch polyfill allowed the clone, which hid it. The generated client's parsed `error` body is now passed through (`packages/api-client/src/problemDetails.ts`, both live repositories). Existing 409/400 tests now act as regression tests under real `Response` semantics.
  - **Undeclared dependency:** `scripts/guard-interactive-nesting.js` required `@babel/parser`, which was available only through `react-scripts`. Now declared (7.29.9).
  - **Browser smoke orphaned its dev server:** the script killed `yarn` but not its child server, and it accepted any server already on the port, so a later run tested a stale orphan. Fixed with a process group teardown and a fail-fast port check.
- **Exact next action:** Deploy dry run on this branch's head; then CI on the PR head once pushes are enabled.
- **Blockers:** Push/PR awaits the owner's `AGENTS.md` §13 change (PLAN-0031).
- **Known failures or limitations:**
  - Vite warns that the main chunk exceeds 500 KB; code splitting is a later optimization.
  - The Emergent provenance document keeps CRA references as history.
- **Working tree state:** Committed on `agent/plan-0033-vite-migration`.

## Progress log

### 2026-10-28T19:00:00Z — Claude Code

- Plan created after owner approval of D-2. Versions chosen (published more than two weeks earlier, mutually compatible, Node.js 24): Vite 8.3.4, `@vitejs/plugin-react` 6.1.2, Vitest 5.0.4, jsdom 30.1.2.

### 2026-10-28T20:00:00Z — Claude Code

- **Checkpoint:** Migration implemented and validated (see execution state).
- **Changes:**
  - Toolchain: `vite.config.mts` (modes, aliases, production isolation plugin, `build/static` layout, Vitest config); root `index.html`; removed `craco.config.js`, `plugins/` and `react-app-env.d.ts`.
  - Dependencies and scripts: package.json scripts and dependencies; tsconfig (`bundler` resolution, Vitest types, `@types/node`).
  - Tests: `jest.*` → `vi.*`; two dead `react-router-dom` passthrough mocks removed; `setupTests.ts` (Vitest jest-dom entry, relative `Request` shim).
  - Fixes: problem-details fix; browser smoke teardown; PLAN-0005 script test filter.
  - Docs: ADR-0008, ADR-0007 note, ADR index, READMEs, dependency triage documents.
- **Validation:** All frontend gates, smoke 28/28, review capture as above.

