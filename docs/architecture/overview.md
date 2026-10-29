# Architecture Overview

- **Status:** Accepted
- **Last updated:** 2026-07-28
- **Discovery source:** [`2026-07-28 stakeholder discovery`](../discovery/2026-07-28-stakeholder-discovery.md)

## Purpose

This document defines the accepted initial architecture for Cocinaris. Detailed technology choices are recorded in Architecture Decision Records under [`decisions/`](decisions/).

Cocinaris is a responsive multilingual web product with independently buildable, testable, deployable, and observable frontend and backend applications.

## Architectural goals

- Protect transactional inventory and recipe-execution consistency.
- Keep probabilistic AI outside authoritative state mutation.
- Support low-cost launch infrastructure and measured horizontal growth.
- Remain useful during AI, notification, cache, or worker degradation.
- Preserve portability between a development VPS and managed cloud deployment.
- Make cost, quota, latency, privacy, and failure behavior observable from the beginning.
- Avoid premature microservices while maintaining extractable module boundaries.

## Accepted technology direction

| Area | Decision |
|---|---|
| Frontend | React and TypeScript generated and evolved through Lovable |
| Backend | .NET 10 LTS with ASP.NET Core |
| Application style | Modular monolith plus separately scalable worker processes |
| API | REST with OpenAPI 3.1 and generated TypeScript client/contracts |
| Primary database | PostgreSQL |
| Data access | Entity Framework Core, with explicit SQL where measured requirements justify it |
| Identity provider | Keycloak through standard OpenID Connect |
| Browser session | Backend-managed secure session cookie; provider tokens are not exposed to frontend JavaScript |
| Async messaging | RabbitMQ with transactional outbox and idempotent consumers |
| Cache and distributed limits | Redis only when justified; never authoritative storage |
| Files | S3-compatible object storage with separate temporary and permanent flows |
| Observability | OpenTelemetry logs, metrics, and traces |
| Packaging | Containers |
| Development and VPS test | Docker Compose |
| Production region | Initial cloud deployment in Virginia |
| Specialized AI or image runtime | Optional isolated Python worker only when a concrete need justifies it |

## System context

```text
Users and browsers
        |
        | HTTPS
        v
CDN / WAF / reverse proxy / load balancer
        |
        +---------------------------+
        |                           |
        v                           v
React frontend                 ASP.NET Core API / BFF
Lovable-generated UI                |
                                    +-------------------------------+
                                    |                               |
                                    v                               v
                             Modular application                Keycloak
                                    |
          +-------------------------+-------------------------+
          |             |             |          |            |
          v             v             v          v            v
     PostgreSQL     RabbitMQ      Object      Redis       AI providers
       + outbox                    storage    optional
          |
          v
     .NET workers
          |
          +---- ingestion, AI jobs, notifications, privacy jobs,
                alert calculation, export, deletion, and scheduling
```

The reverse proxy should expose frontend and API under a controlled origin where practical. Frontend and backend remain separate artifacts even when one external origin routes both.

## Frontend boundary

The frontend owns:

- responsive localized presentation;
- accessible interaction and navigation;
- temporary UI state;
- upload initiation and progress;
- presentation of uncertainty, provenance, validation, quota, and job state;
- consumption of generated API clients.

The frontend does not own:

- authoritative inventory arithmetic or lifecycle transitions;
- authorization decisions;
- AI provider calls or credentials;
- official quota balance;
- shelf-life authority;
- secret material;
- direct database access.

The exact Lovable-generated runtime, router, build tool, component library, and state library are selected after inspecting the generated frontend. React and TypeScript are fixed; incidental tool choices are not assumed before the code exists.

## Backend boundary

The backend owns:

- BFF and API endpoints;
- application authentication integration and authorization;
- domain commands, queries, validation, and transactions;
- PostgreSQL persistence and migrations;
- AI gateway and structured workflow orchestration;
- quota, entitlement, usage, and cost accounting;
- persistent job creation and state;
- outbox event publication;
- privacy and data-right workflows;
- audit, observability, rate limits, and abuse controls.

## Modular monolith

The initial backend is one logical application with explicit modules and a small number of deployables:

```text
Cocinaris.Api
Cocinaris.Worker
Cocinaris.Contracts
Cocinaris.SharedKernel

Modules/
  Accounts
  Profiles
  Inventory
  Ingredients   (canonical ingredient identity, PLAN-0055 / ADR-0010)
  Recipes
  Cooking
  Planning
  Shopping
  Notifications
  Ai
  UsageAndBilling
  Privacy
  AuditAndSupport
```

