# Reference Persona Evidence — 2026-07-28

- **Status:** Accepted discovery evidence
- **Last updated:** 2026-07-28
- **Source:** Project owner acting as the first customer and reference stakeholder
- **Related record:** [`Stakeholder discovery`](2026-07-28-stakeholder-discovery.md)

## Purpose

This document preserves the concrete personal observations, approximate frequencies, priorities, and behavioral evidence supplied during discovery.

The reference persona is evidence for product decisions, not a universal rule. Product behavior must generalize through configurable profiles, optional context, and progressive learning.

## Current living and eating context

- The reference user lives alone.
- The number of meals that may be eaten at home is known most clearly near the end of the week and varies significantly.
- The user estimated approximately 5 to 14 relevant meals per week, 5 to 10 restaurant occurrences, 4 to 9 delivery occurrences, and 3 to 4 improvised meals or snacks. These were conversational estimates with overlapping categories and were not intended as a mathematically reconciled weekly ledger.
- Improvised meals and snacks are currently uncommon but the user wants them to become better and more viable.
- Breakfast may be skipped because maintaining convenient food that lasts at home feels like work.
- The user generally knows the next week's home-meal availability during the weekend.

## Pantry and shopping problem

- Avoiding an active pantry makes cooking unavailable at the moment of intent.
- Buying for one person often leaves unused package quantities or prepared-food portions.
- Ingredients and cooked food are discarded because the next use is not planned.
- Shopping every day to avoid leftovers feels excessively burdensome.
- The user wants a pantry that keeps spontaneous cooking possible without requiring constant shopping.
- Purchase input may come from any source. The user accepts manual entry, a manual list, receipt photography, package scanning, barcode scanning, post-cooking confirmation, and checklist review when the workflow is not repetitive.
- Real package size and the useful destination of the remainder are considered a central Cocinaris differentiator.

## Cooking and effort evidence

- A normal meal should usually fit approximately 30 to 40 minutes of total preparation time for this reference user.
- Longer preparation is acceptable on an occasional day when the user intentionally wants something different or more elaborate.
- The kitchen is reasonably well equipped.
- Equipment is underused because setup, organization, and cleanup create friction.
- The user believes useful components and portions can be frozen but often does not research or organize that work.
- All originally proposed abandonment reasons were considered applicable: missing ingredients, no idea what to make, need to shop, preparation time, dishes and cleanup, limited technique, fear of failure, research effort, repetition, expected leftovers, and delivery being easier or more attractive.

## Variety and learning evidence

- The user becomes bored when food is repeatedly similar.
- Everyday variety means avoiding the feeling of always eating the same thing; it does not require a different international cuisine every day.
- Occasional special meals may focus on a particular country or culinary tradition.
- The user wants both simple achievable meals and a path to learn techniques and make more elaborate dishes at home.
- Instruction detail should follow the user's level, while every individual stage can request more detail.
- Real-time text troubleshooting during a cooking stage is considered essential.

## Leftovers, reheating, and freezing evidence

- Repeating exactly the same dish is acceptable, but generally only once rather than continuously.
- Reheating tolerance depends on the food and the day.
- Frozen food is easily forgotten.
- Freezing acceptance must be configurable by the user rather than encoded as one universal list.
- Cocinaris should bring forgotten refrigerator and freezer information into a simple attention interface.
- The system may suggest preservation, freezing, or use, but it must not force the action.

## Reference priority order

The user ordered personal goals as follows:

1. reduce delivery;
2. spend less;
3. eat better;
4. preserve freedom to decide at cooking time;
5. reduce waste;
6. learn to cook;
7. spend less time;
8. dirty fewer dishes;
9. visit the market less often;
10. use kitchen equipment better.

Other users must be able to assign a different order.

## Reference success criteria

A successful Cocinaris experience would:

- maintain useful ingredients at home so cooking remains an available option;
- materially reduce delivery orders;
- make shopping and cooking feel less tedious and laborious;
- help prevent real loss rather than merely recording discarded food;
- preserve flexibility at the moment of cooking;
- surface forgotten food before it becomes waste;
- provide enough variety and support to avoid boredom and failure.

## Planning preferences and generalization

- For this user, weekly awareness is natural, but the product must not hard-code a weekly cycle.
- Planning and pantry review cadence must be configurable as weekly, monthly, every number of days, or another supported interval.
- Meal types must be generic and configurable rather than restricted to breakfast, lunch, and dinner.
- The user may choose which meals, days, people, and quantities participate in a plan.
- Menu planning is optional and acts as a shortcut to a prepared recipe execution.
- The user may ignore, adapt, reschedule, or replace one planned item without affecting the remainder silently.

## Desired first-value moments

The stakeholder explicitly wanted the first substantial release to support all of these value moments:

- import a purchase and see an organized pantry;
- enter some products and receive useful recipe suggestions;
- plan meals before shopping and receive purchase guidance;
- cook a recipe with staged guidance and troubleshooting;
- reconcile what was used and prioritize the remaining food afterward.

A release that only generates recipes does not satisfy the discovery.

## Inventory and shelf-life expectations

- Quantifiable products should use grams, milliliters, or countable units as appropriate.
- Inventory entry should not permit an item with no quantity representation.
- Hard-to-measure staples may use a general availability state.
- The user may enter personal shelf-life knowledge, but Cocinaris should minimize the need by combining printed dates, user information, curated knowledge, regional references, and AI-assisted extraction.
- The user expects partial transitions such as taking 500 g from a 2 kg frozen lot and thawing it before a planned meal.
- Cocinaris may remind the user one day before preparation to move the required quantity to the refrigerator.
- Attention may appear in the dashboard, web push, and email and may support a reminder such as “notify me again in 24 hours.”

## Product flexibility evidence

The stakeholder repeatedly rejected overfitting the application to one person's answers. Questions about meal prep, freezing, leftovers, equipment, plan cadence, and autonomy should become configurable profile or contextual choices where they materially affect a result.

The accepted design pattern is:

- collect stable defaults in the profile;
- ask contextual questions when needed;
- remember a pattern only with appropriate confirmation;
- allow modules to be omitted;
- make richer context improve, rather than gate, the experience.
