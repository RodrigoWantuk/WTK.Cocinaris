# Primary User Journeys

- **Status:** Accepted
- **Last updated:** 2026-07-30

## Journey design rule

The journeys share a domain but do not force one entry path. A visitor may begin by understanding the product before authentication. An authenticated user may begin from the contextual home, a purchase, a few ingredients, a saved recipe, a menu item, an urgent product, or an intention to cook something unrelated to inventory.

## 1. Understand the product before login

```text
Open the public entry page
→ understand the core problem and useful outcomes from concise text
→ optionally watch or interact with an accessible demonstration
→ review how inventory, planning, suggestions, guided cooking, and reconciliation connect
→ inspect policy information
→ create an account or sign in through the backend-managed authentication flow
```

The public page remains understandable without video, animation, autoplay, personal data, or authenticated API access. Demonstration content is synthetic and never implies that an unavailable capability is already live.

## 2. Return home and decide what to cook

```text
Open the authenticated home
→ receive a safe personal or neutral greeting based on local daypart
→ see the localized primary question “What shall we cook today?”
→ inspect relevant accepted menu entries
→ inspect inventory-based suggestions that prioritize products needing attention
→ inspect suggestions based on the confirmed user profile
→ optionally choose “Help me choose” and answer one or two material questions
→ understand why each option is suggested
→ start, inspect, adapt, schedule, save, favorite, replace, reschedule, or ignore an option
```

The home uses the user's saved or browser-reported IANA timezone, not the server timezone or precise location. Missing menu, inventory, profile, or AI context removes or degrades only the affected suggestion source. Urgency influences ordering and explanation but never forces a choice.

## 3. Plan before shopping

```text
Select period and meal context
→ optionally include profile, inventory, equipment, goals, and purchase limits
→ generate multiple recipe and preparation possibilities
→ inspect, edit, reject, lock, or regenerate individual suggestions
→ prepare now, schedule, save, favorite, or discard
→ accept a plan
→ create flexible reservations and advance actions
→ generate a shopping list with package-remainder reasoning
```

The user may mix fixed dates with unscheduled options. Simulation and draft modes do not mutate the active plan or inventory reservations.

## 4. Register a purchase

```text
Manual item, manual list, or receipt photograph
→ parse and normalize products and quantities
→ show uncertain or missing information
→ require quantity confirmation
→ create inventory lots
→ assign storage and shelf-life evidence
→ show the organized inventory and attention state
```

A source image is temporary and is deleted after parsing or immediately after a reported failure.

## 5. Decide what to cook from a direct request

```text
Start a recipe request from any supported route
→ optionally select inventory, manually entered products, equipment, technique, time, effort, or shopping permission
→ lock products or quantities that must not be used
→ receive explainable suggestions
→ see products that need attention and preservation alternatives
→ choose freely
→ inspect the normalized recipe
→ prepare now, schedule, save, favorite, or discard
```

The user is never forced to select the most urgent product. This direct journey remains available even when the user bypasses the contextual home.

## 6. Use a scheduled menu item

```text
Receive an in-product, push, or email reminder
→ inspect readiness, missing products, uncertainty, and advance actions
→ start the planned recipe
or adapt the recipe
or replace only this menu item
or reschedule
or ignore the plan
```

The rest of the accepted plan is preserved unless the user approves a broader recalculation.

## 7. Guided cooking

```text
Select recipe and portions
→ review mise en place
→ confirm lots, substitutions, equipment, thawing, and advance preparation
→ start an execution explicitly
→ follow staged instructions
→ request more detail for any stage
→ open text troubleshooting when reality diverges
→ adapt active instructions and record the reason
→ finish cooking
```

Viewing a recipe does not start an execution.

## 8. Finalize and reconcile

```text
Review proposed usage
→ adjust consumed quantities, substitutions, omissions, portions, leftovers, freezing, and waste
→ confirm atomically
→ release reservations
→ record result, rating, difficulty, notes, and optional photos
→ choose whether execution changes become a revision or derived recipe
→ update future decisions
```

If reconciliation is not completed, the execution remains explicitly pending rather than producing a false inventory state.

## 9. Preserve food instead of cooking it

```text
Open attention dashboard or an inventory-based home suggestion
→ inspect estimated urgency and information source
→ choose recipe, freeze, divide, prepare a component, correct data, discard, or remind later
→ apply an explicit lifecycle transition
→ recalculate shelf life and future availability
```

## 10. Operate during AI degradation

```text
AI provider unavailable
→ show clear capability status
→ keep the public entry and authenticated home available
→ show accepted menu entries, deterministic readiness, saved recipes, favorites, and deterministic inventory attention when their services are available
→ continue browsing and editing inventory
→ continue or finalize active cooking
→ manage menu, shopping, favorites, photos, and privacy
→ use deterministic quick-choice filtering when possible
→ retry AI-only work later
```

Authoritative state remains consistent during provider failure.

## 11. Exercise privacy rights

```text
Open privacy center
→ inspect categories and purposes
→ correct data, timezone, or preferences
→ export a readable copy
→ delete selected recipes, executions, photos, or inventory
→ revoke optional permissions
→ request account deletion
→ track deletion progress and legally justified retention
```
