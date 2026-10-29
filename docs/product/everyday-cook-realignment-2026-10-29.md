# Everyday-Cook Realignment — Owner Decisions of 2026-10-29

- **Status:** Accepted (owner decisions, recorded verbatim below)
- **Date:** 2026-10-29
- **Plan:** [PLAN-0054](../plans/PLAN-0054-everyday-cook-realignment.md)
- **Audit:** [`docs/design/ux-audit-2026-10-29.md`](../design/ux-audit-2026-10-29.md)

## Why

The owner asked for a review of the whole flow for an average person, living alone or with family, with little cooking experience, across ordinary families in different countries. The review found a product that works for a meticulous cook with a kitchen scale, and is too demanding for most people:

- **Pantry.** Lots in grams are required for the AI, menus, reservations, and shopping to work.
- **Units.** Only grams, millilitres, and units are used.
- **Cooking.** A checklist of every stage on one screen.
- **Vocabulary.** Lots, protections, reservations, and versions are shown up front.
- **Households.** Everything belongs to one person.

## Owner decisions

1. **Pantry quantities: approximate and exact, both first-class.** "Temos que dar opção. Se a pessoa sabe ou pesou exatamente quanto tem, excelente, se não, pode colocar um pouco ou 'meia embalagem', medidas aproximadas."
   - A pantry item may be exact (weighed or counted), or approximate (for example "a little", "half a pack", "running out").
   - The AI, menus, reservations, and the shopping list work with both. Approximate amounts are never presented as exact.
2. **Measures: kitchen measure and weight, always both, compatible.** "Desde que ambas as opções sempre sejam visíveis e sejam confirmadas serem compatíveis. Conheço gente que só funciona na medida de cozinha, mas eu mesmo faço tudo na balança."
   - Every recipe quantity shows the household measure and the metric value together (for example "1 xícara · 180 g").
   - The two are derived from one deterministic conversion and checked for consistency; a model never invents one without the other being validated.
   - Imperial units and °F apply where the region uses them.
3. **A beginner cooking mode.** One step per screen, large text, the screen kept awake, timers inside steps, preparation before heat, what it should look like, and help when something goes wrong. A one-tap "I used what the recipe asked" completion keeps editing optional.
4. **Advanced concepts hidden by default.** Reservations, protections, lots, versions, variations, and history stay automatic and out of sight. They appear only on conflict or on request, in everyday words.
5. **Shared household from launch.** "Casa compartilhada pode sim desde o lançamento."
   - Two or more people share one pantry, menu, and shopping list.
   - Members with age and restrictions shape suggestions.
   - This changes the ownership model in `AGENTS.md` §4 ("Recipes are privately owned"), so it requires a superseding ADR and a migration plan before implementation.
6. **Region.** "Países ocidentais no geral." Suggestions, staples, measures, and meal patterns follow the household's region (Brazil, Latin America, Europe, North America, and so on). Interface languages stay pt-BR, en, and es.
7. **Audience.** All three profiles (living alone, couples, families with children), with first attention to people living alone: "Podemos focar em quem mora sozinho, mas é muito variado."
8. **Validation.** The owner tests the product exhaustively first; then three external people test it with a script prepared in the repository.
9. **Design system and UX are reviewed.** "Queria que a gente revisse o design system e o UX também, não estou satisfeito." The proposed direction is the design canvas linked from PLAN-0054, pending the owner's reaction.
10. **Ordering of the remaining work.** "Faça como achar mais fluido." The agent orders the phases in PLAN-0054.

## Consequences for existing plans

PLAN-0054 runs before the pending phases of these plans:

- PLAN-0029 Phase 4 (package sizes);
- PLAN-0042 Phase 2 remainder (substitutions during cooking);
- PLAN-0037 Phase 3 (product categories);
- PLAN-0045 (troubleshooting).

Those plans are not cancelled. Where PLAN-0054 absorbs part of them (beginner cooking and help in the cooking mode, categories for staples and store sections), it records the overlap.
