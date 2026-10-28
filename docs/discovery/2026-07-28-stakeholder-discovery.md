# Stakeholder Discovery Record — 2026-07-28

- **Status:** Accepted
- **Last updated:** 2026-07-28
- **Source:** Product discovery interview with the project owner acting as customer, stakeholder, product owner, and product manager
- **Related plan:** [`PLAN-0001`](../plans/PLAN-0001-document-product-foundation.md)

## Purpose

This document preserves the complete material outcome of the 2026-07-28 Cocinaris product and architecture discovery. It is a structured decision record rather than a verbatim transcript.

Canonical requirements are organized into the product, domain, architecture, AI, security, and operations documents linked throughout this record. When a later document intentionally changes one of these decisions, it must reference the superseded requirement and explain the migration.

Future agents must not rely on the original conversation. This record and the linked canonical documents are the durable source of truth.

## Central product question

Cocinaris exists to answer:

> Considering what food exists, how much exists, how long it remains usable, and what the user is willing to do, what is the best way to transform that food into useful meals?

Inventory, shopping, planning, equipment, preferences, history, recipes, and AI are supporting contexts or actions around this question. Cocinaris is not primarily a recipe catalog.

## Problem observed in real life

The initial stakeholder case is a person living alone with a highly variable number of meals at home. Restaurant and delivery use are frequent. Improvised meals and snacks are uncommon, and breakfast may be skipped because maintaining convenient food at home feels burdensome.

The recurring problems are:

- an inactive or unreliable pantry makes cooking feel unavailable;
- shopping for one person creates package remainders and prepared-food leftovers;
- food is discarded because quantities, opened packages, freezer contents, and shelf life are not coordinated;
- frequent shopping is inconvenient, but infrequent shopping increases uncertainty and waste;
- repeated meals become boring quickly;
- limited technique and confidence increase research and execution effort;
- kitchen equipment is underused because setup, organization, and cleanup feel expensive;
- useful freezing, component preparation, and reuse strategies are known in theory but not planned in practice;
- delivery wins because it appears simpler than deciding, shopping, preparing, cleaning, and handling leftovers.

The product must generalize this real problem instead of encoding one person's routine as a universal rule.

## Initial target audience priority

1. People living alone.
2. People with limited cooking experience.
3. People who can cook but struggle to organize food, shopping, and meals.
4. People who depend heavily on delivery.
5. People who want to improve the quality or healthfulness of everyday eating.

Couples, small households, larger families, and experienced cooks may use the product, but they do not define the first experience. Time, predictability, available effort, and organizational burden are stronger drivers than household size alone.

## Value directions

Cocinaris must support three compatible value directions:

- eat better and with more everyday quality;
- spend less and waste less while still eating adequately;
- develop culinary skill and make more interesting or elaborate food achievable at home.

The accepted value proposition is:

> Cocinaris helps people with limited time or cooking experience plan, shop, and cook better at home, with less effort, waste, and dependence on delivery.

## Product outcomes

The stakeholder's realistic success criteria include:

- keeping enough useful food at home to make cooking a viable spontaneous choice;
- materially reducing delivery use;
- making shopping and cooking feel less exhausting than they currently feel;
- reducing discarded ingredients and forgotten freezer items;
- preserving freedom to change plans at cooking time;
- enabling varied meals without requiring extensive culinary knowledge;
- supporting learning without forcing the user into a training program.

## Profile and onboarding decisions

The account profile may contain:

- household size and default serving context;
- cooking skill and confidence;
- known techniques and techniques the user wants to learn;
- kitchen appliances, cookware, utensils, and storage capabilities;
- allergies, intolerances, restrictions, dietary patterns, dislikes, and preferences;
- typical cooking time, exceptional cooking time, effort, and cleanup tolerance;
- shopping and planning cadence;
- relationship with repeated meals, reheating, leftovers, freezing, and waste;
- goals ordered by importance, such as reducing delivery, cost, waste, time, or improving health and skill;
- desired assistance and autonomy;
- languages, region, timezone, currency, and measurement system.

