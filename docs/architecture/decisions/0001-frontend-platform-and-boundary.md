# ADR-0001 — Frontend Platform and Boundary

- **Status:** Superseded
- **Date:** 2026-07-28
- **Superseded by:** [ADR-0007](0007-frontend-platform-monorepo-and-generation-tooling.md)
- **Supersession date:** 2026-07-31

## Context

The project owner requires a React frontend generated and evolved through Lovable. The frontend must remain separate from the authoritative backend and usable across desktop and mobile browser sizes.

## Decision

- Use React and TypeScript for the frontend.
- Lovable is the required design and generation workflow.
- Preserve the actual runtime, router, and build system produced by the accepted Lovable project unless a later ADR changes it.
- Keep the frontend independently buildable, testable, deployable, and observable.
- Consume the backend through generated OpenAPI TypeScript clients or contracts.
- Do not use Lovable-hosted database, authentication, AI calls, or backend features as authoritative Cocinaris infrastructure unless a later ADR explicitly accepts them.
- Do not place provider secrets, long-lived identity tokens, authoritative quota, or domain rules in frontend code.
- Require localization readiness, accessibility, responsive behavior, and explicit uncertainty presentation from the first implementation.

## Alternatives considered

- A non-React frontend: rejected by stakeholder constraint.
- A combined server-rendered .NET UI: rejected because frontend and backend must remain separate and Lovable-driven.
- Direct frontend access to database or AI providers: rejected for security, consistency, quota, and portability.

## Consequences

- Frontend design iteration can use Lovable without allowing generated code to define backend architecture.
- Generated code requires review, cleanup, tests, accessibility, localization, and contract integration.
- The exact package set is not chosen until the generated project is inspected.
- Backend APIs must be sufficiently complete for all authoritative operations.

## Supersession note

ADR-0007 retains React/TypeScript and the backend boundary, establishes `apps/frontend` as the sole official frontend source after the Emergent snapshot import, and reclassifies Lovable and Emergent as optional generation tools rather than mandatory platform owners.
