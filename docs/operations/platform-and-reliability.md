# Platform, Scale, Reliability, and Operations

- **Status:** Accepted direction
- **Last updated:** 2026-07-28

## Operating model

Cocinaris launches as a centrally operated SaaS. Future self-hosted distribution may be considered, but it is not a first-release commitment.

The architecture must support multiple API and worker instances for load balancing and redundancy without changing domain behavior.

## Environments

### Local development

Containerized dependencies with reproducible configuration. Developers may run only the components needed for the active module.

### Texas VPS test environment

Docker Compose may host the complete integration environment for functional, migration, queue, and operational testing.

Since 2026-10-28 the owner-operated Linux host is the development, test, build and deployment environment (ADR-0006 amendment). It runs the API under systemd from immutable release directories, with PostgreSQL on the host and Docker Compose for development fixtures. See the [Linux host deployment runbook](linux-host-deployment-runbook.md).

### Virginia production

Initial production is a single cloud region with:

- edge CDN, WAF, TLS, and reverse proxy;
- frontend deployment;
- load-balanced stateless API containers;
- independently scalable worker containers;
- PostgreSQL with backup and point-in-time recovery capability;
- RabbitMQ with durable queues appropriate to the workload;
- Keycloak;
- S3-compatible object storage;
- optional Redis;
- centralized telemetry and alerting.

Exact cloud and managed-service vendors remain open.

## Scale envelopes

| Scenario | Accounts | DAU | Approximate concurrency |
|---|---:|---:|---:|
| Launch | 5,000 | 500 | 50 |
| Successful first year | 100,000 | 10,000 | 1,000 |
| Unexpected success | 1,000,000 | 100,000 | 10,000 |

Load tests must use workload models, not only concurrent connection counts. Important dimensions include AI job arrival, receipt uploads, active cooking sessions, inventory mutations, notifications, and plan generation.

## Scaling sequence

1. instrument and profile;
2. tune queries, indexes, payloads, and worker concurrency;
3. scale stateless API replicas;
4. scale worker pools by job type;
5. increase database and broker capacity;
6. introduce Redis or read models for measured hot paths;
7. separate AI, ingestion, notifications, or public catalog when justified;
8. introduce read replicas, partitioning, or service separation after evidence;
9. consider multi-region only with a defined consistency, privacy, and disaster-recovery model.

## Availability and degradation

The product may tolerate planned low-usage maintenance early, but deployments must avoid unnecessary full outages.

Defined degradation includes:

- AI unavailable: saved and deterministic workflows remain usable;
- notification unavailable: in-product dashboard remains authoritative;
- Redis unavailable: bypass cache or use bounded local fallback without losing authoritative state;
- worker delay: jobs show queued state and age;
- RabbitMQ unavailable: outbox retains publication intent;
- object storage unavailable: prevent new photo operations without corrupting existing domain state.

## Recovery objectives

Initial directional objectives:

- RPO for primary operational data: minutes;
- RTO for severe regional or database failure: hours;
- backups alone are insufficient without restore rehearsal;
- inventory and execution data require point-in-time recovery or equivalent protection;
- backup retention and encryption follow the privacy policy.

Exact objectives become release gates after infrastructure and load plans are selected.

## Persistent jobs

A job records:

- UUID and idempotency key;
- owner and authorization context;
- type and schema version;
- input reference, not unnecessary duplicated private content;
- status and progress;
- attempt count and next attempt;
- timeout and cancellation;
- result or error category;
- creation, start, and completion timestamps;
- correlation and trace identifiers;
- usage and cost where applicable.

The user can leave and return. Jobs survive process restart.

## Messaging reliability

- transactional outbox is committed with domain state;
- publisher confirms and consumer acknowledgements are used appropriately;
- delivery is treated as at least once;
- consumers are idempotent;
- acknowledgements occur after durable processing;
- retries are bounded and classified;
- poison messages reach dead-letter handling;
- operations expose replay and repair procedures;
- duplicate notification, consumption, quota, and deletion effects are prevented.

## Database operations

- migrations are reviewed and automated;
- use expand-and-contract for rolling compatibility;
- destructive migrations require backups and rollback or forward-repair plan;
- deploy during low-usage periods initially;
- application rollback must not require reverting already-used destructive schema changes;
- query plans and index growth are observed;
- data lifecycle and privacy deletion are tested against backups and replicas.

## Observability

OpenTelemetry is the instrumentation standard for logs, metrics, and traces.

Required telemetry includes:

- API rate, latency, error, and saturation;
- database latency, connections, locks, and failed transactions;
- queue depth, age, retries, dead letters, and consumer health;
- job duration, completion, and failure category;
- AI provider, model, tokens, cost, validation, and fallback;
- upload parsing and deletion outcome;
- email and push delivery outcome;
- quota rejection and suspicious usage;
- privacy export and deletion progress;
- backup and restore checks;
- authentication and privileged-access events.

Personal data, complete prompts, allergy data, and private recipe content are not default telemetry attributes.

## Releases

- versioned application and contracts;
- automated build, tests, scans, and migrations;
- feature flags for incomplete or risky capabilities;
- progressive enablement when practical;
- low-usage deployment windows initially;
- health, readiness, and startup probes;
- rollback of application images;
- emergency disablement for AI providers, uploads, notifications, and sharing;
- post-deploy verification and observable acceptance criteria.

## References

- RabbitMQ reliability: https://www.rabbitmq.com/docs/reliability
- PostgreSQL transaction isolation: https://www.postgresql.org/docs/current/transaction-iso.html
- OpenTelemetry .NET: https://opentelemetry.io/docs/languages/dotnet/
