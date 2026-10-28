# ADR-0005 — AI Gateway and Usage Governance

- **Status:** Accepted
- **Date:** 2026-07-28

## Context

Cocinaris uses AI for generation, parsing, adaptation, and troubleshooting. Provider cost, variable latency, privacy, prompt injection, model quality, and token limits can threaten the product and business. Direct provider calls from multiple modules or the frontend would prevent consistent control.

## Decision

- All AI access passes through a backend-owned AI gateway.
- Model operations are registered with versioned request and response schemas, prompts or workflows, budgets, timeouts, fallbacks, and evaluations.
- Context is assembled by backend code from relevant structured data under explicit collection and character limits.
- AI output is untrusted and never mutates authoritative state directly.
- The gateway selects model and provider based on capability, evaluated quality, latency, privacy, availability, and cost.
- Use cheaper models for simple tasks and capable models only where justified.
- Maintain an authoritative PostgreSQL usage and cost ledger with reservation and settlement.
- Enforce user, operation, period, concurrency, upload, and global budget limits.
- Expose understandable usage to users; normalized credits may be used instead of raw provider tokens.
- Reuse validated non-private generated content where safe.
- Keep current user inventory, restrictions, reservations, and troubleshooting contextual.
- Support bounded context compression only for noncritical narrative information.
- Provide emergency feature flags and provider/model disablement.

## Alternatives considered

- Direct SDK use in each module: rejected due duplicated policy and vendor coupling.
- Frontend model calls: rejected due credentials, quota, privacy, and validation.
- One premium model for all operations: rejected due cost and latency.
- Raw tokens as the only user allowance: deferred because tokens differ in cost and value across models.

## Consequences

- The gateway is a critical internal platform module and requires dedicated tests and observability.
- Every new AI capability needs an operation contract and evaluation before release.
- Usage accounting, idempotency, failure refunds, and anti-abuse are part of feature implementation.
- Provider choice remains replaceable and is not decided by this ADR.
