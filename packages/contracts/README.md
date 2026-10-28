# Shared Contracts

This directory contains versioned contracts shared across Cocinaris boundaries.

## Accepted contract direction

- REST API contract expressed as OpenAPI 3.1 from the ASP.NET Core backend;
- reproducibly generated TypeScript frontend client and types;
- versioned event and message schemas;
- versioned structured AI request and response schemas;
- shared stable identifiers, units, enums, error codes, provenance, confidence, and validation metadata;
- compatibility fixtures and migration tests.

The exact generator and generated-file commit policy require an implementation plan after the initial solution exists.

## Recipe AI schemas (protocol 0.3)

Versioned AI response schemas, deterministic validators, and positive/negative fixtures live under [`ai/recipe/`](ai/recipe/).

```bash
cd packages/contracts/ai/recipe
npm install
npm test
npm run check:drift
```

Live provider evaluation is intentionally separate: `scripts/ai/recipe-live-eval.mjs`.

## Inventory v1 snapshot

`openapi/cocinaris-v1.json` is generated from the running ASP.NET Core API. Run
`scripts/backend/export-openapi.sh`, `scripts/backend/check-openapi.sh`, and
`scripts/backend/lint-openapi.sh` from the repository root. The linter configuration intentionally
disables generic 2xx/4xx-presence style rules because OIDC login/logout are redirect-only and health
operations do not manufacture client errors; executable contract tests verify API route errors.

Inventory quantity schemas use two exclusive branches. A measured quantity requires a decimal value
and `Gram`, `Milliliter`, or `Unit`, with availability absent or null. A qualitative quantity
requires `Available`, `Low`, or `Unavailable`, with measured value and unit absent or null.
State-changing cookie-authenticated requests require the declared CSRF header. Create and adjustment
commands additionally require a UUID idempotency key, and mutations of existing lots require the
opaque resource-bound ETag through `If-Match`.

History returns immutable lifecycle transactions plus a `MetadataCorrection` projection. That
projection contains only the stable names of fields that changed; it never contains product names,
note contents, complete request bodies, credentials, or tokens.

The production TypeScript client is generated from this snapshot by PLAN-0016 into
`packages/api-client` (pinned `openapi-typescript` 7.9.1) and mirrored into
`apps/frontend/src/generated/api-client` for frontend compilation. Regenerate with
`cd packages/api-client && yarn generate`. Drift is enforced by
`yarn check:drift` / frontend `yarn check:api-client-drift`.

## Rules

- Contracts are products with owners, versions, tests, and compatibility policies.
- Do not expose EF entities, database models, Keycloak types, RabbitMQ client types, or AI provider SDK types.
- Do not put cross-application business logic in contracts.
- Required fields, nullability, units, ranges, identifiers, collection limits, and version behavior are explicit.
- State-changing operations define idempotency and concurrency behavior when relevant.
- Errors use stable machine-readable codes and localization-ready parameters rather than hard-coded English as the only representation.
- AI schemas define maximum object counts and string lengths and receive structural and domain validation.
- Event schemas support at-least-once delivery and idempotent consumers.
- Breaking changes require migration, compatibility, rollout, and rollback plans.
- Generated artifacts have a canonical source and reproducible command.

## Domain sensitivity

Contracts must preserve accepted distinctions, including:

- product versus culinary classification;
- inventory lot versus aggregate availability;
- physical quantity versus reservation;
- source and confidence;
- recipe identity versus revision and derivation;
- execution-local adaptation versus permanent revision;
- execution completion versus pending reconciliation;
- advisory attention versus forced safety claim.

See [`../../docs/domain/README.md`](../../docs/domain/README.md) and [`ADR-0002`](../../docs/architecture/decisions/0002-backend-platform-and-modular-monolith.md).