The product may ask these questions progressively. Account creation must be short, while richer setup remains available. A preference learned in context may be offered as a profile update, but important preferences require confirmation. Allergies and medical restrictions must never be inferred.

The first release uses one account as the household administrator. That user consolidates relevant household preferences and restrictions into the profile. A collaborative household workspace with separate member accounts and permissions is deferred.

## Optional context and graceful degradation

Every supporting module is optional at the moment of use. A recommendation may use:

- the persistent profile;
- registered inventory;
- ingredients entered only for the current request;
- kitchen equipment and technique constraints;
- an accepted menu plan;
- shopping permission and additional-item limits;
- current time, energy, cleanup tolerance, and intention;
- recent meal and execution history.

A user may ask for a recipe with no inventory, enter a few ingredients manually, or use complete lot-level inventory. More reliable context should improve the result, but no optional module is a mandatory gate.

## Inventory is the core operational domain

### Products remain distinct

Fresh tomatoes, canned tomatoes, tomato sauce, and other tomato products are distinct inventory products. They have different storage, shelf life, package behavior, culinary properties, and substitution limits.

The system may classify products into culinary families or roles to help AI and deterministic search, but it must preserve the purchased or prepared product identity.

### Lots, not aggregate totals

Inventory tracks lots. Two quantities of the same product may differ by purchase date, opened state, storage location, preparation state, expiration, and confidence.

Example:

```text
Milk
- 700 ml, opened, refrigerated
- 1,000 ml, sealed, refrigerated
```

### Quantity is mandatory

An inventory entry cannot be created without a quantity representation.

The preferred internal canonical units are:

- grams for mass;
- milliliters for volume;
- units for countable products.

The UI may display kilograms, liters, cups, packages, fractions, or localized units when conversion is valid. Products that are impractical to measure continuously may use an explicit availability state such as `Available`, `Low`, or `Unavailable`.

### Shelf life is contextual

The system must distinguish:

1. manufacturer-printed expiration or use-by information;
2. shelf life after opening;
3. shelf life based on storage, preparation, freezing, thawing, and handling state.

The information hierarchy is:

1. explicit manufacturer information;
2. explicit user information or override;
3. curated Cocinaris product and food-safety rules;
4. curated regional references;
5. AI-assisted estimate;
6. unknown.

AI may extract or suggest information, but it is not the authoritative safety database. Source, confidence, and estimation basis must remain inspectable.

When a source is absent, uncertain, or close to its estimated limit, Cocinaris raises attention and requests action. It does not silently invent facts and does not force an inventory block.

### Advisory behavior

Shelf-life behavior is advisory rather than coercive. The system can show:

- use within an estimated number of hours or days;
- prioritize soon;
- check condition;
- estimate uncertain;
- likely discard.

The user remains able to correct, consume, preserve, postpone a reminder, or discard the product. Alerts must lead to useful actions such as finding recipes, freezing, correcting data, marking consumed, discarding, or reminding later.

### Lifecycle transitions and derived lots

Opening, refrigerating, freezing, thawing, cooking, storing as leftovers, consuming, and discarding are explicit transitions.

If only part of a lot changes state, the application creates a derived lot. For example, thawing 500 g from a 2,000 g frozen lot produces a 1,500 g frozen remainder and a 500 g thawing or thawed lot with a new shelf-life estimate.

A planned meal may generate an advance action such as moving 500 g from freezer to refrigerator the day before preparation.

### Flexible reservations and locks

Accepted plans may reserve quantities without removing them physically. Before a recommendation, the user can preserve, release, reallocate, or ignore reservations and can lock products or quantities that must not be used.

### Provenance and confidence

Important values record their origin, such as:

- user entry;
- receipt extraction;
- barcode or product database;
- package image;
- Cocinaris rule;
- AI estimate;
- recipe execution;
- system calculation.

The main interface stays simple, but the user can inspect how a quantity, expiration estimate, classification, or recommendation was produced.

## Inventory input decisions

The domain must support multiple entry sources without depending on the retailer:

- manual single-item entry;
- manual list entry, optionally parsed into structured data by AI;
- receipt or invoice photograph;
- future package-image and barcode workflows;
- purchase-plan reconciliation;
- recipe-execution results.

