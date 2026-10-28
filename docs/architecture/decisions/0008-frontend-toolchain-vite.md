# ADR-0008 — Frontend Toolchain: Vite and Vitest

- **Status:** Accepted
- **Date:** 2026-10-28
- **Decision owner:** Repository owner (decision D-2, recorded in the PLAN-0028 coverage-review amendment)
- **Plan:** [PLAN-0033](../../plans/PLAN-0033-migrate-frontend-to-vite.md)
- **Amends:** ADR-0007, which preserved the imported Create React App + CRACO runtime "unless a later ADR changes it"

## Context

The frontend imported from Emergent ran on Create React App 5 with CRACO. CRA is deprecated and unmaintained. Its webpack, babel and Jest 27 toolchain accounted for most of the dependency tree (1,454 lockfile entries), more than 40 dependency-audit resolutions, and repeated advisory churn (PLAN-0015, PLAN-0029). It also hid defects:
- CRA's jsdom environment shipped a fetch polyfill that allowed `clone()` on consumed bodies, so tests passed while browsers dropped error codes and field errors from failed API calls;
- a guard script depended on `@babel/parser` only transitively.

## Decision

- Use **Vite** (8.x) for the development server and production builds, with `@vitejs/plugin-react`.
- Use **Vitest** (5.x, jsdom environment, `mockReset` equivalent to CRA's `resetMocks`) for unit and component tests.
- Keep the external contracts of `apps/frontend` stable:
  - `REACT_APP_FRONTEND_MODE` (prototype/production/test), mandatory for builds;
  - script names (`start`, `build`, `build:production`, `build:prototype`, `test`);
  - output under `build/static/{js,css,media}`;
  - dev server port 3000;
  - production isolation of prototype-only modules (`vite.config.ts` plugin replacing the former webpack `NormalModuleReplacementPlugin`).
- Application code keeps reading `process.env.REACT_APP_FRONTEND_MODE` and `process.env.NODE_ENV`, which Vite `define` replaces at build time, so no runtime code depends on the bundler.
- The Vite entry `index.html` lives at the package root and loads only same-origin content (PLAN-0032 guard).

## Alternatives considered

- **Keep CRA/CRACO:** rejected; unmaintained, large advisory surface, Node.js 24 friction.
- **Next.js / React Router framework mode:** rejected; they add server rendering and routing conventions that ADR-0001/0007 and the backend-managed session boundary do not need.
- **Vite with Jest kept:** rejected; it would retain a separate babel transform pipeline. Vitest shares the Vite config and aliases.

## Consequences

- Dependency tree reduced from 1,454 to 555 lockfile entries; no audit resolutions remain except the React Router alignment pin.
- Production builds take a few seconds instead of about a minute.
- Tests run on Node's real `Request`/`Response`. A test setup shim resolves relative request URLs against the document, as browsers do.
- Vitest 5 requires Node.js 22.12+ or 24, matching the Node.js 24 baseline of all workflows and the owner host.
- React Router 8 adoption is now unblocked but remains a separate decision.

## References

- Vite: https://vite.dev/guide/
- Vitest: https://vitest.dev/guide/
