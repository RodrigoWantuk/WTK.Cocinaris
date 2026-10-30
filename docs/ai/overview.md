# AI Architecture Overview

- **Status:** Accepted
- **Last updated:** 2026-08-05
- **Recipe artifact protocol:** [`recipe-artifact-protocol.md`](recipe-artifact-protocol.md) (`0.3`, Revised under PLAN-0022)

## Purpose

AI is central to Cocinaris's reasoning and interaction, but it does not own application state, safety truth, authorization, or business correctness.

All AI access passes through an application-owned backend gateway.

## AI responsibilities

AI may support:

- profile clarification and natural-language onboarding;
- manual-list, receipt, URL, and image extraction;
- product classification and culinary-role suggestions;
- recipe generation, normalization, analysis, and adaptation;
- meal, shopping, preservation, and preparation proposals;
- skill-appropriate staged instructions;
- text troubleshooting during active cooking;
- explanation of technique, substitution, urgency, and trade-offs;
- bounded summarization or context compression;
- translation and localization assistance subject to review.

Each operation has its own versioned contract, prompt or workflow, model policy, budget, validation, evaluation, and fallback.

## Deterministic responsibilities

Application code owns:

- authentication and authorization;
- inventory quantities, units, lots, reservations, and transitions;
- transaction and concurrency control;
- shelf-life evidence hierarchy and curated safety rules;
- allergies and restriction enforcement;
- state mutation and audit;
- quota and entitlement ledger;
- provider cost accounting;
- idempotency and job state;
- retention, export, and deletion.

A model may propose a command. The application validates it and the user confirms it where required.

## Gateway pipeline

```text
Authorized operation request
        ↓
Quota and entitlement precheck
        ↓
Context selection, minimization, and redaction
        ↓
Versioned structured request
        ↓
Provider and model routing
        ↓
Timeout, cancellation, and bounded retry
        ↓
Structured response
        ↓
Parse and schema validation
        ↓
Domain, restriction, safety, and consistency validation
        ↓
Repair, fallback, clarification, or safe failure
        ↓
Usage settlement and telemetry
        ↓
Presentation or validated application command
```

## Structured protocol

Requests use versioned JSON with:

- operation identifier;
- schema version;
- locale and measurement context;
- explicit tagged sections;
- maximum collection sizes;
- maximum string lengths;
- maximum context and output budget;
- relevant user facts only;
- explicit unknown values;
- source and confidence where applicable;
- stable identifiers that do not expose database internals unnecessarily.

Responses use versioned schemas. Provider-native structured-output features may assist but do not replace application validation.

## Context selection

The backend selects context deterministically for the operation. It never gives a model unrestricted access to the complete profile, pantry, history, or database.

Possible selected context includes:

- restrictions required for safety;
- relevant product lots and urgency;
- locked and reserved quantities;
- equipment involved;
- recent related executions;
- current plan summary;
- active cooking stage and prior troubleshooting.

A cheaper model may compress noncritical narrative context when a measured need exists. Critical facts remain structured and cannot be removed or altered by compression.

## Content reuse

Cocinaris should generate reusable culinary content once when safe and reuse it:

- normalized recipes;
- recipe stages and detail variants;
- generic mise en place;
- translations after validation;
- generic storage guidance;
- catalog classifications;
- non-user-specific preparation variants.

The following remain user- and time-specific:

- allergies and restrictions;
- current lots and shelf-life urgency;
- reservations and locks;
- portions;
- shopping state;
- current execution and troubleshooting;
- personalized recommendation ordering.

Private context is never reused across users.

Ingredient identity ([`ingredient-identity.md`](ingredient-identity.md), PLAN-0055) is the first shared, non-private layer. Ingredient names resolve to a curated catalog once for all users. Embeddings are reused only for the exact same text. The [recipe-idea library](recipe-idea-library.md) (PLAN-0055 Phase 2) builds on them: generic ideas answer suggestions and selections without AI calls. Dish-image reuse by similarity is the next step.

## Operation classes

### Interactive

Examples: stage detail, substitution while cooking, troubleshooting.

Target behavior: a few to several seconds when possible, clear progress, strict timeout, and safe fallback. Safety-critical uncertainty may take priority over speed.

### Short background

Examples: recipe generation, receipt parsing, recipe import, moderate plan generation.

Target behavior: durable job, visible progress, normally completed within approximately a minute, and resumable after navigation.

### Long background

Examples: broad planning, data export, deletion, reprocessing, evaluation.

Target behavior: persistent job with progress and completion notification. The user may close the application.

Exact objectives are defined and load-tested per operation.

## Validation layers

1. transport and parsing;
2. schema;
3. field and size limits;
4. domain identity and units;
5. inventory consistency;
6. household preferences and restrictions;
7. food safety;
8. authorization;
9. current-state and revision consistency;
10. cost and entitlement.

Invalid results never enter authoritative state silently.

## Resilience

Every workflow defines:

- timeout and cancellation;
- retry and repair count;
- idempotency behavior;
- provider and model fallback;
- deterministic fallback where possible;
- user clarification path;
- safe-failure message;
- AI-unavailable product behavior.

Existing inventory, saved recipes, menu, shopping lists, active cooking, finalization, and privacy operations remain available without AI.

## Safety boundary

AI may explain or identify a concern, but curated and deterministic rules govern allergy, storage, cross-contamination, temperature, doneness, reheating, and unsafe substitutions where rules exist.

The product exposes uncertainty and does not guarantee food safety from an estimate.

## Search boundary

The initial release does not use semantic or vector search. Recipe and product discovery use relational queries, structured filters, deterministic scoring, and explicit recommendation algorithms. AI may generate candidates but is not the search engine for stored private data.

## Evaluation

Each operation requires versioned evaluation covering:

- schema validity;
- contextual consistency;
- quantities and units;
- restrictions and allergy behavior;
- food safety;
- instruction completeness;
- usefulness and actionability;
- uncertainty and clarification;
- localization;
- latency and cost;
- regression against accepted baselines.

Provider, model, prompt, or workflow changes pass evaluation gates and can be rolled back or disabled.

## Observability and privacy

Record, where permitted:

- operation and workflow version;
- provider and model;
- request and job correlation IDs;
- latency, retries, repair, and fallback;
- input, output, and cached usage;
- estimated and settled provider cost;
- schema and domain validation;
- user correction, acceptance, or rejection;
- failure category.

Do not log complete profiles, allergy data, prompts, responses, URLs, source images, or private recipe content by default.

See [`usage-and-cost-governance.md`](usage-and-cost-governance.md).
