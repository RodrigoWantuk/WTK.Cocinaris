# Cocinaris Frontend

Official Cocinaris frontend package living at `apps/frontend`.

## Authority and current state

- This directory is the **only** official frontend source for Cocinaris.
- Imported from Emergent snapshot `69f798f66b7987c4ed785c52c90a5539bf46f52e` (see [`docs/development/emergent-frontend-import-provenance.md`](../../docs/development/emergent-frontend-import-provenance.md)).
- `kitchen-emergent` is a historical snapshot. There is **no** bidirectional sync.
- Emergent and Lovable remain optional generation tools only (ADR-0007).
- PLAN-0015 remediation and browser gates are completed.
- PLAN-0016 is merged and supplies the production BFF session plus authenticated inventory routes.
- PLAN-0011 Phase 1 + Phase 2 deliver the public entry and mock-backed contextual home on `/` and `/app/hoje` (merged via PR #34). Live home sources remain PLAN-0021 (production uses controlled unavailable adapters).
- PLAN-0020 delivers production profile routes under `/app/perfil*` (household, preferences, equipment, completeness) through live adapters.
- The broader initial release is not production-complete. Undeveloped product areas must show truthful unavailable/prototype states until their plans deliver live behavior.

## Stack

- React 19 + TypeScript (`allowJs: false`)
- Create React App + CRACO
- Yarn classic (`packageManager` field)

## Frontend modes (build-time)

Set `REACT_APP_FRONTEND_MODE` at build/start time (validated):

| Mode | Script | Behavior |
|---|---|---|
| `prototype` | `yarn start` / `yarn build:prototype` | ScenarioBar, fixtures, mock session allowed |
| `production` | `yarn build:production` | No scenario tooling; live session/inventory adapters; unavailable adapters for undeveloped areas |
| `test` | `yarn test` | Prototype-compatible composition for Jest |

Composition roots live under `src/app/runtime/`. Providers require injected adapters — no silent mock defaults in production.

## Development

```bash
cd apps/frontend
yarn install --frozen-lockfile
yarn start
```

### Quality scripts

```bash
yarn typecheck
yarn lint
yarn format:check
yarn test
yarn guard:ts-only
yarn guard:build-mode
yarn guard:production-isolation
yarn build
yarn inspect:production-bundle
yarn build:prototype
yarn build:production
yarn audit:policy
```

### Automated browser smoke (Playwright)

Playwright is a **direct** `devDependency`. Do not rely on global installs or absolute browser cache paths.

```bash
cd apps/frontend
yarn install --frozen-lockfile
yarn smoke:browser:install
yarn start            # terminal 1 — prototype mode
yarn smoke:browser    # terminal 2
```

CI uses `yarn smoke:browser:ci` (`SMOKE_MANAGE_SERVER=1`) after the quality job. Any `Failed` / `Blocked` / `Not executed` mandatory automated check exits non-zero.

Historical reports and plan-specific evidence live under the documented evidence paths. New feature plans must produce their own current evidence and must not reuse an old report as proof.

## Mock vs live adapters

| Path | Role |
|---|---|
| `src/adapters/mock/` | Fixture-backed presentation projections for **prototype/test** only (includes contextual-home mocks) |
| `src/adapters/live/` | Live inventory repository and unavailable stand-ins for undeveloped areas (including contextual home) |
| `src/generated/api-client/` | CRA mirror of `@cocinaris/api-client` (do not edit; regenerate) |
| `src/contracts/` | Presentation models (inventory quantity, preparation, contextual home) |
| `src/features/inventory/` | Production inventory screens |
| `src/features/entry/` | Public entry (Phase 1) |
| `src/features/home/` | Contextual home presentation (Phase 2) |
| `src/app/session/` | `SessionAdapter` boundary (`createBffSessionAdapter` in production; mock in prototype) |

Presentation components consume projections. They must **not** perform authoritative inventory, reservation, unit-conversion, recommendation, quota, or safety arithmetic.

## Generated OpenAPI client

Canonical package: `packages/api-client` (generator `openapi-typescript` 7.9.1 + runtime `openapi-fetch` 0.14.0).

```bash
cd apps/frontend
yarn generate:api-client
yarn check:api-client-drift
yarn typecheck:api-client
```

CI fails when the OpenAPI snapshot, package output, or frontend mirror drift.

## Session and API seams

- Browser auth is BFF/session oriented via `POST /api/v1/auth/login`, `GET /api/v1/session`, `POST /api/v1/auth/logout`.
- Cookies use `credentials: "include"`; OIDC tokens are never stored in JavaScript storage.
- Production inventory routes live under `/app/despensa` (list/detail/create/edit/adjust/history).
- Authenticated contextual home lives under `/app/hoje` (mock-backed in prototype/test; unavailable sources in production until PLAN-0021).
- Authenticated profile lives under `/app/perfil*` (live adapters; see [`docs/profile/README.md`](docs/profile/README.md)).
- Production home adapters set `capabilityStatus: "not_implemented"` and `retryable: false` so permanent capability gaps do not show misleading Retry.
- Quick chooser classifies resolved suggestion statuses (`ready`/`empty` complete; failed/unavailable do not emit completion telemetry).
- Chooser definitions are a discriminated union (exactly 1–2 questions when available) with runtime normalization before UI state.
- Titles/labels use `HomeDisplayText` (`catalog` | `literal`) via `renderHomeText` for PLAN-0021-ready dynamic names without inventing live DTOs.
- Public entry demo CTA respects `prefers-reduced-motion`; unknown/missing `matchMedia` uses conservative `auto` scrolling.
- Production does not fall back to mock pantry data, mock home fixtures, or prototype `localStorage` auth.
- Same-origin API base path: `/api/v1` (proxy or reverse-proxy in integrated environments).
- See [`docs/contextual-home/README.md`](docs/contextual-home/README.md) for presentation/adapter boundaries, async invalidation, and suggestion model fields.

### Local integrated run (production mode)

1. Start backend + PostgreSQL + Keycloak per `apps/backend` / infrastructure docs.
2. Serve the production build behind the same origin as the API (or configure a same-origin proxy).
3. Open `/acesso` → backend-managed login → `/app/despensa`.

## Dependency triage

- Vulnerabilities / allowlist: [`docs/dependency-vulnerability-triage.md`](docs/dependency-vulnerability-triage.md) and `audit-allowlist.json`
- Incompatible Yarn resolutions: [`docs/dependency-resolution-triage.md`](docs/dependency-resolution-triage.md)

## CI

GitHub Actions workflow: `.github/workflows/frontend.yml` — required jobs **quality** and **browser-smoke** are blocking.

## TypeScript

Application sources under `src/` must be TypeScript (`.ts`/`.tsx`). Enforce with `yarn guard:ts-only`.

## Agent environment

Before any remote Git or GitHub operation, read the final section of [`../../AGENTS.md`](../../AGENTS.md). On the exact host `NOTEBOOK-DEB-RODRIGO`, use the documented GitHub App wrappers, an `agent/` branch, a draft PR, and request `RodrigoWantuk` review. Never push directly to `main`, merge, or expose credentials.