Modules own their domain rules and persistence mappings. A module must not bypass another module's public application contract by freely editing its tables.

The API and workers may scale independently while sharing the same codebase and database at first.

## API and contracts

- REST is the initial external API style.
- OpenAPI 3.1 is generated from the backend build.
- TypeScript clients and types are generated rather than copied manually.
- APIs, events, AI schemas, units, enums, and identifiers are versioned contracts.
- Breaking changes require compatibility and migration plans.
- Idempotency keys are required for retryable state-changing operations where duplicate effects would be harmful.

## Transactional consistency

PostgreSQL is the source of truth. Critical operations use one logical transaction.

Recipe completion includes, as applicable:

```text
complete execution
+ consume inventory lots
+ create leftover lots
+ register waste
+ release reservations
+ store execution result
+ write outbox events
```

The application uses optimistic concurrency by default and stronger locking or transaction isolation for specific invariants. Serialization failures are retried by re-executing the complete transaction with bounded policy.

## Asynchronous work

Long or externally dependent work is persistent and resumable:

- AI recipe generation and broad planning;
- receipt and recipe parsing;
- notifications;
- shelf-life alert calculation;
- account export and deletion;
- image processing;
- reprocessing and maintenance.

A web request creates durable job state and can return before completion. The user may close the page and later inspect progress and results.

RabbitMQ provides at-least-once delivery. Consumers are idempotent. Failed messages use bounded retries and dead-letter handling. PostgreSQL transactional outbox prevents domain commits from being separated from event intent.

## AI boundary

All model access passes through the backend AI gateway. The gateway owns operation catalog, provider routing, models, prompts, context, schemas, budgets, timeout, fallback, validation, telemetry, and usage settlement.

AI output is untrusted input and never writes authoritative state directly.

See [`../ai/overview.md`](../ai/overview.md) and [`../ai/usage-and-cost-governance.md`](../ai/usage-and-cost-governance.md).

## Data and file storage

### PostgreSQL

Stores authoritative operational data, usage ledger, job records, audit records, consent, privacy requests, and outbox records.

### Redis

May support cache, distributed rate limiting, short-lived deduplication, job progress acceleration, and brief coordination. It is optional at launch and never the sole store for inventory, recipes, quota balances, consent, billing, or durable jobs.

### Object storage

Temporary receipt and import images are isolated, parsed once, and deleted on success or reported failure. Permanent recipe or execution photos use a separate user-authorized storage lifecycle.

## Deployment

### Texas VPS environment

Docker Compose may run frontend, API, workers, PostgreSQL, RabbitMQ, Keycloak, object storage, optional Redis, and an OpenTelemetry Collector for development and testing.

### Virginia production environment

Production uses containerized stateless API and worker replicas behind managed edge and load-balancing infrastructure. PostgreSQL, RabbitMQ, Keycloak, object storage, and optional Redis may be managed or separately operated according to later vendor decisions.

Kubernetes is not required initially. Container and configuration discipline must keep future migration possible.

## Scale envelopes

| Scenario | Accounts | Daily active users | Approximate concurrency |
|---|---:|---:|---:|
| Launch | 5,000 | 500 | 50 |
| Successful first year | 100,000 | 10,000 | 1,000 |
| Unexpected success | 1,000,000 | 100,000 | 10,000 |

These are architecture and test envelopes, not forecasts.

## Extraction path

Separately scalable or deployable boundaries may later include:

- AI gateway and AI workers;
- image and document ingestion;
- notification delivery;
- scheduling and background jobs;
- public recipe sharing and community catalog;
- search and recommendation read models;
- analytics.

Extraction requires measured load, security, compliance, reliability, or ownership justification and an accepted ADR.

## Related decisions

- [`0001 — Frontend platform and boundary`](decisions/0001-frontend-platform-and-boundary.md)
- [`0002 — Backend platform and modular monolith`](decisions/0002-backend-platform-and-modular-monolith.md)
- [`0003 — Primary data and asynchronous messaging`](decisions/0003-primary-data-and-asynchronous-messaging.md)
- [`0004 — Identity and browser session`](decisions/0004-identity-and-browser-session.md)
- [`0005 — AI gateway and usage governance`](decisions/0005-ai-gateway-and-usage-governance.md)
- [`0006 — Deployment and observability`](decisions/0006-deployment-and-observability.md)
