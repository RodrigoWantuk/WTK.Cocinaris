# Cocinaris Documentation

This directory is the durable source of truth for product, domain, architecture, engineering, AI, quality, security, operations, development, and work-execution decisions.

Documentation is part of the product. Code, tests, contracts, plans, and operational behavior must remain consistent with the accepted documents.

## Naming history

The product was called **KitchenFlow** during discovery and the first implementation plans. On 2026-10-28 the owner adopted **Cocinaris** for the product and every technical identifier ([PLAN-0033](plans/PLAN-0033-rename-to-cocinaris.md)). Living documents, code, contracts and configuration use Cocinaris. Completed plans PLAN-0001 to PLAN-0027 and `docs/evidence/` are immutable execution history and keep the old name, along with the commands and identifiers that were valid at the commits they reference.

## Documentation map

```text
plan-status.md       Canonical registry of active and completed plans
plans/               Implementation, testing, research, documentation, and operations plans
discovery/           Structured stakeholder discovery and reference evidence
product/             Vision, audience, entry/home, orchestration, journeys, and release scope
domain/              Inventory, planning, shopping, recipes, cooking, and invariants
architecture/        System architecture, principles, and ADRs
development/         Supported Windows/Linux environment and execution conventions
ai/                  AI workflows, context, providers, validation, quotas, and cost
security/            Privacy, data protection, security, and abuse controls
testing/             Quality strategy, domain gates, test levels, and release gates
operations/          Deployment, scale, reliability, backup, and observability
```

## Required reading for every agent

Before modifying the repository:

1. [`../README.md`](../README.md)
2. [`../AGENTS.md`](../AGENTS.md)
3. [`plan-status.md`](plan-status.md)
4. [`plans/README.md`](plans/README.md)
5. the active plan assigned to the work
6. [`product/vision.md`](product/vision.md)
7. [`discovery/2026-07-28-stakeholder-discovery.md`](discovery/2026-07-28-stakeholder-discovery.md)
8. [`discovery/2026-07-28-reference-persona-evidence.md`](discovery/2026-07-28-reference-persona-evidence.md)
9. [`architecture/principles.md`](architecture/principles.md)
10. [`architecture/decisions/README.md`](architecture/decisions/README.md)
11. every applicable accepted ADR

This is the minimum. Work-specific reading follows.

## Product and frontend work

Read:

- [`product/audience-and-profile.md`](product/audience-and-profile.md)
- [`product/entry-and-contextual-home.md`](product/entry-and-contextual-home.md)
- [`product/closed-loop-kitchen-orchestration.md`](product/closed-loop-kitchen-orchestration.md)
- [`product/user-journeys.md`](product/user-journeys.md)
- [`product/initial-release.md`](product/initial-release.md)
- [`domain/README.md`](domain/README.md)
- all domain documents touched by the workflow
- [`development/environment.md`](development/environment.md)
- [`security/privacy-and-data-protection.md`](security/privacy-and-data-protection.md)
- [`testing/product-foundation-gates.md`](testing/product-foundation-gates.md)

## Backend, data, and integration work

Read:

- [`product/closed-loop-kitchen-orchestration.md`](product/closed-loop-kitchen-orchestration.md) when work affects planning, preparation, cooking, inventory reconciliation, recovery, or troubleshooting;
- [`domain/inventory-lifecycle.md`](domain/inventory-lifecycle.md)
- [`domain/planning-and-shopping.md`](domain/planning-and-shopping.md)
- [`domain/recipes-and-cooking.md`](domain/recipes-and-cooking.md)
- [`architecture/overview.md`](architecture/overview.md)
- [`development/environment.md`](development/environment.md)
- [`ai/overview.md`](ai/overview.md)
- [`ai/usage-and-cost-governance.md`](ai/usage-and-cost-governance.md)
- [`security/security-and-abuse.md`](security/security-and-abuse.md)
- [`operations/platform-and-reliability.md`](operations/platform-and-reliability.md)
- [`operations/linux-host-deployment-runbook.md`](operations/linux-host-deployment-runbook.md) for building, deploying, rolling back, and restoring on the owner Linux host
- [`operations/backend-inventory-runbook.md`](operations/backend-inventory-runbook.md) for PLAN-0003
  development, migration, contract, identity, and recovery operations
