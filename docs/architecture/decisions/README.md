# Architecture Decision Records

Architecture Decision Records capture significant decisions, context, alternatives, and consequences.

## Mandatory lifecycle

- **Draft:** being written.
- **Proposed:** ready for stakeholder decision.
- **Accepted:** approved and binding.
- **Rejected:** reviewed and not adopted.
- **Superseded:** replaced by a later ADR.
- **Deprecated:** retained for history but no longer recommended.

Do not rewrite accepted history. Add a note or superseding ADR.

## Current ADRs

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-frontend-platform-and-boundary.md) | React/TypeScript frontend through Lovable with a strict backend boundary | Superseded by ADR-0007 |
| [0002](0002-backend-platform-and-modular-monolith.md) | .NET 10 ASP.NET Core modular monolith and .NET workers | Accepted |
| [0003](0003-primary-data-and-asynchronous-messaging.md) | PostgreSQL, transactional outbox, RabbitMQ, and optional Redis | Accepted |
| [0004](0004-identity-and-browser-session.md) | OIDC (Keycloak as development fixture; any compliant provider in deployments, 2026-10-28 amendment) and backend-managed browser session | Accepted |
| [0005](0005-ai-gateway-and-usage-governance.md) | Central AI gateway, structured context, model routing, and quota ledger | Accepted |
| [0006](0006-deployment-and-observability.md) | Containers, Docker Compose test environment, Virginia production, and OpenTelemetry (owner Linux host as development and deployment environment, 2026-10-28 amendment) | Accepted |
| [0007](0007-frontend-platform-monorepo-and-generation-tooling.md) | React/TypeScript frontend; monorepo `apps/frontend` authority; Emergent/Lovable optional | Accepted |
| [0008](0008-frontend-toolchain-vite.md) | Vite and Vitest replace Create React App + CRACO (amends ADR-0007) | Accepted |

## Future decision backlog

ADRs or implementation designs are still required for:

- frontend package set evolution beyond the ADR-0008 toolchain (state, component, and localization libraries; React Router 8);
- detailed .NET solution and module enforcement;
- migration tooling conventions and schema ownership;
- exact RabbitMQ topology and scheduler implementation;
- exact cloud and managed-service vendors;
- AI providers, model catalog, prompt storage, evaluation tooling, and credit conversion;
- notification providers;
- object-storage provider and media pipeline;
- billing and advertising;
- secret management;
- deployment pipeline and release gates (owner-host scripted deployment exists since PLAN-0031; production pipeline remains open);
- retention schedule and legal launch-country matrix.

Use [`0000-adr-template.md`](0000-adr-template.md) for future records.
