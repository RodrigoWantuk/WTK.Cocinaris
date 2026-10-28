# @cocinaris/api-client

Reproducible TypeScript client generated from
`packages/contracts/openapi/cocinaris-v1.json`.

## Generator policy

| Item | Value |
|---|---|
| Generator | `openapi-typescript` **7.9.1** (pinned) |
| Runtime helper | `openapi-fetch` **0.14.0** (pinned) |
| Generated output | `src/generated/schema.ts` (**committed**) |
| Manual edits | **Forbidden** on generated files |

## Commands

```bash
cd packages/api-client
yarn install --frozen-lockfile
yarn generate
yarn check:drift
yarn typecheck
```

CI must fail when the committed OpenAPI snapshot and generated client drift.

## Frontend mirror

The frontend compiles the client from inside `apps/frontend/src` (a historical Create React App constraint kept under Vite so the frontend stays self-contained).
`yarn generate` therefore syncs `src/` into
`apps/frontend/src/generated/api-client` (committed mirror; do not edit by hand).

## Ownership

- OpenAPI source of truth: `packages/contracts`
- Generation package: this directory
- Presentation models and CSRF/ETag orchestration: `apps/frontend` live adapters

Application UI must not bind directly to generated DTO shapes.