For the initial release, manual entry, manual list parsing, and receipt-photo parsing are required. Uploaded source images are temporary. After success they are deleted immediately. On parsing failure, the user is informed immediately, no automatic retry is performed, and the image is deleted.

## Recommendation behavior

Recommendations consider, when available:

- preference and restriction fit;
- inventory utilization;
- opened packages and expiration urgency;
- future package remainders;
- time, skill, energy, equipment, and cleanup effort;
- serving count;
- nutrition and variety goals;
- existing plans and reservations;
- additional purchases;
- freezing and preservation preferences;
- recent executions and user feedback.

Urgent products may be highlighted and alternatives proposed, but the user can choose something else. Cocinaris explains relevant trade-offs rather than rewriting accepted decisions silently.

## Planning and shopping decisions

Menu planning is an optional module. A menu entry is an intention and shortcut to a predefined recipe execution, not an obligation.

A plan may contain fixed dates and times, flexible recipes for a period, or both. The user can generate many suggestions and choose which recipes to prepare now, schedule, save, favorite, or discard.

An accepted menu entry may contain:

- recipe and selected revision;
- optional date and time;
- portions;
- expected ingredients;
- flexible reservations;
- preparation or thawing tasks;
- readiness and missing-item state.

Approaching the planned time, the system may warn about missing required or optional products, uncertain inventory, or required advance preparation.

Shopping is part of the core value. Cocinaris must help decide what to buy based on selected meals, current lots, reservations, package sizes, expected remainder, future uses, and time until the next purchase.

Shopping lists distinguish required, recommended, optional, already available, expected-to-run-out, and operational items. A real purchase may differ from the plan. Cocinaris proposes localized adaptations while preserving accepted meals whenever possible.

Planning supports `Simulation`, `Draft`, and `Accepted` states so the user can explore without mutating inventory reservations or the active plan.

## Recipe identity and import decisions

Each user-owned recipe is an independent database object with a UUID. Supported initial origins are:

- generated by AI;
- parsed from a URL;
- parsed from a photograph of a recipe book, notebook, package, or similar source.

The parser produces a normalized structured recipe and warns about ambiguity, missing ingredients, unused ingredients, inconsistent quantities, absent time or temperature, contradictory steps, preparation prerequisites, and potential safety problems.

After import, Cocinaris stores only the normalized recipe and internal processing metadata. It does not retain the source image, source URL, or original page content.

Recipes accept manual edits and AI-assisted customizations whether they are saved, favorited, or scheduled.

## Recipe revisions, derivation, and ownership

Small corrections and refinements create revisions of the same recipe UUID. Material variations that change method, identity, or result create a derived recipe with a new UUID and a parent-recipe and parent-revision reference.

A favorite follows the current revision by default. A scheduled entry may follow the current revision or be pinned to a specific revision.

A recipe object is never jointly mutable across users. Sharing creates an independent copy with a new UUID and provenance pointing to an immutable shared snapshot. Initial sharing priority is:

1. private share link;
2. direct share to another Cocinaris user;
3. unlisted public link;
4. searchable public catalog in a later release.

## Guided cooking and troubleshooting

Starting cooking is an explicit action. Viewing ingredients and steps alone does not start an execution.

Before starting, a mise en place review checks products, quantities, substitutions, equipment, portions, advance preparation, thawing, and expected time. The review informs rather than blocks.

Instructions are broken into stages. The default detail level follows the profile, but every individual stage can request more detail. Familiarity with a specific recipe or technique is distinct from global cooking skill.

Each stage supports text troubleshooting. The AI receives the recipe revision, active execution state, completed stages, actual products and substitutions, equipment, recorded time, and prior execution changes. It may explain, propose recovery, or update the active instructions.

Troubleshooting changes apply immediately to the execution-local recipe state and are logged. At finalization, the user chooses whether to discard them, save selected changes as a new revision, or create a derived recipe. Emergency corrections never change the permanent recipe silently.

## Execution finalization and learning

Finalization proposes an inventory transaction covering:

- lots and quantities consumed;
- substitutions and omitted products;
- portions produced and consumed;
- leftovers and their storage state;
- frozen or preserved products;
- waste and discard events;
- released reservations.

