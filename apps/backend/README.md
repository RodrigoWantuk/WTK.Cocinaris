# Cocinaris Backend

This directory contains the independently deployable Cocinaris backend API and worker solution.

## Accepted platform

- .NET 10 LTS;
- ASP.NET Core 10;
- modular monolith;
- separate .NET API and worker deployables;
- REST and OpenAPI 3.1;
- Entity Framework Core and PostgreSQL;
- RabbitMQ and transactional outbox;
- Keycloak through OpenID Connect;
- optional Redis for non-authoritative acceleration;
- OpenTelemetry.

See [`ADR-0002`](../../docs/architecture/decisions/0002-backend-platform-and-modular-monolith.md) and the remaining accepted ADRs.

## Local authenticated-slice development

Start PostgreSQL and Keycloak with `docker compose -f infrastructure/compose/compose.dev.yml up -d`. The API launch profile uses `https://localhost:7443`, which is the Keycloak redirect origin and must use a trusted ASP.NET Core development certificate. Cookie-authenticated development must use HTTPS because the session and antiforgery cookies deliberately use the `Secure` and `__Host-` constraints. Verify the certificate with `dotnet dev-certs https --check --trust` where the host supports trust installation.

Configure `COCINARIS_DB_CONNECTION`, `COCINARIS_OIDC_AUTHORITY`, `COCINARIS_OIDC_CLIENT_ID`, `COCINARIS_OIDC_CLIENT_SECRET`, and `COCINARIS_SESSION_KEYRING_PATH` through an ignored local environment file or secret store. The session key-ring path is development-local only; production deployments must provide a shared protected key ring. Do not place OIDC tokens or client secrets in browser configuration.

The API binds and validates database, OIDC, Data Protection, secure-session, and idempotency options
at startup. Production-like environments require a non-placeholder database connection, HTTPS OIDC
authority, confidential client identifier and secret, and an absolute persistent key-ring path.
`Session:CookieName` must retain the `__Host-` prefix, `Session:IdleTimeout` must be between five
minutes and one day, and `Idempotency:Retention` must be between one and ninety days. Safe session
and retention defaults live in `appsettings.json`; Keycloak fixture authority/client values live only
in `appsettings.Development.json`. No production credential has a repository default.

`/health/live` proves that the process pipeline is running. `/health/ready` validates the complete
local configuration set and PostgreSQL connectivity. It deliberately does not call OIDC discovery:
an identity-provider outage must be monitored independently and must not create an unbounded
readiness dependency. A 503 response is normalized as `application/problem+json` with
`service_unavailable`.

Framework authentication, authorization, CSRF, rate-limit, malformed JSON, unsupported media type,
route/query binding, dependency, and unexpected failures use the same privacy-safe Problem Details
shape. Responses contain a stable `errorCode` and safe trace identifier; they do not copy exception
messages, request bodies, credentials, cookies, tokens, private notes, or user identifiers.

The complete executable workflow for prerequisites, secrets, Compose, restore/build/test, dependency
audit, migrations and forward repair, HTTPS, session keys, health, OpenAPI, the real-Keycloak smoke,
and troubleshooting is in the
[`backend inventory runbook`](../../docs/operations/backend-inventory-runbook.md).

## Inventory and security telemetry

Set the optional standard `OTEL_EXPORTER_OTLP_ENDPOINT` server-side to export traces and metrics over
OTLP. Without it, instrumentation remains local and no external collector is a required dependency
for this slice. The redaction processor runs before configured trace export.

The project-owned metric surface is intentionally bounded:

| Metric | Unit | Labels | Interpretation |
|---|---|---|---|
| `cocinaris.inventory.mutations` | `operations` | `operation`, `outcome` | Final create, metadata-update, adjustment, lifecycle-transition, or delete outcome |
| `cocinaris.inventory.rejections` | `operations` | `category` | Stable validation or domain-rule rejection |
| `cocinaris.inventory.concurrency_failures` | `operations` | `operation` | Stale optimistic-concurrency mutation |
| `cocinaris.inventory.idempotency` | `operations` | `operation`, `outcome` | First completion, replay, reused key, or defensive in-progress response |
| `cocinaris.security.access_failures` | `failures` | `category` | Authentication, authorization, CSRF, or rate-limit rejection |

Allowed labels never include internal user IDs, issuer/subject, addresses, product names, notes,
headers, request bodies, cookies, authorization values, or tokens. ASP.NET Core instrumentation owns
standard route, status, duration, and dependency measurements.

## Responsibilities

- backend-for-frontend and API endpoints;
- identity integration, secure browser session, and domain authorization;
- product and household application workflows;
- authoritative inventory, recipe, planning, shopping, cooking, quota, privacy, and audit state;
- transactions, concurrency, idempotency, and migrations;
- AI gateway, context selection, structured workflows, validation, quotas, and cost accounting;
- persistent jobs, outbox publication, message consumption, retries, and dead letters;
- provider integration and degraded behavior;
- observability and support diagnostics without unnecessary private content.

## Initial module direction

```text
Accounts
Profiles
Inventory
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

Modules own their rules and persistence mappings. Cross-module behavior uses explicit application contracts rather than arbitrary table mutation.

## Python boundary

Python is not the authoritative backend. An isolated Python worker may be introduced only when a concrete model, image, or specialized-library requirement justifies it through a plan and ADR. It does not own authentication, authorization, inventory, quota, or the primary database.

## Required properties

- independently buildable, testable, deployable, and observable;
- configuration-driven across environments;
- horizontally scalable where stateless;
- secure by default and explicit about authorization;
- provider-independent at external boundaries;
- compatible with versioned contracts;
- useful during AI and optional-service degradation;
- resumable and idempotent for persistent jobs;
- compliant with documented privacy, deletion, and retention behavior.

## Required reading

- [`../../docs/README.md`](../../docs/README.md)
- [`../../docs/domain/README.md`](../../docs/domain/README.md)
- [`../../docs/architecture/overview.md`](../../docs/architecture/overview.md)
- [`../../docs/ai/overview.md`](../../docs/ai/overview.md)
- [`../../docs/security/security-and-abuse.md`](../../docs/security/security-and-abuse.md)
- [`../../docs/operations/platform-and-reliability.md`](../../docs/operations/platform-and-reliability.md)
- [`../../docs/testing/product-foundation-gates.md`](../../docs/testing/product-foundation-gates.md)
