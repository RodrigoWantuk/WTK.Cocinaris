# Inventory and Food Lifecycle

- **Status:** Accepted
- **Last updated:** 2026-10-28 (implemented transition rules, PLAN-0037)

## Product identity

Inventory records the real product, not only a generic culinary ingredient.

Examples of distinct products include fresh tomato, canned peeled tomato, tomato paste, and prepared tomato sauce. A product may have culinary classifications and possible roles, but classification never erases product identity.

## Inventory lot

A lot represents a quantity of one product with a common relevant history and state.

Typical fields include:

- lot UUID and owner;
- product identity and classification;
- quantity and canonical unit or availability state;
- original package quantity when known;
- acquisition and provenance information;
- package state;
- storage location and condition;
- raw, prepared, frozen, thawing, thawed, leftover, or other lifecycle state;
- manufacturer information;
- opened, frozen, thawed, prepared, and transition timestamps;
- shelf-life evidence and current estimate;
- confidence and source by field;
- reservations and locks;
- parent lot for derived quantities;
- cost allocation when available.

## Quantity rules

An entry requires either:

- measurable quantity; or
- an explicit qualitative availability state for products whose continuous use is impractical to track.

Canonical calculable units are grams, milliliters, and units. Display units may be localized when conversion is valid.

Package fractions may be accepted by the UI only when the package size is known or confirmed. The system must not pretend that an unknown package fraction is an exact mass or volume.

### Implemented estimated amounts (PLAN-0054 Phase 2)

The owner decided that an amount can be exact or approximate: "Se a pessoa sabe ou pesou exatamente quanto tem, excelente, se não, pode colocar um pouco ou 'meia embalagem'."

- **Estimate mark.** A measured quantity carries `IsEstimate` (API `quantity.estimated`). It is set when the owner states a share of a package (full, about half, a little) together with the package size. The estimate is the size times 1, 0.5, or 0.25. Counts round to whole units, at least one.
- **No package size.** Nothing is estimated. "Full" and "about half" record the availability `Available`, and "a little" records `Low`. An unknown package fraction never becomes a number.
- **Using an estimate.** Suggestions, menus, reservations, shopping, and cooking read the estimated value like any measured value.
- **Consumption beyond an estimate.** Consuming, discarding, or preparation consumption beyond an estimate empties the lot to zero instead of failing, because the real use is better evidence than the guess. Against an exact quantity the same request is still rejected.
- **Corrections.** A correction states whether the new value is an estimate (`estimated` on a `Correct` adjustment). Without it, the corrected value is exact. Other adjustment types reject `estimated`.
- **Partial transitions.** Both parts of a partial transition keep the source's mark.
- **Storage.** The mark is stored on the lot (`inventory.lots.QuantityIsEstimate`, constrained to measured lots), not in transaction history.
- **Display.** The interface shows an estimate with a leading "~".

## Shelf-life evidence

Shelf life is not one field. The system represents evidence and conditions.

### Evidence priority

1. manufacturer-printed date;
2. explicit user date or override;
3. curated Cocinaris rule;
4. curated regional reference;
5. AI-assisted estimate;
6. unknown.

An implementation may combine evidence but cannot silently replace a stronger explicit source with a weaker estimate.

### Conditions

Shelf-life evaluation may depend on:

- sealed or opened package;
- original packaging integrity;
- refrigeration, freezing, or ambient storage;
- raw or cooked state;
- preparation time;
- thawing method and time;
- user-reported handling;
- regional product characteristics;
- curated safety margins.

### Attention states

The UI may derive states such as:

- normal;
- use soon;
- use with priority;
- check condition;
- estimate uncertain;
- likely discard.

These are advisory. The user chooses and confirms the action.

### Implemented attention rules (PLAN-0037 Phase 2)

The backend computes attention at read time, in the calendar of the time zone saved in the user's profile (UTC when none is saved). Nothing is stored or enforced. Each result carries a state, an optional estimated use-by date, its strongest evidence and stable reason codes.