The user may confirm the proposal or edit it freely. The authoritative inventory update is atomic with execution completion, or the execution remains explicitly pending reconciliation.

An execution may store rating, difficulty, notes, problems, solutions, photos, and intent to repeat. Permanent photos exist only when the user explicitly attaches them to a recipe or execution.

Observed behavior can produce learning hypotheses, but important permanent preferences require confirmation. The user must be able to inspect and correct learned assumptions.

## Community features

A searchable public recipe catalog and ranking are deferred beyond the first release. Publication must always be explicit and must publish a selected immutable revision snapshot.

Future initial ranking signals are:

- user rating;
- completed execution count;
- favorite count.

Private personal ratings and public community ratings remain separate. Public content requires moderation, reporting, privacy, provenance, and rights analysis.

## First release definition

The first launchable release is a responsive web application with:

- mandatory account creation;
- household and cooking profile;
- inventory with lots and lifecycle;
- manual and AI-parsed list input;
- receipt-photo input;
- recipe generation and import from URL or image;
- recipe library, revisions, favorites, and private sharing;
- shopping assistance and shopping lists;
- optional menu planning and flexible reservations;
- guided cooking and mise en place;
- text troubleshooting;
- execution finalization and atomic inventory reconciliation;
- attention dashboard, web push, and email notifications;
- explicit access, correction, export, and deletion workflows;
- useful degraded operation when AI is unavailable.

Every data-rich module remains optional in a specific user flow. The product degrades from complete context to manually entered context without becoming unusable.

## Platform, localization, and account policy

- The first product is web-only and responsive across supported device sizes.
- There is no native mobile application in the initial release.
- An account is required before using the product.
- The product is for adults aged 18 or older. Users must also have legal capacity to accept the terms in their jurisdiction. Country availability requires legal review; the product does not attempt to infer worldwide legal majority from a birth date.
- Initial interface languages are Portuguese, English, and Spanish.
- Metric and imperial measurement systems are configurable independently from language.
- The product is designed internationally with Brazil-first defaults and initial content maturity.

## AI operating and commercial decisions

All model access passes through an application-owned AI gateway. The backend constructs a limited, structured context. Requests and responses use versioned JSON contracts with explicit object counts, field lengths, token or cost budgets, and validation.

A cheaper model may summarize or compress context when justified, but critical facts and restrictions remain structured and deterministic.

Model selection is task-specific. Low-cost models should handle simpler extraction and classification, while more capable models are reserved for complex planning or troubleshooting. The system should reuse normalized recipes, instructions, translations, and other safe generated content instead of regenerating them unnecessarily.

User-specific restrictions, current lots, urgency, reservations, portions, and active troubleshooting remain contextual and must not be reused across users.

The commercial direction is a highly limited free plan, potentially supported by ads, and a paid subscription that removes ads and increases AI allowance. Exact pricing and limits remain open. Usage must be visible to the user. Internal accounting records provider tokens and cost; the user-facing allowance may use normalized AI credits because raw tokens are not comparable across models.

Rigid per-user, per-operation, per-time-window, and global limits are required. Abuse controls must address automated sign-up, multiple-account farming, disposable email, suspicious network and device patterns, upload abuse, and prompt injection without relying on a single intrusive identifier.

## Availability without AI

When AI is unavailable, users can still:

- view and edit profile and inventory;
- inspect saved recipes and instructions;
- view menu plans and shopping lists;
- continue and finalize an existing execution;
- reconcile inventory;
- manage favorites, history, privacy, and account data.

Generation, adaptation, parsing, contextual troubleshooting, and new AI recommendations may be temporarily unavailable with clear status and recovery communication.

## Accepted architecture direction

