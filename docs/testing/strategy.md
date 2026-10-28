# Testing Strategy

- **Status:** Accepted with lean-validation amendment
- **Last updated:** 2026-08-05
- **Lean validation amendment:** [`../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md`](../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md)

## Purpose

Cocinaris requires a layered quality strategy covering deterministic software, contracts, integrations, user journeys, AI-assisted behavior, security, accessibility, localization, performance, and operations.

Testing is part of feature design. A requirement is incomplete until its verification approach is understood.

## Quality objectives

- Protect household data and authorization boundaries.
- Preserve inventory and planning consistency.
- Detect contract incompatibilities before deployment.
- Prevent unsafe or restriction-violating cooking guidance.
- Keep critical journeys usable across supported languages and devices.
- Make AI changes measurable and reproducible.
- Support safe deployment to cloud and VPS environments.
- Keep failures diagnosable without exposing sensitive data.

## Test execution governance

### Default lean validation

Ordinary implementation, research, documentation, and contract work follows the lean risk-based default in [`../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md`](../plans/PLAN-0007-amendment-2026-08-05-lean-validation.md):

1. targeted unit tests for changed deterministic behavior;
2. one focused integration or contract test when a real boundary changes;
3. format/build/typecheck for affected projects;
4. one final CI execution on the PR head.

Validation scope tracks the changed area. Backend-only, frontend-only, contract-only, and documentation-only changes do not require unrelated full matrices. Live AI provider campaigns remain separated from the default fast suite and must declare a cost ceiling.

Default deterministic execution is once. Suspected flaky behavior may use up to three executions. Concurrency or idempotency changes may use 5–10 focused synchronized executions. More than ten repetitions requires a documented reproduced race or an explicit owner request.

Independent validation is not required by default. It is reserved for concrete high-impact risk such as cross-user authorization or data exposure, destructive migrations, payment or financial accounting, production credential handling, confirmed concurrency corruption, data-loss recovery, or critical food-safety enforcement.

Do not create a documentation-only commit solely to record successful CI workflow IDs after the final head has already passed.

### When a dedicated testing plan is required

A separate testing plan under `docs/plans/` is required only when independent validation is justified by the elevated-risk criteria above, or when the owner explicitly requests an independent assessment. Use [`../plans/0000-test-plan-template.md`](../plans/0000-test-plan-template.md) to define:

- the exact system-under-test branch, pull request, commit, release, and environment;
- the authoritative test basis and acceptance criteria;
- risk-based test priorities;
- requirements-to-evidence traceability;
- environments, synthetic data, fixtures, and provider simulations;
- test cases, defects, evidence, coverage gaps, and residual risk;
- entry and exit criteria;
- the final quality outcome and merge or release recommendation;
- current checkpoint, blockers, and exact next action.

Before every testing-agent commit, the agent must update the test plan's execution state and progress log together with the matching row in `docs/plan-status.md`. The update must describe the test state produced by that commit, including evidence, defects, coverage gaps, blockers, and continuation instructions.

A testing agent must verify behavior against product requirements, contracts, architecture, and acceptance criteria rather than trusting implementation claims. Completion of test execution is not equivalent to a passing result: a completed testing plan may conclude `Pass`, `Conditional Pass`, `Fail`, or `Inconclusive`.

## Test levels

### Unit tests

Use unit tests for deterministic behavior with narrow dependencies, including:

- domain rules and state transitions;
- quantity and unit behavior;
- inventory reconciliation;
- planning constraints;
- validation and authorization policies;
- mapping and normalization;
- cost and quota calculations;
- prompt-context selection and redaction rules.

Unit tests must be fast, isolated, and deterministic.

### Contract tests

Contract tests verify:

- frontend and backend API compatibility;
- structured AI inputs and outputs;
- event and message schemas;
- serialization and versioning behavior;
- backward-compatibility promises;
- validation of required fields, units, ranges, and identifiers.

Published contracts must have automated compatibility checks.

### Integration tests

Integration tests cover real boundaries such as:

- database persistence and migrations;
- authentication and authorization integration;
- background processing;
- cache and locking behavior;
- external provider adapters using controlled environments or recorded fixtures;
- infrastructure configuration where practical.

Use disposable, isolated test environments. Tests must not depend on shared developer data.

