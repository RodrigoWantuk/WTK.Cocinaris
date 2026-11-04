# Guided Shopping Planning ("Planejar compras")

- **Status:** Slice 1 of 7 implemented; later stages are specified in [PLAN-0062](../plans/PLAN-0062-guided-shopping-planning.md)
- **Last updated:** 2026-11-04

## Purpose

Help someone going to the market who does not know what to cook. One setup screen and dish cards produce a shopping list net of the pantry. This is a guided entry into the existing menu draft, list, pantry, and cooking flows, not a parallel planner.

The owner's decisions and the full specification of later slices are in PLAN-0062.

## The flow today

1. **Setup (`/app/compras/planejar`).** One screen with the last answers preselected: days, meals, meals eaten out, how to cook, what matters most, and, unless the person cooks a dish each day, when to prepare and for how long. An optional starting ingredient. People and restrictions come from the profile. People can change for this plan; restrictions change only in the profile.
2. **Cards (`/app/compras/planejar/:sessionId`).** One dish at a time, with "+N itens novos" and what it reuses. "Sim", "Sim, 2 vezes", "Não" with an optional reason, "Desfazer", "Quero outras opções", and "Já está bom, ver a lista".
3. **Review (`.../revisar`).** Dishes in a suggested order, what spoils first coming first. Any dish can be given a date, changed, swapped, or removed. "Usar este plano" accepts the menu.
4. **List.** The existing list, with "Já tenho" on each line and "Compartilhar lista".

## Rules

- **Answers are the plan's.** They never update the profile. The next plan only reuses them as defaults.
- **Restrictions are hard rules.** The goal, ranking, options, and starting ingredient cannot relax them. See [`restriction-rules-review.md`](../domain/restriction-rules-review.md).
- **Drafts reserve nothing.** A plan reaches "Hoje" and reserves pantry stock only after "Usar este plano". The list works before that, as a simulation.
- **Pack sizes are never invented.** "+N" counts lines, not packages.
- **"+N" is an estimate.** The real list comes from the expanded recipes, so it can differ slightly.
- **The list holds recipe ingredients only,** by owner decision. Whether people keep a second list is to be observed in the external test.
- **"Sim, 2 vezes"** asks how many meals (2 to 4) and how: "Rende para N refeições" (one cooking, double portions, one menu entry) or "Fazer de novo outro dia" (N entries). The first is preselected unless the person cooks a dish each day.
- **"Já tenho"** records a pantry item with the amount the plan needs, marked approximate, with undo for 8 seconds.
- **"Economizar"** favors usually cheaper ingredients and what is at home. It shows no prices.

## States

| Situation | Behavior |
|---|---|
| Empty pantry | Normal flow; "Já tenho" fills the pantry |
| Profile incomplete | Two people by default; no restriction is assumed |
| Guard blocks every card | Other options are offered; the rule is never relaxed |
| AI unavailable or budget exhausted | Saved recipes and library ideas keep working, with one notice |
| No cards left (5 batches used, or 60 cards) | "Não há mais opções agora"; finish with what was chosen |
| Left mid-flow | Everything is saved; resume from setup |
| Discard | The menu is archived and cards deleted; recipes already created stay |
| Undo after a recipe was created | The meals leave the menu; the recipe stays in the library |
| Dish past the session's time | Stays, ranked low, labeled "Passa do seu tempo" |
| Restriction that cannot be checked | The setup screen and each card say so |

## Not yet (PLAN-0062 later slices)

Arrival and portioning, "Não achei este item", splitting by store, the offline list, cook-by limits and warnings, repeat or save a plan, reusable components, frozen whole dishes, the timed session route, and push notifications. Nothing here shows a shelf-life date yet: that needs the owner-reviewed storage table.
