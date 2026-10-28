# Closed-Loop Kitchen Orchestration

- **Status:** Accepted
- **Last updated:** 2026-07-31
- **Related plan:** [`PLAN-0013`](../plans/PLAN-0013-define-closed-loop-kitchen-orchestration.md)

## Product position

Cocinaris coordinates the real transformation of food across inventory, planning, preparation, cooking, preservation, leftovers, and recovery when reality differs from the plan.

It is not reduced to a recipe catalog, pantry checklist, shopping list, or weekly calendar.

The closed loop is:

```text
Inventory and known uncertainty
→ planning simulation
→ explicit accepted plan
→ shopping and preparation actions
→ cooking or quick completion
→ reconciliation with reality
→ updated inventory, components, leftovers, history, and future decisions
→ localized recovery when the loop diverges
```

No stage silently rewrites authoritative inventory or accepted user decisions.

## 1. Localized recovery when reality changes

A plan is an intention, not an obligation.

Relevant divergence includes:

- a meal was skipped, cancelled, replaced by delivery, or prepared outside Cocinaris;
- time, effort, equipment, guests, or preference changed;
- an ingredient became unavailable or unusable;
- an expected preparation was not completed;
- the actual purchase differed from the shopping plan;
- actual consumption or produced portions differed from expectation;
- unexpected leftovers or prepared components now exist;
- a product needs attention sooner than expected.

Cocinaris identifies the affected:

- planned meals;
- reservations;
- shopping requirements;
- preparation, thawing, soaking, marinating, preservation, or freezing actions;
- prepared components and downstream dependencies;
- shelf-life attention and storage consequences.

It offers explicit, reversible choices such as:

- preserve the plan unchanged;
- reschedule the affected meal;
- replace only affected entries;
- adapt portions;
- release or move reservations;
- preserve, refrigerate, or freeze a product;
- use a leftover or prepared component;
- remove obsolete shopping requirements;
- accept a broader recalculation explicitly.

Unaffected accepted decisions remain stable by default.

AI may propose alternatives, but only deterministic, authorized commands mutate authoritative state.

## 2. Prepared components are inventory

A recipe may depend on another preparation rather than only on raw purchased products.

Examples include:

- meat, chicken, fish, or vegetable stock;
- cooked beans, grains, or rice;
- shredded or roasted meat;
- tomato sauce or another base sauce;
- dough, batter, or pastry base;
- purée;
- chopped vegetables;
- caramelized onions;
- marinade;
- cooked or portioned meal base.

A prerequisite can be:

- already available;
- prepared immediately;
- scheduled for another day;
- produced in a larger batch;
- refrigerated;
- frozen in portions;
- purchased ready-made when permitted;
- substituted.

When preparation creates a reusable component, the output becomes one or more inventory lots with:

- stable product identity;
- quantity and unit or explicit availability state;
- recipe and preparation provenance;
- parent input lots and actual consumed quantities;
- preparation timestamp;
- storage location and condition;
- lifecycle state;
- shelf-life evidence, source, conditions, and confidence;
- reservations or allocations;
- history and concurrency state.

Example:

```text
Prepare 2,000 ml beef stock
→ reserve 600 ml for Tuesday sauce
→ reserve 400 ml for Friday soup
→ freeze two 500 ml lots
```

The produced stock is authoritative inventory, not a note attached to a recipe.

A recipe requirement may reference a prepared-product identity and acceptable forms. Planning and cooking distinguish raw inputs, active work, passive elapsed time, cooling or resting, produced yield, storage, readiness, and substitutions.

## 3. Sequential planning simulation

A weekly plan must not independently assume the same inventory for every meal.

Simulation begins from a versioned projection of:

- relevant inventory;
- accepted reservations;
- prepared components;
- profile and restrictions;
- equipment;
- planning constraints;
- confirmed purchase and package-size assumptions.

Each candidate or selected entry applies projected:

- consumption;
- reservations;
- component production;
- leftovers;
- preparation tasks;
- purchases;
- package additions;
- preservation actions.

Later candidates use the projected result.

Example:

```text
Available chicken lot: 1,000 g
Day A projected use: 250 g
Projected remainder before Day B: 750 g
Day B is evaluated against 750 g, not 1,000 g
```

This feedback loop is required from the first planning implementation.

### Package sizes

Known or user-confirmed package sizes affect projected shopping and subsequent availability.

The system may consider:

- net quantity required;
- available package quantity;
- projected remainder;
- storage and freezing capability;
- later uses;
- perishability.

It must not invent an exact package size.

Advanced automatic optimization of every package remainder may be introduced later, but package-size context and sequential inventory feedback are required.

## 4. Simulation before acceptance

Simulation is non-authoritative.

It does not mutate:

- physical inventory;
- accepted plans;
- reservations;
- shopping lists;
- preparation tasks;
- saved recipes.

