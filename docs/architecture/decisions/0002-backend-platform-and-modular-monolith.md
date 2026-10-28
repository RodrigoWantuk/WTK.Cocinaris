# ADR-0002 — Backend Platform and Modular Monolith

- **Status:** Accepted
- **Date:** 2026-07-28

## Context

Cocinaris's central complexity is transactional inventory, recipes, reservations, execution reconciliation, privacy, quotas, and reliable workflows. Normal AI provider integration is HTTP-based and does not require Python to own the application core.

The project owner has extensive C# experience and requires strong security, quality, maintainability, and scaling without premature distributed complexity.

## Decision

- Use .NET 10 LTS and ASP.NET Core 10 for the authoritative backend.
- Begin as a modular monolith with explicit domain modules.
- Use a separate .NET worker deployable from the same codebase for persistent background work.
- Use REST and built-in OpenAPI 3.1 generation for external contracts.
- Generate TypeScript clients and types for the React frontend.
- Use Entity Framework Core for standard persistence and migrations; use explicit SQL only for measured needs with tests and ownership.
- Permit isolated Python services only when a concrete specialized library, local model, image pipeline, or data task justifies them.
- A Python service does not own core authentication, authorization, inventory, quota, or the primary database.

## Alternatives considered

### Python backend

Viable, especially for model or data workloads, but rejected for the authoritative core because Cocinaris is dominated by transactional domain behavior and the owner is more productive in C#.

### Microservices from the start

Rejected because they add deployment, consistency, observability, and local-development cost before measured need.

### Serverless functions as the primary backend

Rejected as the default because long workflows, durable jobs, modular transactions, and provider orchestration need consistent application ownership. Specific functions may be introduced later by ADR.

## Consequences

- Strong typed domain and contract tooling.
- One primary backend ecosystem at launch.
- Module boundaries and tests must prevent the monolith from becoming an unstructured shared database application.
- Service extraction remains possible but requires evidence and an ADR.
- The project must track .NET support and upgrade before end of support.

## References

- .NET lifecycle: https://learn.microsoft.com/lifecycle/products/microsoft-net-and-net-core
- ASP.NET Core OpenAPI: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