| Situation | Evidence | Result |
|---|---|---|
| In the freezer | Curated rule | `normal`; `check condition` after 180 days frozen (quality, not safety). The printed date stops counting. |
| Thawing or thawed outside the refrigerator | Curated rule | `check condition` |
| Thawing or thawed in the refrigerator | Curated rule: thaw date + 2 days, or the printed date when it is earlier | date classification below |
| Prepared lot with a recorded shelf-life date | Prepared record | date classification |
| Printed date | Printed date | date classification. An opened package whose result would be `normal` becomes `estimate uncertain`, because the date describes the sealed package. |
| No date, opened | Unknown | `estimate uncertain` |
| No date, sealed or unknown | Unknown | `normal` (pantry staples are not flagged) |

The date classification uses the days left until the estimate: more than 7 days past means `likely discard`; any past date means `check condition`; today or tomorrow means `use with priority`; up to 4 days means `use soon`; otherwise `normal`. Product categories (Phase 3) will refine these generic rules. The interface always shows that this is guidance and that the user should check the food.

## Lifecycle transitions

Transitions include, where applicable:

```text
Acquired
→ Stored
→ Opened
→ Divided
→ Frozen
→ Thawing
→ Thawed
→ Prepared
→ Leftover
→ Consumed | Preserved | Discarded
```

Every transition records actor, time, source, affected quantity, previous state, new state, and relevant evidence.

### Implemented transition rules (PLAN-0037 Phase 1)

The first implementation tracks the lifecycle states `AsAcquired`, `Prepared` (preparation outputs), `Frozen`, `Thawing` and `Thawed`, separately from package state and storage location. A product bought frozen stays `AsAcquired` in the freezer until the user transitions it. The user-initiated transitions are:

| Transition | Allowed when | Effect | Part of a lot |
|---|---|---|---|
| Open | package not already opened | package `Opened`, `OpenedAt`; optional move outside the freezer | No |
| Move | target differs, is not the freezer, and the lot is not in the freezer | storage changes | Yes |
| Freeze | lot not in the freezer | storage `Freezer`, state `Frozen`, `FrozenAt`; thaw instants cleared | Yes |
| Start thawing | lot in the freezer | state `Thawing`, `ThawStartedAt`; storage refrigerator by default | Yes |
| Finish thawing | state `Thawing` | state `Thawed`, `ThawedAt` | No |

These rules check physical consistency only. Refreezing a thawed lot is allowed; advice about it belongs to the advisory attention engine (PLAN-0037 Phase 2). Each lot keeps the latest instant of each transition, and its immutable history keeps every transition with kind, previous and resulting state and storage, and the related lot of a split. Leftover, preserved and recipe-consumption transitions arrive with cooking (PLAN-0042).

### Derived lots

A partial transition splits the lot.

```text
2,000 g frozen chicken
→ thaw 500 g

1,500 g frozen chicken
500 g thawing chicken, refrigerated
```

The derived lot retains provenance but gains its own state, timestamps, shelf-life estimate, reservation, and subsequent history.

A partial transition requires a measured lot and a positive quantity below the current quantity, in the lot's unit. The source keeps the exact remainder and its state (history type `TransitionSplit`). The derived lot copies product, printed date, notes and package state, takes the transition's resulting state and storage, references the source as its parent (`TransitionDerived`), and is created in the same atomic, idempotent write.

## Reservations and locks

A reservation associates quantity with an accepted plan or preparation action. It does not reduce physical quantity.

A user can:

- preserve reservations;
- release or reallocate them;
- allow a recommendation to reorganize them;
- ignore them for one request;
- lock a complete lot;
- lock a minimum quantity that cannot be proposed for use.

The system must detect over-reservation and explain the conflict.

### Implemented reservations and locks (PLAN-0029 Phase 3)

- **Reservations are derived.** Accepted menu meals still to cook reserve pantry quantity in plan order. They use the lot the recipe was written for first, then lots of the same catalog ingredient or normalized name and the same unit (PLAN-0055), earliest printed or prepared date first. They are recomputed on every read and never change physical quantity.
- **Locks are planning-owned protections.**
  - The owner can protect a whole lot or keep a minimum quantity.
  - Protected quantity is set aside before reservations and never proposed for cooking.
  - The owner may still use it explicitly; nothing is blocked.
- **Releasing a meal.** The owner can release a meal's reservations without skipping it. A meal cooked or skipped stops reserving by itself.
- **Over-reservation.** It shows as readiness ("Falta: …") on the meals that do not get enough. The cooking review warns, naming the meals, when a quantity eats into another meal's reservation or the protected part.
- **Not yet:** manual reallocation, and recommendations reorganizing reservations.

