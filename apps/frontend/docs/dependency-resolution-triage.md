# Frontend dependency resolution triage

Policy: every Yarn `resolutions` entry must be listed here with its reason. Generic claims that "every override remediates advisories" are insufficient.

## Current resolutions (since PLAN-0033, 2026-10-28)

| package | forced | reason |
|---|---|---|
| `react-router` | `7.18.5` | Keeps `react-router` aligned with `react-router-dom` 7.18.5, which carries the backported fix for advisory `1138769`/`1124282` (PLAN-0029) |

No other resolution is needed. The Create React App toolchain (webpack, babel, Jest 27, webpack-dev-server, svgo, postcss loaders) that required more than 40 forced versions was removed by the Vite/Vitest migration ([ADR-0008](../../../docs/architecture/decisions/0008-frontend-toolchain-vite.md)). Its triage tables live in git history (this file before PLAN-0033) and in PLAN-0015/PLAN-0029.

## Proof commands

```bash
yarn install --frozen-lockfile   # no "incompatible resolution" warnings expected
yarn audit:policy
```