### End-to-end tests

End-to-end tests validate critical user journeys from the browser through authoritative state changes. Candidate journeys include:

- household creation and onboarding;
- preference and restriction management;
- pantry initialization and correction;
- shopping-list generation and editing;
- weekly planning;
- selecting a meal for the current context;
- completing a guided cooking session;
- handling an AI or network failure without losing state;
- exporting or deleting household data.

The end-to-end suite should remain focused on high-value journeys rather than duplicating all lower-level tests.

### AI evaluations

Model-assisted features require versioned evaluations rather than relying only on conventional pass/fail unit tests.

Evaluation suites should measure:

- schema validity;
- domain consistency;
- allergy and restriction compliance;
- food-safety behavior;
- quantity and unit consistency;
- relevance to pantry, equipment, time, and skill;
- completeness and actionability;
- uncertainty and clarification behavior;
- localization quality;
- latency, usage, and cost;
- regression against accepted baselines.

Use controlled fixtures, recorded responses, deterministic simulators, or explicitly versioned datasets. Live-provider tests must be separated from the default fast test suite and must have bounded cost.

### Security tests

Security verification should include:

- authentication and session behavior;
- horizontal and vertical authorization;
- household data isolation;
- input validation and injection resistance;
- secret handling;
- dependency and container scanning;
- rate limiting and abuse controls;
- prompt injection and data-exfiltration scenarios;
- privacy, retention, export, and deletion behavior.

Threat modeling must identify which checks are automated and which require review or operational controls.

### Accessibility tests

Accessibility quality includes automated checks and human review for:

- semantic structure;
- keyboard navigation;
- focus order and focus visibility;
- screen-reader labels and announcements;
- contrast and non-color indicators;
- responsive zoom and text scaling;
- time-sensitive cooking interactions;
- error messages and recovery paths.

Automated tools cannot replace manual assistive-technology testing.

### Localization tests

Localization testing must cover:

- missing and unused translation keys;
- fallback locale behavior;
- pluralization and grammatical variants;
- date, time, number, currency, and unit formatting;
- right-to-left readiness when supported;
- layout expansion and truncation;
- locale-independent internal identifiers;
- ingredient and culinary terminology by region.

### Performance and resilience tests

Performance testing should be based on documented workloads and service-level objectives. Relevant scenarios include:

- concurrent API use;
- planning and AI workflow latency;
- background-job throughput;
- database growth and query behavior;
- cache effectiveness and invalidation;
- provider timeout, retry, and circuit-breaker behavior;
- degraded operation during partial outages.

## Test data

- Never use production personal data in development or automated tests.
- Use synthetic households representing diverse languages, equipment, skills, restrictions, and pantry states.
- Clearly identify allergy and food-safety cases.
- Version evaluation datasets and document their origin and intended coverage.
- Keep fixtures understandable and small enough to review.
- Redact recorded provider responses before committing them.

## Continuous integration gates

The initial CI design should eventually enforce, as relevant:

1. formatting and static analysis;
2. unit tests;
3. contract validation;
4. integration tests;
5. build and package verification;
6. dependency and secret scanning;
7. accessibility and localization checks;
8. selected end-to-end smoke tests;
9. AI evaluation thresholds for affected workflows;
10. migration and deployment validation.

Exact tools and thresholds require ADRs after the technology stack is selected.

## Defect handling

A defect report should include:

- observed and expected behavior;
- affected user journey and environment;
- reproducible steps or diagnostic evidence;
- privacy-safe logs and correlation identifiers;
- severity and safety impact;
- regression test expectations.

Food-safety, allergy, data-isolation, credential, and destructive-data defects require explicit incident evaluation.

Testing plans must distinguish product defects from test defects, environment failures, expected behavior requiring clarification, and accepted limitations.

## Definition of tested

A change is considered tested only when:

- the relevant risks are identified;
- appropriate automated tests exist and pass, or failures are documented;
- required manual verification is recorded;
- AI behavior is evaluated when affected;
- failure and recovery paths are covered;
- requirements have evidence or an explicit coverage gap;
- defects and residual risks are recorded;
- documentation and acceptance criteria match observed behavior;
- the testing plan and `docs/plan-status.md` are current;
- known gaps are documented truthfully.
