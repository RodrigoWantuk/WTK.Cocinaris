# Product Vision

- **Status:** Accepted
- **Last updated:** 2026-07-28
- **Product:** Cocinaris
- **Discovery source:** [`2026-07-28 stakeholder discovery`](../discovery/2026-07-28-stakeholder-discovery.md)

## Vision

Cocinaris makes home cooking practical enough to fit real life.

It is not primarily a recipe catalog. It is a household food decision and execution system that connects what the user owns, what remains usable, what may be purchased, what the user is willing and able to do, and how those resources can become useful meals.

## Central product question

> Considering what food exists, how much exists, how long it remains usable, and what the user is willing to do, what is the best way to transform that food into useful meals?

Every major module exists to provide context, execute an answer, or learn from the outcome.

## Problem statement

Home cooking frequently loses to delivery because it is a chain of separate burdens:

- deciding what to eat;
- knowing what food is actually available;
- remembering opened packages, freezer contents, and shelf life;
- buying quantities that fit real package sizes and household demand;
- finding a useful destination for package remainders and leftovers;
- fitting the meal to time, energy, skill, equipment, cleanup tolerance, and preference;
- understanding unfamiliar techniques;
- recovering when preparation diverges from the instructions;
- reconciling what was consumed, stored, frozen, or discarded.

The absence of recipes is not the primary problem. The accumulated planning, memory, organization, confidence, execution, and cleanup cost is the problem.

## Initial audience

Cocinaris is initially optimized, in priority order, for:

1. people living alone;
2. people with limited cooking experience;
3. people who can cook but struggle to organize food, shopping, and meals;
4. people who depend heavily on delivery;
5. people who want to improve the quality or healthfulness of everyday eating.

The architecture may support other household sizes, but the first experience must remain excellent for a single adult or small household with limited time.

## Value proposition

> Cocinaris helps people with limited time or cooking experience plan, shop, and cook better at home, with less effort, waste, and dependence on delivery.

It supports three compatible motivations:

- improve everyday food quality;
- spend and waste less;
- learn techniques and make more interesting food achievable at home.

## Product capabilities

### Understand

Maintain an editable profile of household context, cooking skill, equipment, restrictions, preferences, routines, goals, time, effort, cleanup tolerance, reheating, leftovers, and freezing preferences.

### Supply

Track real products as inventory lots, including quantity, source, package state, storage, preparation state, shelf-life evidence, uncertainty, reservations, and lifecycle transitions.

### Decide

Produce explainable meal and preservation suggestions from optional context. Prioritize useful consumption without forcing the user to follow a recommendation.

### Plan

Optionally organize recipes, portions, purchases, flexible reservations, preparation tasks, thawing, and missing-item alerts. Planning is an aid, not a contract.

### Shop

Generate shopping guidance that accounts for current lots, selected meals, package sizes, expected remainders, future uses, required and optional products, and the real purchase outcome.

### Cook

Provide mise en place, staged instructions, adjustable detail, timers and checkpoints when implemented, text troubleshooting, active-instruction adaptation, and explicit execution completion.

### Reconcile and learn

Atomically reconcile consumption, leftovers, freezing, waste, and reservations. Preserve ratings, notes, photos, changes, and user-confirmed learning for future decisions.

## Product principles

- **Reduce decisions without removing agency.** Recommend and explain; do not force.
- **Waste avoidance is a decision factor, not a moral judgment.** Surface urgent food and useful actions without shaming the user.
- **Optional context must degrade gracefully.** Complete inventory improves decisions, but manual or minimal context remains useful.
- **Track real products and lots.** Do not erase storage, package, and shelf-life differences by reducing everything to a generic ingredient.
- **Planning is flexible.** Plans create intentions, shortcuts, reservations, and preparation actions, not obligations.
- **Inventory mutations are explicit.** Proposed consumption becomes authoritative only through confirmed policy or finalization.
- **Uncertainty is visible.** Estimated shelf life, extracted data, and AI output identify source and confidence.
- **AI assists; deterministic systems protect.** Models reason and generate. Application code owns state, arithmetic, authorization, quotas, validation, and safety boundaries.
- **Continuity matters.** Shopping, cooking, storage, and history improve the next decision.
- **Internationalization is foundational.** Language, region, measurement, currency, ingredient terminology, and culinary convention are separate concerns.
- **Privacy and safety are product behavior.** They are not launch-time policy additions.

## Product boundaries

The initial product provides general cooking and nutrition information. It does not diagnose, prescribe clinical diets, replace a nutritionist, or guarantee that a food is safe based only on an estimate.

The product is for users aged 18 or older and requires an account.

## Success indicators

Candidate indicators include:

- reduced self-reported delivery reliance;
- useful food maintained in inventory;
- expired or discarded quantity and preventable-waste events;
- time from intent to accepted meal;
- shopping-plan acceptance and correction;
- inventory reconciliation accuracy;
- completed versus abandoned cooking executions;
- repeated successful recipes and techniques;
- user-confirmed improvement in confidence;
- AI cost, latency, validation, correction, and fallback rates;
- retention without excessive notification pressure.

Exact targets require instrumentation and launch baselines.

## Related documents

- [`Audience and profile`](audience-and-profile.md)
- [`Primary user journeys`](user-journeys.md)
- [`Initial release definition`](initial-release.md)
- [`Domain documentation`](../domain/README.md)
