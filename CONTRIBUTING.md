# Contributing to Cocinaris

Cocinaris is an early-stage source-available project with an accepted product and architecture foundation. Contributions must preserve product intent, domain integrity, security, privacy, food safety, accessibility, localization, cost control, operability, testability, documentation completeness, and resumability.

## Before starting

Read:

- `README.md`;
- `AGENTS.md`;
- `docs/README.md` and its role-specific foundation path;
- `docs/plan-status.md`;
- `docs/plans/README.md`;
- the active plan;
- every applicable accepted ADR.

Open or reference an issue for nontrivial work when it improves scope, decision, defect, or dependency traceability.

## Language

All technical content is written in English. User-facing content uses the selected localization system and is not hard-coded in application logic.

## Plan requirement

Every nontrivial agent contribution uses a numbered plan and registry entry.

Before implementation:

1. create or claim the plan;
2. confirm included and excluded scope;
3. define testable acceptance criteria and required validation;
4. identify ownership, dependencies, branch, checkpoint, and exact next action;
5. define the substantial coherent outcome targeted for the current run;
6. register the plan.

Before every agent-created commit, update the active plan and registry with the state produced by that commit. Before pausing or handing off, leave enough repository state for another contributor to continue without conversation history.

## Substantial delivery per run

Each execution run must aim to complete the largest coherent plan phase, vertical slice, or decision-ready outcome that can be safely implemented, documented, tested, and validated.

When no real blocker exists, a run should not end after planning, scaffolding, one isolated file, one endpoint, one component, one migration, placeholder tests, a status-only change, or another trivial checkpoint. Continue through adjacent in-scope checkpoints while dependencies are available and the repository remains safe and verifiable.

A substantial delivery normally includes the relevant implementation, tests, documentation, contracts, migrations, validation, and handoff evidence. Delivery size is measured by coherent outcome and resolved acceptance criteria, not by lines changed, files changed, commits, or elapsed time.

Use multiple cohesive commits when appropriate. “Large delivery” never permits scope creep, unrelated bundling, giant unreviewable commits, skipped tests, weakened security, undocumented behavior, or false completion claims.

Valid reasons to stop early are limited to a real external blocker, required stakeholder decision, unsafe uncertainty, environment or tool failure, conflicting concurrent work, exhausted execution capacity, or a necessary plan revision. Record the precise cause, completed work, validation, remaining work, and exact next action.

## Product and architecture consistency

Do not introduce behavior that contradicts the accepted foundation, especially:

- aggregate-only inventory instead of lot lifecycle;
- silent AI state mutation;
- forced menu planning or inventory participation;
- shared mutable recipes across users;
- frontend-owned authoritative rules;
- direct AI provider calls outside the gateway;
- Redis or queues as authoritative product storage;
- an undocumented reduction of initial release scope.

A durable change requires stakeholder-approved product documentation or an ADR, as applicable.

## Complete documentation

Documentation is part of each delivery and is updated in the same pull request as the code, behavior, contract, configuration, migration, deployment, or decision it describes.

Update every affected area, including when applicable:

- product and user behavior;
- domain rules and invariants;
- architecture and ADRs;
- APIs, events, schemas, prompts, and generated contracts;
- configuration, environment variables, defaults, and safe examples;
- migrations, compatibility, rollback, and forward-repair guidance;
- deployment, observability, alerts, runbooks, backup, restore, and support procedures;
- security, privacy, food safety, AI cost, localization, accessibility, performance, and resilience;
- testing, fixtures, evaluation data, commands, evidence, limitations, and handoff.

### Code comments and API documentation

- New or materially changed public and protected .NET types and members require accurate XML documentation.
- Public APIs, domain types, commands, queries, options, extension methods, middleware, adapters, exceptions, and reusable utilities require XML documentation.
- Non-obvious internal .NET contracts also require XML documentation or an equivalent durable explanation when domain, security, ownership, concurrency, lifecycle, idempotency, performance, or failure semantics matter.
- Use XML elements such as `<summary>`, `<param>`, `<typeparam>`, `<returns>`, `<exception>`, `<remarks>`, `<value>`, and `<inheritdoc/>` when applicable.
- Exported reusable TypeScript components, hooks, functions, types, adapters, and utilities require TSDoc/JSDoc when their contract or side effects are not self-evident.
- Inline comments explain rationale, invariants, hazards, protocol constraints, security boundaries, concurrency, idempotency, compatibility, or intentionally unusual behavior.
- Do not add comments that merely narrate syntax or duplicate clear names.
- Remove or correct stale, misleading, redundant, false, and commented-out code.
- Generated code is documented at the source schema or generator boundary rather than manually edited solely for comments.
- Exceptions must be explicit, narrow, justified in the active plan, and visible in review.

## Branches and commits

Agent branches include the plan ID:

```text
agent/plan-0001-short-scope
```

Use concise imperative English commit messages. Commit plan-state updates with the work they describe. Delete merged branches unless a documented operational reason requires retention.

Use cohesive, reviewable commits even when the overall run delivers a substantial amount of work.

## Pull requests

A pull request includes:

- linked plan and delivery state;
- summary and rationale;
- included and excluded scope;
- user and developer impact;
- product and domain effects;
- architecture and contracts;
- security, privacy, food-safety, localization, accessibility, AI-cost, and operational effects;
- complete durable and code-level documentation evidence;
- the substantial outcome delivered during the run;
- automated and manual validation;
- failures, skipped checks, limitations, and exact handoff.

### Agent authority limits (mandatory)

- Agents **may** create branches, commits, and **draft** pull requests.
- Agents **must not** approve pull requests (including their own).
- Agents **must not** enable auto-merge.
- Agents **must not** merge pull requests.
- **Only the repository owner** (or an explicitly designated human maintainer) may authorize and perform merge.
- Green CI is **not** merge authorization.
- An agent must **never** record owner approval without contemporaneous, explicit owner evidence for that specific merge.

## Quality requirements

Before review:

- run formatting, analysis, build, tests, evaluations, and scans appropriate to the change;
- test failure and recovery paths;
- verify authorization and data isolation;
- verify idempotency and concurrency when applicable;
- validate localization and accessibility;
- confirm no secrets, personal data, source images, or private transcripts were added;
- ensure durable docs, code comments, XML documentation, TSDoc/JSDoc, ADRs, contracts, plan, and registry match the branch;
- confirm the run delivered a substantial coherent outcome or document the valid reason it could not;
- disclose checks that could not run.

AI-generated work is held to the same standard and is not accepted merely because it compiles or appears plausible.

## License

Contributions are distributed under the repository's PolyForm Noncommercial License 1.0.0 unless a separate written agreement states otherwise.
