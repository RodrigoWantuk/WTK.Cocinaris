# Emergent Frontend Import Provenance

This record documents the one-time import of the final Emergent frontend snapshot into the official Cocinaris monorepo.

## Source

| Field | Value |
|---|---|
| Repository | `RodrigoWantuk/kitchen-emergent` |
| Branch | `main` |
| Commit | `69f798f66b7987c4ed785c52c90a5539bf46f52e` |
| Commit subject | Auto-generated changes |
| Commit timestamp (author) | 2026-07-31 19:46:57 +0000 |
| Source path | `frontend/` |
| Import date (UTC) | 2026-07-31T20:17:24Z |
| Destination | `WTK.Cocinaris/apps/frontend/` |
| Plan | [PLAN-0014](../../plans/PLAN-0014-integrate-emergent-frontend.md) |
| ADR | [ADR-0007](../decisions/0007-frontend-platform-monorepo-and-generation-tooling.md) |

## Method

Auditable filtered filesystem copy:

1. Verified local clone checkout at commit `69f798f66b7987c4ed785c52c90a5539bf46f52e`.
2. Removed the previous placeholder contents of `apps/frontend/`.
3. Copied `$PATH_KITCHEN_EMERGENT/frontend/.` into `apps/frontend/` with `cp -a`.
4. Confirmed the result is not nested as `apps/frontend/frontend/`.
5. Confirmed required roots exist: `package.json`, `src/`, `public/`.

No `git subtree` merge was used because only the `frontend/` subtree is authoritative and the Emergent repository will not remain a sync remote.

## Included paths

Everything under Emergent `frontend/` except installed dependencies and local env files:

- `package.json`, `components.json`, `craco.config.js`, `jsconfig.json`, `postcss.config.js`, `tailwind.config.js`, `.gitignore`
- `public/`
- `src/`
- `plugins/`
- `README.md` (CRA template README from the snapshot)

## Excluded paths (never imported)

From the Emergent repository root and elsewhere:

- `backend/`
- `.emergent/`
- `memory/`
- `test_reports/`
- `tests/` (repository-level Emergent tests outside `frontend/`)
- `.env` and local env variants
- MongoDB / FastAPI artifacts
- `node_modules/`, `build/`, `coverage/`
- logs and temporary artifacts
- installed dependency trees

## Post-import policy

- `apps/frontend` is the sole official frontend source.
- `kitchen-emergent` is a historical snapshot and may be archived after validation.
- No bidirectional sync or future Emergent pull is planned.
- Later PLAN-0014 commits may remove Emergent platform coupling, add lockfiles, TypeScript, tests, and CI without rewriting this provenance record.
