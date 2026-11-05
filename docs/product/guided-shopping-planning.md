# Guided Shopping Planning ("Planejar compras")

- **Status:** Slices 1 and 2 of 7 implemented; later stages are specified in [PLAN-0062](../plans/PLAN-0062-guided-shopping-planning.md)
- **Last updated:** 2026-11-04

## Purpose

Help someone going to the market who does not know what to cook. One setup screen and dish cards produce a shopping list net of the pantry. This is a guided entry into the existing menu draft, list, pantry, and cooking flows, not a parallel planner.

The owner's decisions and the full specification of later slices are in PLAN-0062.

## The flow today

1. **Setup (`/app/compras/planejar`).** One screen with the last answers preselected: days, meals, meals eaten out, how to cook, what matters most, and, unless the person cooks a dish each day, when to prepare and for how long. An optional starting ingredient. People and restrictions come from the profile. People can change for this plan; restrictions change only in the profile.
2. **Cards (`/app/compras/planejar/:sessionId`).** One dish at a time, with "+N itens novos" and what it reuses. "Sim", "Sim, 2 vezes", "Não" with an optional reason, "Desfazer", "Quero outras opções", and "Já está bom, ver a lista".
3. **Review (`.../revisar`).** Dishes in a suggested order, what spoils first coming first. Any dish can be given a date, changed, swapped, or removed. "Usar este plano" accepts the menu.
4. **List.** The existing list, with "Já tenho" and "Não achei este item" on each line, "Compartilhar lista", and "Separar por lugar".
5. **Arrival (`/app/cardapio/:menuId/chegada`).** "Cheguei do mercado": where each purchase goes and how to divide it, recorded with one confirmation.

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

- **"Não achei este item"** names the affected dishes and offers: buy something else in its place (checked against restrictions; the recipe does not change, and "Mudou alguma coisa?" handles it while cooking), buy later (the line leaves the pending count), or change the dish, which opens the menu.
- **"Separar por lugar"** groups by market, street market, butcher, or bakery. The default comes from the store section, and a line can be moved. The shared text follows it.
- **Arrival uses the checked lines,** or all lines to buy when none is checked. It asks how much came only for perishable items with no confirmed package size. Any place can be changed.
- **Storage advice is advisory and off by default.** With the flag off, a purchase stays whole and nothing mentions freezing or dates. See [`storage-guidance-review.md`](../domain/storage-guidance-review.md).
- **A freezer needs no question.** Only an equipment list without a freezer rules freezing out; an empty list says nothing.

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
| Substitute conflicts with a restriction | Refused, with the reason |
| Part of "Guardar tudo" fails | The screen says how many; trying again never counts anything twice |
| Nothing to store | Explains and links back to the list |

## Offline list

A market often has no signal, so the list keeps working without it ([ADR-0012](../architecture/decisions/0012-offline-shopping-list.md)).

- **What opens.** The application, and the last lists opened on this device (at most five menus). Other screens say "Precisa de internet" and link to the list. The list shows when it was saved.
- **What works.** Checking a line, and "Comprar depois" and back. Marks reach the server by themselves when the connection returns, oldest first.
- **What does not.** "Comprei", "Já tenho", "Cheguei do mercado", substitutes, places, and package sizes need the server, so they are disabled and the screen says so.
- **Banner.** "Sem internet. Suas marcações serão enviadas depois."
- **A line that changed.** A mark on a line no longer on the list is not sent: "N marcações não se aplicam mais", with the names and a dismiss.
- **Leaving.** Logging out erases the device. Another person signing in erases the previous person's data, including unsent marks. An ended session goes to sign-in, keeps the unsent marks for the same person, and closes the offline list until then.

## Not yet (PLAN-0062 later slices)

Cook-by limits and warnings, repeat or save a plan, reusable components, frozen whole dishes, the timed session route, and push notifications. Shelf-life dates and freezing advice need the owner-reviewed storage table.
