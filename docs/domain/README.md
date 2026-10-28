# Cocinaris Core Domain

- **Status:** Accepted
- **Last updated:** 2026-07-31

## Domain purpose

The core domain coordinates the lifecycle of food and the decisions that transform food into useful meals.

The central question is:

> Considering what food exists, how much exists, how long it remains usable, and what the user is willing to do, what is the best way to transform that food into useful meals?

The domain operates as a closed loop: projected planning leads to explicit actions, execution reconciles reality, and localized recovery preserves unaffected decisions when reality changes.

See [`../product/closed-loop-kitchen-orchestration.md`](../product/closed-loop-kitchen-orchestration.md).

## Domain areas

### Inventory lifecycle

Owns products, lots, quantities, storage, package and preparation states, shelf-life evidence, provenance, reservations, transitions, consumption, leftovers, prepared components, preservation, waste, and reconciliation.

Prepared reusable components are first-class inventory lots and retain provenance from consumed parent lots and preparation executions.

See [`inventory-lifecycle.md`](inventory-lifecycle.md).

### Planning and shopping

Owns optional menu intentions, simulations, drafts, accepted plans, flexible reservations, preparation actions, shopping requirements, package remainders, and actual-purchase adaptation.

Simulation evaluates candidates sequentially against projected inventory. Earlier projected consumption, production, reservations, and purchases change the state used by later candidates.

See [`planning-and-shopping.md`](planning-and-shopping.md).

### Recipes and cooking

Owns recipe identity, normalized content, prepared-component prerequisites, revisions, derivation, provenance, private sharing by copy, guided executions, troubleshooting, execution-local adaptations, finalization, and learning.

See [`recipes-and-cooking.md`](recipes-and-cooking.md).

### Closed-loop orchestration

Coordinates cross-domain impact without bypassing module ownership:

- localized plan recovery;
- preparation dependency routes;
- produced-component allocation;
- projected planning consequences;
- execution reconciliation;
- pending-reconciliation states;
- controlled troubleshooting learning.

Orchestration uses explicit module contracts. It does not become an unrestricted service that edits every module's tables.

See [`../product/closed-loop-kitchen-orchestration.md`](../product/closed-loop-kitchen-orchestration.md).

## Supporting contexts

- account and household profile;
- preferences and restrictions;
- kitchen equipment and capabilities;
- localization and regional product terminology;
- AI workflows;
- notifications;
- privacy and consent;
- subscription and entitlement.

Supporting contexts must not directly bypass the core domain's validation and state transitions.

## Conceptual entities

```text
CocinarisUser
HouseholdProfile
CookingProfile
KitchenCapability

Product
ProductClassification
InventoryLot
PreparedComponentLot
StorageLocation
ShelfLifeEvidence
InventoryReservation
InventoryTransaction
WasteEvent

Recipe
RecipeRevision
PreparedComponentRequirement
RecipeShareSnapshot
RecipeExecution
TroubleshootingEvent
ExecutionResult
ReconciliationProposal

MealPlan
PlanningSimulation
ProjectedInventoryState
PlannedMeal
PreparationAction
PreparationDependency
ShoppingPlan
ShoppingItem
PurchaseReconciliation
PlanRecoveryProposal

AiOperation
UsageLedger
Notification
ConsentRecord
PrivacyRequest
AuditEvent
```

These are conceptual terms, not a final database schema. Implementation plans must refine invariants and contracts without changing accepted product behavior silently.

## Cross-domain invariants

- A user never mutates another user's private recipe or inventory object.
- Quantity or explicit availability state is required for inventory.
- Prepared components use the same authoritative product-and-lot principles as purchased products.
- AI never directly mutates authoritative domain state.
- Important state changes are authenticated, authorized, validated, auditable, and idempotent where retries are possible.
- Execution completion and inventory reconciliation are one logical transaction unless explicitly pending reconciliation.
- Quick meal completion does not imply reconciled inventory.
- Planning simulation and projected inventory remain non-authoritative until explicit acceptance.
- Sequential planning applies earlier projected use and production before evaluating later candidates.
- Planning reservations are flexible and do not claim physical consumption.
- Localized recovery preserves unaffected accepted decisions by default.
- Preparation routes use real dependency and time semantics, not localized labels as authority.
- Uncertainty and provenance remain visible.
- Troubleshooting changes execution-local state by default; durable learning requires explicit user confirmation.
- Recommendations are advisory and editable.