## Entry and provenance

Initial entry methods are:

- manual form;
- manual text list with optional AI parsing;
- receipt-photo parsing;
- purchase reconciliation;
- recipe-execution reconciliation.

Every important value records its source. AI-extracted values remain untrusted until validated and, where confidence is insufficient, confirmed.

Quantity is always required. Shelf-life fields may remain unknown or estimated with visible confidence.

## Temporary image policy

Receipt and recipe-import images are transient processing inputs.

```text
Upload
→ validate type and size
→ parse
→ return structured proposal or immediate failure
→ delete source image
```

There is no automatic retry that retains the image after a failure. A new attempt requires a new explicit upload.

## Inventory transactions

Authoritative changes are grouped into transactions, including:

- acquisition;
- correction;
- lifecycle transition;
- recipe consumption;
- leftover creation;
- preservation;
- waste;
- reservation creation or release.

Operations must support idempotency where clients or workers may retry.

### Prepared-component transaction boundary

The first prepared-component implementation records a manual preparation batch in the Inventory module. It consumes one or more measured owner-scoped parent lots and creates one or more stored portions of one declared output product in one authoritative transaction. The v1 command carries an opaque current version for every parent; a missing or stale parent version rejects the complete batch.

Partial preparation consumption preserves the source lot as the authoritative remainder and records the exact consumed quantity in immutable batch provenance. Each output is a normal inventory lot supplemented by immutable prepared metadata: source, prepared time, `Prepared` lifecycle state, storage, and advisory shelf-life evidence. The batch itself persists its declared yield as immutable measured value/unit or qualitative availability state. Batch reads, idempotent replay, and lot-provenance views use that historical batch fact, never the mutable current quantities of output lots. The v1 API permits either a measured yield partitioned exactly by same-unit output portions, or one qualitative output; it does not infer package conversions or food-safety guarantees.

Lot-provenance reads are bounded to the fifty most-recent related batches in each direction, ordered by `PreparedAt` descending then `BatchId` descending. `consumedByTruncated` and `producedByTruncated` independently signal that the corresponding direction has additional older batches; `false` means the returned direction is complete within the current data. The Inventory persistence boundary fetches at most fifty-one identifiers per direction and then batch-loads the retained graphs, rather than issuing one batch query per relationship. Cursor pagination is intentionally deferred until a consumer needs more than this operational inspection window.

Planning reservations are derived and protections are planning-owned (PLAN-0029 Phase 3). The cooking proposal reads both through `IPantryCommitmentLookup`. Protected consumption is advised against, not rejected, because the owner's explicit choice prevails.

## Recipe completion reconciliation

Finalization proposes expected mutations, for example:

```text
Consume 380 g chicken from lot A
Consume 180 ml cream from lot B
Create 2 refrigerated prepared-food portions
Register 1 consumed portion
Release plan reservations
```

The user may confirm or edit. Execution completion and these mutations commit atomically, or the execution is marked pending reconciliation.

### Implemented reconciliation (PLAN-0042 Phase 1)

- Completion runs the preparation workflow with source `RecipeExecution`. The batch records the execution as `SourceReferenceId`, and a unique index allows one reconciliation per execution.
- Inputs are the lots the user confirmed, each protected by the version read with the proposal. The output is at most one prepared leftover lot, named after the recipe, measured in portions (`Unit`), with an optional user-entered use-by date. The attention rules for prepared lots apply to it.
- Unlike a manual preparation, a recipe execution may consume nothing (leftovers of a dish made only from untracked ingredients) or store nothing (everything eaten). Leftover portions may not exceed the portions cooked. History rows carry the reason `recipe_execution`.
- The inventory writes and the execution state change share one PostgreSQL transaction. A stale lot or any failure rolls back everything, so "pending reconciliation" never arises in Phase 1.

## Attention and reminder actions

Each attention item should expose relevant actions:

- find recipes;
- preserve or freeze;
- divide or prepare a component;
- mark consumed;
- discard;
- correct information;
- remind later;
- inspect evidence and confidence.

The system must help prevent forgetting without claiming certainty or blocking user choice.