Before accepting, the user may compare projected consequences such as:

- inventory used and remaining;
- products and components reused across meals;
- shopping requirements;
- known package-size impact;
- expected leftovers;
- refrigerator, freezer, and storage demand;
- number and timing of preparation sessions;
- active effort, passive elapsed time, and cleanup concentration;
- repeated ingredients, techniques, or flavors;
- assumptions and uncertainty;
- impact if a preparation is missed.

The user may accept all, accept selected entries, lock decisions, replace one entry, alter portions, or remain in draft.

Only explicit acceptance creates authoritative plan entries, reservations, tasks, and shopping requirements.

## 5. Reconciliation closes the cooking cycle

Cooking completion proposes what actually happened:

- products and lot quantities consumed;
- substitutions, omissions, and additions;
- portions produced and consumed;
- leftovers;
- refrigerated or frozen portions;
- prepared reusable components;
- preservation;
- waste or discard;
- reservation release.

The user may edit the proposal.

Execution completion and authoritative mutations commit atomically, or the execution remains explicitly pending reconciliation.

### Quick completion

A planned meal may be marked as performed without opening guided cooking.

This does not imply inventory reconciliation.

The product distinguishes at least:

- completed and reconciled;
- completed with reconciliation pending;
- not prepared, skipped, or cancelled.

## 6. Pragmatic inventory uncertainty

Cocinaris does not require false precision.

It distinguishes:

- exact quantity;
- approximate quantity;
- qualitative availability;
- unknown state;
- source;
- confidence;
- inferred value;
- user-confirmed value.

The product asks for confirmation only when uncertainty materially changes:

- recipe feasibility;
- safety;
- shopping;
- planning;
- reservation or reconciliation.

A strong explicit source is not silently replaced by a weaker estimate.

The interface communicates uncertainty clearly without turning every item into mandatory data entry.

## 7. Troubleshooting with controlled learning

During cooking, troubleshooting uses bounded context:

- recipe and revision;
- current and completed stages;
- products and substitutions actually used;
- quantities;
- equipment;
- time and temperature when available;
- previous troubleshooting and execution-local changes.

The result may explain the issue, propose recovery, adapt later stages, or recommend stopping when safety is uncertain.

Changes affect only the current execution by default.

At finalization, the user may:

- discard the adaptation;
- save a recipe revision;
- create a derived recipe;
- keep an execution note;
- explicitly confirm a learned preference.

Allergies, medical restrictions, and other sensitive constraints are never inferred from behavior.

## 8. Multi-day preparation route

The preparation route coordinates dependencies across days and recipes.

It may include:

- thawing;
- soaking;
- marinating;
- cooling;
- chopping;
- cooking a reusable component;
- dividing or portioning;
- refrigerating;
- freezing;
- preserving;
- reheating or finishing.

The route is derived from:

- accepted meals;
- recipe prerequisites;
- prepared-component outputs;
- current inventory;
- storage and equipment;
- dependency relationships;
- real dates, time windows, and timezone.

It represents:

- upcoming tasks;
- tasks that may begin;
- blocked tasks;
- completed tasks;
- overdue tasks;
- downstream meals affected by delay;
- reusable outputs shared by multiple recipes.

Example:

```text
Monday evening — leave beans to soak
Tuesday morning — cook beans
Tuesday noon — reserve 500 g for soup
Tuesday noon — freeze 300 g
Thursday evening — use reserved beans in soup
```

It is not a flat reminder list grouped only by labels such as “morning” or “the night before”.

## Responsibility boundaries

### Backend and deterministic domain logic

Own:

- product and lot identity;
- quantity arithmetic and projected inventory;
- reservations and allocation;
- package-size and availability inputs;
- dependency validation and due-window calculation;
- timezone-aware authoritative scheduling;
- conflict detection;
- reconciliation and atomic transactions;
- concurrency, idempotency, history, authorization, privacy, and food-safety controls;
- structured AI-gateway context and validation.

### Frontend implementation team

Own:

- generated-contract adapters;
- production state management;
- error, stale, retry, conflict, blocked-task, and pending-reconciliation presentation;
- accessibility, localization, responsiveness, and automated frontend tests;
- technical component decomposition.

### Product-design prototype tools

May define and demonstrate:

- visual hierarchy;
- interaction concepts;
- dialogs;
- comparisons;
- route presentation;
- explanations;
- synthetic scenarios.

They do not define authoritative arithmetic, persistence, schedules, shelf life, safety, or API contracts.

## Implementation order

Future implementation should be split into bounded plans:

1. prepared components and derived inventory lots;
2. sequential planning simulation;
3. execution reconciliation and quick-completion states;
4. localized plan recovery;
5. multi-day preparation dependency route;
6. troubleshooting and controlled learning.

Exact module contracts, transactions, events, workers, schemas, prompts, and algorithms require later implementation plans and ADRs where necessary.
