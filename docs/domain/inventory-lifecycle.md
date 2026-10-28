# Inventory and Food Lifecycle

- **Status:** Accepted
- **Last updated:** 2026-07-28

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

### Derived lots

A partial transition splits the lot.

```text
2,000 g frozen chicken
→ thaw 500 g

1,500 g frozen chicken
500 g thawing chicken, refrigerated
```

The derived lot retains provenance but gains its own state, timestamps, shelf-life estimate, reservation, and subsequent history.

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

Planning reservations and locks are not yet persisted. The preparation boundary retains owner-consistent provenance so a future planning-owned protection query can reject protected consumption without migrating historical batches.

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
