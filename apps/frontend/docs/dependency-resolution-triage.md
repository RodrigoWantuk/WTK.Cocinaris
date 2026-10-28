# Frontend dependency resolution triage (PLAN-0015)

Policy: every Yarn `resolutions` entry that triggers an **incompatible requested version** warning must appear in this table with individual justification. Generic claims that “every override remediates advisories” are insufficient.

Install command under review: `yarn install --frozen-lockfile`.

## Incompatible resolution warnings (current)

These are the warnings emitted by Yarn classic for ranges that do not satisfy the forced resolution:

| package | dependency path (why) | requested range | forced version | compatibility change | advisory ID / technical reason | patched version | why override is necessary | incompatibility risk | compatibility proof | removal plan |
|---|---|---|---|---|---|---|---|---|---|---|
| `underscore` | `react-scripts` → `bfj` → `jsonpath` | `1.13.6` (exact via jsonpath pin path) | `1.13.8` | patch within 1.13.x | Historical ReDoS / prototype pollution hardening on 1.13.7+; keep aligned with `jsonpath@1.3.0` resolution | `>=1.13.7` typical | CRA cannot bump `jsonpath` cleanly; force patched underscore | Low — patch-level API stable | `yarn typecheck`, `yarn test`, `yarn build` | Remove when CRA/jsonpath path upgrades past vulnerable underscore |
| `uuid` | `react-scripts` → `webpack-dev-server` → `sockjs` | `^8.3.2` | `11.1.1` | **major** 8→11 | **Advisory `1119441`** (moderate): Missing buffer bounds check in v3/v5/v6 when `buf` is provided. No patched release remains on the 8.x line (`8.3.2` still vulnerable). | Fixed in modern uuid majors (forced `11.1.1`) | Cannot satisfy sockjs `^8.3.2` and clear `1119441` simultaneously under CRA | Medium — sockjs tooling may assume uuid v8 exports | `yarn start` / `yarn test` / builds; `yarn audit:policy` fails if reverted to `8.3.2` | Exit CRA WDS/sockjs path or replace sockjs consumer |
| `resolve-url-loader` | `react-scripts` CSS pipeline | `^4.0.0` | `5.0.0` | **major** 4→5 | Postcss 8 alignment with CRA CSS loader chain / advisory path on older resolve-url-loader+postcss | postcss 8 line | Required together with `postcss@8.5.18` resolutions | Medium — loader major | `yarn build`, `yarn build:prototype` | Remove on CRA exit / Vite migration |
| `webpack-dev-server` | `react-scripts` | `^4.6.0` | `5.2.6` | **major** 4→5 | Advisory remediation on WDS 4.x (ws / middleware) | `>=5.2.x` for several GHSA | CRA still requests WDS 4; force 5 for known vulns | Medium–High — dev-server only, not production bundle | `yarn start` compiles; CI quality job | Restore WDS 4 only if patched; else migrate off CRA |
| `serialize-javascript` | webpack / terser plugin paths | `^6.0.0` and `^4.0.0` | `7.1.4` | **major** | XSS / RCE class issues in older serialize-javascript used by webpack HTML/asset plugins | `>=6.0.2` / `>=7` depending on advisory | Multiple CRA webpack plugins request divergent majors; single force | Medium | `yarn build`, `yarn build:production` | Drop when webpack stack upgrades |
| `svgo` | `@svgr/plugin-svgo`, `postcss-svgo` | `^1.2.2` (legacy path) | `2.8.3` | **major** 1→2 | High severity SVGO advisories on 1.x | `>=2.8.3` | CRA SVG pipeline still pulls SVGO 1 via some plugins | Medium — SVG optimize only | `yarn build` (SVG imports) | Remove when SVGR/postcss-svgo request SVGO 2+ |
| `@tootallnate/once` | jsdom / agent-base test tooling | `1` (major 1) | `2.0.1` | **major** 1→2 | Advisory on once@1 used by test/jsdom stack | `>=2.0.0` | Test tooling only; CRA/jsdom path | Low for production artifact | `yarn test` | Remove when jsdom path bumps |
| `@eslint/plugin-kit` | eslint 9 tooling | `^0.2.7` | `0.3.4` | minor within 0.x (Yarn still warns) | Advisory / API fix on plugin-kit 0.2.x | `>=0.3.4` | Keep eslint 9 flat-config stack healthy | Low | `yarn lint` | Remove when eslint pulls `^0.3` |

### Previously regressive — corrected this round

| package | prior forced | issue | action |
|---|---|---|---|
| `@babel/plugin-transform-modules-systemjs` | `7.29.4` while requested `^7.29.7` | **Downgrade** below requested range | Forced to **`7.29.8`** (satisfies `^7.29.7`). Path: `react-scripts` → `@svgr/webpack` → `@babel/preset-env`. Proof: `yarn install` no longer warns incompatible for this package; `yarn test` / builds green. |

## Compatible resolutions (no Yarn incompatible warning)

These remain for advisory or toolchain alignment but do **not** currently emit the incompatible-range warning. They are listed for honesty, not as silent “all good”:

| package | forced | reason (summary) |
|---|---|---|
| `react-router` | `7.18.5` | Align router packages; carries the backported fix for `1138769`/`1124282` (PLAN-0029) |
| `postcss` (+ nested paths) | `8.5.18` | High advisory remediation |
| nested `svgo` paths | `2.8.4` | Same SVGO remediation as above; `2.8.4` fixes `1243426`/`1193740` (PLAN-0029) |
| `fast-uri`, `flatted`, `qs`, `diff`, `follow-redirects`, `node-forge`, `nth-check`, `shell-quote`, `rollup`, `path-to-regexp`, `http-proxy-middleware`, `yaml`/`js-yaml` pins, `picomatch`, `minimatch`, `form-data`, `ws` | various | Historical PLAN-0015 advisory/toolchain pins; `fast-uri`, `qs`, `follow-redirects`, `shell-quote`, `serialize-javascript` and nested `js-yaml` bumped by PLAN-0029; re-verify on CRA exit |

## Active audit allowlist

See `audit-allowlist.json`. Currently empty (PLAN-0029 removed the `1124282` react-router exception after the 7.18.x backport).

## Accepted remaining install warnings

After this triage, `yarn install --frozen-lockfile` may still emit the **incompatible** warnings in the first table. Each is individually accepted until CRA migration. Unmet peer warnings (e.g. `@babel/core` peer on the systemjs plugin, `react-day-picker` peers) are separate from resolution-range incompatibilities and are not silenced.

## Proof commands

```bash
cd apps/frontend
yarn install --frozen-lockfile
yarn typecheck && yarn lint && yarn test
yarn build && yarn build:prototype && yarn build:production
yarn audit:policy
```
