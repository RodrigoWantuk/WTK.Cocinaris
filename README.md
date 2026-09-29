# Cocinaris 

Cocinaris  is an AI-assisted responsive web application that helps people with limited time or cooking experience plan, shop, and cook better at home, with less effort, waste, and dependence on delivery.

> Repository name: `WTK.Cocinaris`  
> Product name: **Cocinaris**

## Central product question

> Considering what food exists, how much exists, how long it remains usable, and what the user is willing to do, what is the best way to transform that food into useful meals?

Cocinaris is not primarily a recipe catalog. Inventory, shopping, optional menu planning, equipment, profile, recipe generation, guided cooking, troubleshooting, and history provide context or actions around this decision.

## Project status

Cocinaris has completed its initial product and architecture discovery and now has production-shaped authenticated foundations on `main`:

- backend-managed OIDC browser session and internal user ownership;
- owner-isolated PostgreSQL inventory with concurrency, idempotency, history, migrations, OpenAPI, and operational tests;
- profile, household context, preferences/restrictions, equipment, and completeness backend;
- generated TypeScript API client;
- production session and authenticated inventory frontend routes;
- prototype/production isolation and blocking frontend/backend CI gates.

The full initial release is **not** implemented. Public entry, contextual home, production profile UI, recipes, planning, shopping, guided cooking, reconciliation, AI Gateway, persistent jobs, notifications, privacy workflows, generated media, billing, and launch operations remain plan-driven future work.

Do not interpret accepted architecture or prototype surfaces as completed production capability.

## Initial release direction

The first launchable release is intentionally substantial and closes the core lifecycle:

```text
profile and context
→ plan or choose food
→ determine purchases
→ register product lots
→ monitor quantity and usable life
→ choose or adapt a recipe
→ cook with staged guidance and troubleshooting
→ reconcile consumption, leftovers, preservation, and waste
→ improve the next decision
```

The product is web-only initially, requires an adult account, supports Portuguese, English, and Spanish, and supports metric and imperial measurements.

See [`docs/product/initial-release.md`](docs/product/initial-release.md).

## Accepted architecture

- React and TypeScript frontend under monorepo authority;
- independent .NET 10 / ASP.NET Core backend;
- modular monolith with separately scalable .NET workers;
- REST and OpenAPI-generated TypeScript contracts;
- PostgreSQL as authoritative storage;
- RabbitMQ, transactional outbox, at-least-once delivery, and idempotent consumers;
- Keycloak through OpenID Connect and a backend-managed browser session;
- optional Redis for non-authoritative acceleration;
- S3-compatible storage for controlled temporary and permanent media flows;
- centralized AI gateway, structured context, model routing, quotas, and cost accounting;
- OpenTelemetry and containerized deployment;
- Docker Compose for development and the Texas VPS test environment;
- initial production target in a Virginia cloud region.

Accepted decisions are indexed in [`docs/architecture/decisions/README.md`](docs/architecture/decisions/README.md).

## Repository structure

```text
apps/                 Independently deployable applications
  frontend/            React and TypeScript frontend
  backend/             ASP.NET Core API and worker solution
packages/             Versioned contracts and reusable technical packages
docs/                 Product, domain, architecture, AI, security, testing, operations, and plans
  discovery/           Durable stakeholder discovery records
  product/             Vision, audience, journeys, and release scope
  domain/              Inventory, planning, shopping, recipes, and cooking rules
  architecture/        System architecture, principles, and ADRs
  ai/                  AI workflows, validation, usage, quotas, and cost
  security/            Privacy, security, and abuse requirements
  operations/          Deployment, scale, reliability, and observability
  plans/               Executable agent plans
  plan-status.md       Canonical execution and handoff registry
infrastructure/       Deployment and environment assets
scripts/              Repository automation
.github/              GitHub workflows and contribution templates
```

## Mandatory documentation

Before changing the repository, read [`AGENTS.md`](AGENTS.md), [`docs/README.md`](docs/README.md), [`docs/plan-status.md`](docs/plan-status.md), the active plan, and the complete foundation pack required by the type of work.

The full 2026-07-28 discovery is preserved in [`docs/discovery/2026-07-28-stakeholder-discovery.md`](docs/discovery/2026-07-28-stakeholder-discovery.md). Future agents must not reconstruct requirements from chat history or silently replace accepted behavior with a smaller interpretation.

Every nontrivial agent change is plan-driven. Before every agent-created commit, update the active plan and the central registry with the state produced by that commit.

The final section of `AGENTS.md` contains mandatory hostname-specific GitHub App rules. Agents must read and follow that section before remote Git or GitHub operations.

## Language

All source code, technical documentation, branches, commits, issues, pull requests, tests, logs, and identifiers are written in English. User-facing content is localization-ready and is not embedded directly in application logic.

## License

Cocinaris is source-available under the **PolyForm Noncommercial License 1.0.0**. See [`LICENSE`](LICENSE).