- [`testing/strategy.md`](testing/strategy.md)
- [`testing/product-foundation-gates.md`](testing/product-foundation-gates.md)

Backend or integration work that supplies the authenticated home must also read [`product/entry-and-contextual-home.md`](product/entry-and-contextual-home.md).

## Testing work

Read the complete implementation-specific test basis plus:

- [`development/environment.md`](development/environment.md)
- [`testing/strategy.md`](testing/strategy.md)
- [`testing/product-foundation-gates.md`](testing/product-foundation-gates.md)
- the applicable implementation plans, accepted ADRs, contracts, migrations, and pinned system-under-test baselines.

## AI work

Read every backend item plus:

- the operation-specific prompt, schema, evaluation, privacy, quota, and fallback design;
- ADR-0005 and any later provider or evaluation ADR;
- applicable food-safety and restriction rules.

No agent may add a direct provider call outside the application-owned AI gateway.

## Security, privacy, and operations work

Read:

- [`development/environment.md`](development/environment.md)
- [`security/privacy-and-data-protection.md`](security/privacy-and-data-protection.md)
- [`security/security-and-abuse.md`](security/security-and-abuse.md)
- [`operations/platform-and-reliability.md`](operations/platform-and-reliability.md)
- identity, data, messaging, AI, and deployment ADRs;
- the applicable domain documents because privacy and recovery workflows must preserve domain invariants.

## Canonical decision hierarchy

When documents appear to conflict, use this order and raise the conflict in the active plan:

1. current explicit stakeholder decision recorded in an accepted document;
2. accepted ADR for technical decisions;
3. accepted product and domain documents;
4. active plan for bounded execution detail;
5. draft or proposed documents;
6. local implementation comments.

Do not silently resolve a conflict by choosing the easiest implementation.

## Non-negotiable foundation summary

- The core question is how to transform available, usable food into useful meals under user intent and constraints.
- Inventory tracks real products and lots, not only aggregate ingredients.
- Prepared reusable components are first-class inventory lots with provenance, lifecycle, shelf-life evidence, and reservations.
- Shelf-life guidance is sourced, confidence-aware, advisory, and actionable.
- Quantity or an explicit availability state is required.
- Planning, inventory, equipment, and history are optional context sources and degrade gracefully.
- Menu plans are flexible intentions, not obligations.
- Planning simulation uses projected sequential inventory, so earlier planned consumption changes later candidates.
- Recommendations never silently rewrite accepted plans or authoritative inventory.
- When reality changes, recovery preserves unaffected accepted decisions and proposes localized reversible changes.
- The authenticated home prioritizes relevant accepted menu entries, then inventory attention, then profile fit, then a one- or two-question quick chooser.
- Local-time meal context uses the user's timezone and never the server timezone or precise geolocation by default.
- Execution completion and inventory reconciliation are atomic or explicitly pending.
- Quick meal completion does not falsely imply inventory reconciliation.
- Multi-day preparation is a dependency route, not a flat reminder list.
- Recipes are user-owned, revisioned, derivable, and shared by immutable snapshot and copy.
- Troubleshooting changes execution-local state by default; recipe or preference learning requires explicit confirmation.
- AI never mutates authoritative state directly.
- React frontend and .NET backend are independently deployable.
- Privacy, food safety, accessibility, localization, cost, and operability are first-class behavior.

## Plan execution

Detailed plans live under `docs/plans/`; the central status view is `docs/plan-status.md`.

Before every agent-created commit, the active plan and registry must describe the repository state produced by that same commit, including progress, validation, failures, blockers, and exact next action.

## Document lifecycle

- **Draft:** under development and not binding.
- **Proposed:** ready for stakeholder review.
- **Accepted:** approved and binding.
- **Superseded:** replaced by another document.
- **Rejected:** considered and intentionally not adopted.
- **Deprecated:** retained for compatibility or history but not for new work.

Execution-plan statuses are defined in [`plan-status.md`](plan-status.md).

## Writing standards

- Write technical documentation in English.
- Use direct, testable statements.
- Separate accepted decisions, assumptions, risks, progress, and open questions.
- Link to canonical documents instead of copying divergent rules.
- Use ISO 8601 dates and UTC timestamps when precision matters.
- Keep diagrams text-based or reproducible when practical.
- Reference plans, ADRs, issues, and pull requests.
- Preserve historical plan and ADR rationale.
