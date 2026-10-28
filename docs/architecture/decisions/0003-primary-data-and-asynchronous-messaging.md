# ADR-0003 — Primary Data and Asynchronous Messaging

- **Status:** Accepted
- **Date:** 2026-07-28

## Context

Cocinaris requires relational transactions across executions, lots, leftovers, waste, reservations, quotas, jobs, consent, and audit. It also requires durable background work for AI, parsing, notifications, privacy, and scheduling.

## Decision

- Use PostgreSQL as the authoritative relational database.
- Use one database cluster initially, with explicit module ownership of tables and mappings.
- Use optimistic concurrency and targeted stronger isolation or locks for critical invariants.
- Retry complete transactions after serialization conflicts with bounded policy.
- Store persistent job state and a transactional outbox in PostgreSQL.
- Use RabbitMQ for asynchronous delivery.
- Assume at-least-once delivery and implement idempotent consumers.
- Use bounded retries and dead-letter handling.
- Use Redis only as optional auxiliary cache, distributed limit support, short-lived coordination, or accelerated job progress.
- Never make Redis the sole store of inventory, recipes, official quota, billing, consent, or jobs.

## Alternatives considered

- Separate database per module: deferred until independent ownership or scaling requires it.
- Event sourcing for the entire product: rejected as unnecessary complexity; domain history and audit are implemented selectively.
- Database-only polling with no broker forever: acceptable as a temporary bootstrap, but RabbitMQ is the accepted target for durable asynchronous workload isolation.
- Exactly-once messaging: rejected as an end-to-end guarantee; idempotent at-least-once processing is the realistic contract.

## Consequences

- Critical state and outbox intent can commit atomically.
- Consumers and handlers require idempotency keys and duplicate tests.
- Operational runbooks must cover dead letters, replays, poison messages, and outbox lag.
- Module table ownership must be reviewed.

## References

- PostgreSQL transaction isolation: https://www.postgresql.org/docs/current/transaction-iso.html
- RabbitMQ reliability: https://www.rabbitmq.com/docs/reliability
