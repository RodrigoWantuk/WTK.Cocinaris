# PLAN-0036: Migrate the Frontend Toolchain from CRA/CRACO to Vite

- **Status:** Completed
- **Type:** Implementation
- **Priority:** High
- **Owner:** Claude Code (claimed 2026-10-28)
- **Created:** 2026-10-28
- **Last updated:** 2026-10-28
- **Branch:** `agent/plan-0031-0036-foundation-stack` (integrates the former stacked branches with `main` after PR #44)
- **Pull request:** [PR #46](https://github.com/RodrigoWantuk/WTK.Cocinaris/pull/46), merged by the owner at `3d45bee`
- **Numbering:** Created as PLAN-0033. Renumbered to PLAN-0036 on 2026-10-28 because merged PR #44 had already published PLAN-0028–PLAN-0030 ([mapping](PLAN-0031-amendment-2026-10-28-coverage-review.md#renumbering-after-pr-44)).
- **Related ADRs:** ADR-0007; new ADR-0008
- **Dependencies:** PLAN-0035; owner decision D-2 (approved 2026-10-28)

## Objective

Replace Create React App 5 and CRACO, which are deprecated and pull a large unmaintained webpack/babel/jest toolchain, with Vite for development and builds and Vitest for tests. Keep every external contract of `apps/frontend` unchanged: script names, frontend modes, production isolation, guards, output layout under `build/static/{js,css}`, dev server port 3000, and CI steps.

## Scope

### Included

- Vite configuration with production/prototype/test modes, mandatory mode for builds, path aliases, production module replacements for prototype-only modules, and the `build/static` output layout.
- Vitest with jsdom, `mockReset` semantics equal to CRA's `resetMocks`, the jest-dom matchers, and the existing setup file; mechanical conversion of `jest.*` APIs to `vi.*`.
- Root `index.html` (same-origin rules from PLAN-0035 preserved; guard updated).
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
- [x] Browser smoke and the PLAN-0035 review capture run against the Vite build.
- [x] Deploy dry run builds the Vite output.
- [x] ADR-0008 and documentation updated.

## Execution state

- **Current run delivery target:** Complete migration with validation.
- **Current checkpoint:** Migration implemented and validated locally on Node.js 24.22.1. Deploy dry run of `34f1e5e` passed.
  - Builds: production and prototype in about 2.5 s; a build without a mode fails.
  - Bundle: inspection confirms production isolation (631 KB main chunk, down from 677 KB).
  - Guards: all five pass.
  - Tests: Vitest 46 files / 387 tests.
  - Other gates: lint, typecheck and Prettier clean; `audit:policy` passes; API client drift check passes.
  - Browser smoke: 28/28 against the Vite dev server.
  - PLAN-0035 review capture on the Vite build: 0 axe violations, 0 overflow, 0 external hosts, 0 session refetches.
  - Dependencies: lockfile entries 1,454 → 555; resolutions more than 40 → 1; clean `--frozen-lockfile` install with no incompatible-resolution warnings.
- **Defects found and fixed during the migration:**
  - **Production bug (API errors):** `readProblemDetails` re-read response bodies that openapi-fetch had already consumed. Browsers reject `clone()` on a consumed body, so live inventory and profile errors lost their `errorCode` and field errors: a 409 `profile_already_exists` showed as a generic conflict, and 400 validation errors as messages without fields. CRA's test fetch polyfill allowed the clone, which hid it. The generated client's parsed `error` body is now passed through (`packages/api-client/src/problemDetails.ts`, both live repositories). Existing 409/400 tests now act as regression tests under real `Response` semantics.
  - **Undeclared dependency:** `scripts/guard-interactive-nesting.js` required `@babel/parser`, which was available only through `react-scripts`. Now declared (7.29.9).
  - **Browser smoke orphaned its dev server:** the script killed `yarn` but not its child server, and it accepted any server already on the port, so a later run tested a stale orphan. Fixed with a process group teardown and a fail-fast port check.
- **Exact next action:** None. Follow-up work is tracked in the PLAN-0031 coverage review roadmap.
- **Blockers:** None.
- **Known failures or limitations:**
  - Vite warns that the main chunk exceeds 500 KB; code splitting is a later optimization.
  - The Emergent provenance document keeps CRA references as history.
- **Working tree state:** Committed on `agent/plan-0031-0036-foundation-stack`.

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

### 2026-10-28T19:32:00Z — Claude Code

- Deploy dry run of `34f1e5e` passed in 43 s (95 s with CRA). The release has `web/static/{css,js,media}` and the title "Cocinaris", with no external origins. The pre-flight still reports the two rename-migration steps. The unactivated release was removed. `packages/contracts/README.md` wording update included in this commit.

### 2026-10-28T22:20:00Z — Claude Code (PR #44 integration)

- The owner merged PR #44, which had already published PLAN-0028–PLAN-0030. This plan was renumbered from PLAN-0033 to PLAN-0036; `main` was merged into `agent/plan-0031-0036-foundation-stack`.
- Converted the PR #44 recipe tests from Jest to Vitest (`vi.fn`, `vi.spyOn`, `vi.restoreAllMocks`). One test then failed for a real reason: `liveRecipeRepository` called `readProblemDetails(response)` after openapi-fetch had consumed the body, so in the browser every recipe error lost its `errorCode` (for example `ai_budget_exhausted` became `unavailable`). It now passes the parsed `error`, as the inventory and profile repositories do.
- **Validation on the merged tree:** backend format, build with warnings as errors, unit 76/76, architecture 14/14, integration 178/178 (OIDC fixture); migrations applied twice idempotently (10 rows); OpenAPI export matches and lints; API client drift check; frontend lint, typecheck, guards, Vitest 50 files / 410 tests, production build, build-mode guard, audit policy (0 allowlisted).
- **Not passed locally:** the prototype browser smoke timed out on a page reload after 6 passing journeys, twice, while another project on the host kept the load average near 10 on 2 CPUs. The pre-merge commit `ff5f715` also fails with a navigation timeout under the same load, so this is not a regression of the merge. CI runs the smoke on the PR head.

### 2026-10-28T23:05:00Z — Claude Code

- The owner merged PR #46 (`3d45bee`, 2026-10-28T22:41Z). GitHub Actions did not run on it because the owner account is locked for billing; validation is the owner-host run recorded above. Status set to Completed.