- Frontend and backend are separate applications with independent builds, tests, deployments, and observability.
- The frontend is React and TypeScript generated and evolved through Lovable. This is a fixed stakeholder constraint.
- The backend is .NET 10 with ASP.NET Core and begins as a modular monolith.
- The API is REST with OpenAPI-generated TypeScript contracts or clients.
- PostgreSQL is the authoritative relational database.
- Important asynchronous work uses persistent jobs, RabbitMQ, a transactional outbox, at-least-once delivery, and idempotent consumers.
- Redis is optional auxiliary infrastructure for cache, distributed limits, short-lived coordination, and progress; it is never the sole source of authoritative data.
- Keycloak is the initial identity provider through standard OpenID Connect. Cocinaris keeps its own user UUID and owns domain authorization.
- Browser authentication uses a backend-managed secure session and avoids placing long-lived provider tokens in frontend storage.
- S3-compatible object storage handles permanent user photos and temporary upload processing.
- OpenTelemetry is the instrumentation standard.
- Python is allowed only as an isolated specialized worker when a concrete library or local-model need justifies it. It does not own the core domain database or authorization.
- Production is initially expected in a Virginia cloud region. Testing may run on the owner's Texas VPS.
- Docker-based packaging is required. Docker Compose is appropriate for development and the initial VPS environment. Kubernetes is not an initial requirement.

## Scale and reliability planning envelopes

These are capacity-planning envelopes, not business forecasts:

| Scenario | Registered accounts | Daily active users | Approximate concurrency |
|---|---:|---:|---:|
| Launch | 5,000 | 500 | 50 |
| Successful first year | 100,000 | 10,000 | 1,000 |
| Unexpected success | 1,000,000 | 100,000 | 10,000 |

The initial architecture must handle launch economically, reach the successful-first-year scenario primarily through horizontal API and worker scaling plus database capacity, and allow AI, ingestion, notifications, and search/read models to separate if unexpected success creates measured bottlenecks.

The initial recovery objectives are directional:

- primary-data RPO measured in minutes, not one day;
- RTO measured in hours;
- partial operation without AI or notifications;
- low-usage deployment windows with backward-compatible migrations and rollback capability.

## Privacy and data protection decisions

Cocinaris must implement privacy by design for LGPD and GDPR-oriented operation. Before launch, professional legal review is required for terms, privacy notice, lawful bases, sensitive-data processing, international transfers, retention, advertising, and country availability.

The first release includes:

- data minimization;
- purpose and provenance records;
- separate consent or permission where appropriate;
- access and correction;
- export in a readable format;
- deletion of recipes, executions, photos, inventory, and account;
- consent and notification preference management;
- a traceable account-deletion workflow;
- documented retention by data category;
- processor and subprocessor inventory;
- international-transfer review because production is expected in the United States;
- minimized and redacted AI context;
- no default logging of complete prompts, profiles, allergy data, or model responses.

Allergies, intolerances, and health-related restrictions require heightened controls. The product provides general nutrition and calorie estimates with a disclaimer and does not replace a nutritionist or provide medical treatment in the initial release.

## Decisions intentionally deferred

- Exact cloud provider and managed-service products.
- Exact AI providers, model list, fallback order, and price-to-credit conversion.
- Exact free and paid limits, subscription prices, billing provider, and ad network.
- Exact shelf-life reference sources and regional curation workflow.
- Detailed frontend design system, state library, and localization library.
- Searchable public recipe catalog, public profiles, and ranking algorithm.
- Collaborative household accounts and member permissions.
- Barcode and broad package-image product databases.
- Clinical nutrition, weight goals, and professional nutritionist integration.
- Native mobile applications.
- Exact retention periods and launch-country legal matrix.

## Required canonical reading

Future agents must read, as applicable:

- [`../product/vision.md`](../product/vision.md)
- [`../product/audience-and-profile.md`](../product/audience-and-profile.md)
- [`../product/user-journeys.md`](../product/user-journeys.md)
- [`../product/initial-release.md`](../product/initial-release.md)
- [`../domain/README.md`](../domain/README.md)
- [`../domain/inventory-lifecycle.md`](../domain/inventory-lifecycle.md)
- [`../domain/planning-and-shopping.md`](../domain/planning-and-shopping.md)
- [`../domain/recipes-and-cooking.md`](../domain/recipes-and-cooking.md)
- [`../architecture/overview.md`](../architecture/overview.md)
- [`../ai/overview.md`](../ai/overview.md)
- the applicable security, operations, and ADR documents.
