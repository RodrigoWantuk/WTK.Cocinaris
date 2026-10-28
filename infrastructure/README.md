# Infrastructure

This directory contains versioned infrastructure and deployment assets for Cocinaris.

## Accepted direction

- containerized frontend, ASP.NET Core API, .NET workers, and self-operated dependencies where applicable;
- Docker Compose for local development and the Texas VPS integration environment;
- load-balancer-ready production deployment in a Virginia cloud region;
- PostgreSQL, RabbitMQ, Keycloak, S3-compatible object storage, optional Redis, and OpenTelemetry collection;
- stateless API replicas and independently scalable worker pools;
- no Kubernetes requirement for the initial release;
- same application code across environments, changed through configuration and adapters.

Exact cloud, managed-service, infrastructure-as-code, CI/CD, secret-management, and telemetry-storage providers remain open and require plans or ADRs.

## Development environment

The canonical host and container requirements are defined in [`../docs/development/environment.md`](../docs/development/environment.md).

Default development policy:

- install Git, Docker, .NET 10 SDK, Node.js 24 LTS, and CLI diagnostics on the host;
- run infrastructure dependencies as Linux containers on both Windows and Linux;
- do not require native PostgreSQL, Keycloak/Java, RabbitMQ/Erlang, Redis, S3 emulator, or Python installations;
- keep the default Compose profile limited to services required by the active executable slice;
- pin exact image tags and later digests in committed Compose/configuration files;
- expose development ports on loopback where practical;
- document safe environment placeholders without committing secrets.

## Intended assets

- local and integration Compose definitions;
- container build definitions;
- cloud deployment definitions;
- VPS deployment definitions;
- environment configuration templates;
- database migration execution;
- Keycloak configuration automation;
- RabbitMQ topology and recovery automation;
- object-storage lifecycle policies;
- backup, restore, and disaster-recovery automation;
- OpenTelemetry Collector and alerting configuration;
- health and release verification scripts.

## Rules

- Never commit secrets, production identifiers, private keys, or environment credentials.
- Document every required variable with a safe example.
- Separate local, test, staging, and production behavior through configuration, not code forks.
- Use least privilege, encryption, controlled network exposure, and private service connectivity where practical.
- Infrastructure changes require validation, rollback or forward-repair, and plan-state updates.
- Database changes use backward-compatible rollout where possible.
- Temporary import objects delete after success or reported failure; permanent user photos use a separate lifecycle.
- PostgreSQL backups support a tested recovery objective measured in minutes of data loss and hours of restoration, subject to selected infrastructure.
- Queue topology supports durable jobs, bounded retries, dead letters, replay, and idempotency.
- Telemetry excludes unnecessary personal, allergy, prompt, response, and recipe content.
- Provider-specific choices require an ADR when they create durable cost, security, portability, or operational consequences.

See [`../docs/operations/platform-and-reliability.md`](../docs/operations/platform-and-reliability.md), [`../docs/development/environment.md`](../docs/development/environment.md), and [`ADR-0006`](../docs/architecture/decisions/0006-deployment-and-observability.md).

The first authenticated inventory slice's exact PostgreSQL, Keycloak, migration-artifact,
forward-repair, readiness, HTTPS, key-ring, contract, and smoke-test procedures are maintained in
the [`backend inventory runbook`](../docs/operations/backend-inventory-runbook.md).
