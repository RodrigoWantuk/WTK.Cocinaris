# Guided Shopping Planning ("Planejar")

- **Status:** Delivered through PLAN-0062 (all seven slices except its cook-by warnings) and PLAN-0065 (the board, the menu in use, and this rewrite, 2026-11-06)
- **Last updated:** 2026-11-06

## Purpose

Help someone going to the market who does not know what to cook. They say how many days and how many of each meal they want, choose dishes that fit each meal, typed or offered, and get a list net of their pantry. After shopping, Cocinaris says where things go and how to portion them, helps cook several dishes at once, and reminds them before food is lost. This is a guided entry into the existing menu, list, pantry, and cooking flows, not a parallel planner.

The owner's decisions are recorded in [PLAN-0062](../plans/PLAN-0062-guided-shopping-planning.md) and [PLAN-0065](../plans/PLAN-0065-planning-rework.md). PLAN-0065 replaced PLAN-0062's setup questions, dish-by-dish cards, review screen, and the "Quero outras opções" panel of PLAN-0064 with the first screen, the board, and the options sheet below.

## The way in (PLAN-0063)

The "Planejar" tab, the "Planejar" action on the main button, and the "Planejar" tile on the home page all open one page, "Planejamento" (`/app/compras`). It shows the current menu and leads with the next step: start a plan, continue one, what to buy, "Cheguei do mercado", "Começar o preparo", or the menu. The list is called "O que comprar". Loading this page or the home page keeps the current list on the device, so it opens at the market without ever having been opened.

## The flow

A menu is how many of each meal within a period, with no fixed days unless the person gives one. Seven snacks within fourteen days is a valid menu.

1. **First screen (`/app/compras/planejar`).** "Por quantos dias?" (3, 5, 7, 14, or another number up to 14), "Começando" (today, tomorrow, or one of the next days, as chips), and four counters: breakfast, lunch, dinner, and snack, each 0 to 40 and at most 40 in all. The counters do not depend on the days; a counter not yet touched follows the days. "Para 2 pessoas · Alterar" changes people for this menu only. Nothing about cooking is asked. The main action says how many meals: "Montar cardápio (7)". Earlier menus can be repeated from "Repetir um cardápio", which is folded. The last answers come preselected. The screen fits a 360 × 740 phone without scrolling.
2. **Board (`/app/compras/planejar/:sessionId`).** One surface per meal type with a count, such as "Lanches · 3 de 7". Each chosen dish shows its time, "rende N refeições" when it covers more than one, a small day tag only when it has a day, and whether its recipe is still being prepared. One row says what is missing: "Escolher lanche · faltam 4". On phones the tab bar is hidden; "Sair (fica salvo)" leaves. A fixed bar shows "3 de 7 · 9 itens" and "O que comprar". While meals are missing its main action is "Completar (N)", with "Usar este cardápio" beside it, which asks first; when nothing is missing, "Usar este cardápio" is the only action. Both wait while recipes are being prepared. Each meal type's heading has a compact counter (44 px targets) that changes how many are wanted; quick taps send only the last count, and it never goes below what is chosen. A base that a chosen dish uses appears as a quiet row, "Base sugerida: Frango desfiado serve para Wrap e Escondidinho", with "Adicionar" and "Agora não"; it never interrupts. "Mais opções", folded at the end, adds a meal type the menu does not have yet and holds "Distribuir nos dias" (optional: gives each undated dish a day, each meal type spread over the period, what spoils first first) and "Tirar os dias".
3. **Actions on a chosen dish (Slice 3).** Tapping a dish opens a sheet: "Ver receita", "Trocar" (removes it and opens its meal's options at once), "Marcar um dia" (chips of the period's days and "Sem dia"), "Rende quantas refeições?" (1 to 4; covering more than the meal type wants first says "Você pediu 3. Com isso ficam 4." and raises the count only on "Aumentar para 4"), "Pessoas neste prato" (1 to 12, for this dish only; one cooking makes at most 24 portions), "Tentar de novo" when its recipe failed, and "Remover". Each is one explicit change to that dish.
4. **"Completar" (Slice 3).** A sheet proposes one dish for each missing meal from that meal type's offers, never the same dish twice, each with "Trocar". A meal type with fewer offers than missing meals asks for more once when the sheet opens; a meal still without offers says "Sem sugestão por enquanto". Nothing changes until "Usar estes (N)". The dishes are chosen in one request; if some no longer fit, the sheet stays open and says "Não consegui adicionar: …" and how many meals are still missing, and the others stay chosen. Menus started before PLAN-0065 open on the board too, read as their days for each meal type, and the old review address leads there.
5. **Options of a meal type (a sheet).** A text box first, "O que você quer?", up to 120 characters. Typing shows the person's own recipes that match, whatever meal they were saved for, and always "Pedir à IA: {texto}", which asks for that dish and close alternatives; they come first, under "Para “{texto}”". Then two switches, "Minhas preferências" and "O que tenho e já escolhi", and a filter: "Rápido", "Menos compras", or "Econômico". Changing a switch replaces the options with new ones after a short pause, so quick taps send only the last state; the filter only reorders. Six options, each with time, "+N itens novos", and what it reuses. "Ver ingredientes" opens a dish's ingredients with "Quero parecido" and "Não sugerir isso". "Já neste cardápio" repeats a dish. "Ver outras" asks for six more. Choosing keeps the sheet open, with "Desfazer" for five seconds, until the meal type is complete. When a meal type has few options, the sheet asks for more once by itself. A request that breaks a restriction is refused by naming the restriction, with no way around it. Without AI, the sheet says so once and the person's recipes and the options stay usable.
6. **List.** One line per item: check, name, amount, and the dishes it is for. "Comprei", "Já tenho", and "Não achei" sit behind the "⋯" on the line. At the top, one line says how much is left to buy and how much is marked, with a thin bar and a share icon; under it, "Por seção" and "Por lugar" chips. Once something is marked, a bar stays at the bottom, above the tab bar: "Cheguei do mercado (3)". The header reads only the list on the device, so it works without a connection; the bar is disabled offline. Opened from a board, "Voltar" returns to it.
7. **Arrival (`/app/cardapio/:menuId/chegada`).** "Cheguei do mercado": where each purchase goes and how to divide it, recorded with one confirmation.
8. **The menu in use (`/app/cardapio/:menuId`, Slice 4).** The heading and period, then the next step as on "Planejar" ("Comprar N itens", "Cheguei do mercado", or "Abrir preparo"), then today's dishes, later dishes by day, and **"Quando quiser"**: undated dishes by meal type. Days with nothing on them do not appear, and past days are folded. "Cozinhar" is in the row only for today's first dish, or the next in the menu's order; every other action is in the dish's sheet: "Cozinhar", "Ver receita", "Trocar", the day, portions, whether it reserves from the pantry, "Pular", and "Remover". Each changes that dish only. Near the end of the period: "Faltam 2 dias · 3 lanches por fazer". "Mais opções", folded, holds "Preparar vários de uma vez" and "Arquivar cardápio".
9. **Choosing again on a menu in use (Slice 4).** A meal the menu still lacks shows "Escolher lanche · faltam 1" under "Quando quiser", and "Trocar" on a dish takes that dish off and opens its meal's options, the same sheet as on the board; a notice offers to put the dish back ("Desfazer"). The new dish keeps the old one's day and takes the next place in the menu's order; nothing else on the menu moves. The person's own recipes are offered at once, so this works without AI.
10. **"Preparar vários de uma vez" (Slice 4).** Choose a day (from today to the end of the period) and the dishes to make together. Only the day they are cooked changes: each stays on the day it is eaten, with its reservation, and on "Hoje" as before. The dishes show "Preparar sex., 7", and the preparation session of that day gathers them. A dish already cooked ahead for a later day reads "Já preparado".
11. **"Hoje".** Dishes dated today, then "Quando quiser" with the next undated dish of each meal type. A dish with no day whose ingredients should be used soon shows in "Prepare-se" as "Fazer Moqueca até amanhã", with why ("Por causa de Peixe. É uma estimativa: confira o alimento.") and three ways out: "Congelar hoje" (only when the storage table says it can be frozen), "Trocar de prato", and "Deixar como está", which hides it until the day changes.
12. **Menus (`/app/cardapio`).** The list of menus and "Novo cardápio", which opens the first screen. Menus are no longer created empty by dates; those made that way before still open and work.

A menu whose dishes have no day raises no buy reminder, because reminders come from dated meals. The list and the hub still show what to buy.

## Rules

- **Answers are the menu's.** Counts, days, people, switches, and the filter never update the profile. The next menu only reuses them as defaults.
- **Restrictions are hard rules.** No switch, filter, typed request, or ranking relaxes them; a request that breaks one is refused by naming it. See [`restriction-rules-review.md`](../domain/restriction-rules-review.md).
- **Meals are checked, not trusted.** A dish the AI offers for a snack or breakfast must look like one (`MealFit`); full meals are dropped. A dish the person asks for by name, or one of their own recipes, is their call.
- **Tastes are tastes.** "Minhas preferências" off removes tastes only; allergies, intolerances, medical restrictions, diets, and dislikes stay.
- **Typed words stay private.** A request is untrusted, limited to 120 characters, never logged, and a dish made from it never reaches the shared idea library.
- **Drafts reserve nothing.** A menu reaches "Hoje" and reserves pantry stock only after "Usar este cardápio". The list works before that, as a simulation.
- **A menu in use changes one dish at a time.** A swap, a filled meal, a day, or portions touch that dish only; "Preparar vários de uma vez" changes only the day those dishes are cooked.
- **Dates are optional.** Undated dishes are eaten "quando quiser" within the period, and the list is the same either way.
- **Pack sizes are never invented.** "+N itens" counts lines, not packages, and is an estimate until the recipes are prepared.
- **The list holds recipe ingredients only,** by owner decision. Whether people keep a second list is to be observed in the external test.
- **"Já tenho"** records a pantry item with the amount the menu needs, marked approximate, with undo for 8 seconds.
- **"Não achei este item"** names the affected dishes and offers: buy something else in its place (checked against restrictions; the recipe does not change, and "Mudou alguma coisa?" handles it while cooking), buy later (the line leaves the pending count), or change the dish, which opens the menu.
- **"Por lugar"** groups by market, street market, butcher, or bakery. The default comes from the store section, and a line can be moved. The shared text follows it.
- **Arrival uses the checked lines,** or all lines to buy when none is checked. It asks how much came only for perishable items with no confirmed package size. Any place can be changed.
- **Storage advice is advisory.** It comes only from the owner-reviewed table and is off on the test host until the owner turns it on; with it off, a purchase stays whole and nothing mentions freezing or dates. See [`storage-guidance-review.md`](../domain/storage-guidance-review.md).
- **A freezer needs no question.** Only an equipment list without a freezer rules freezing out; an empty list says nothing.

## States

| Situation | Behavior |
|---|---|
| Empty pantry | Normal flow; "Já tenho" fills the pantry |
| Profile incomplete | Two people by default; no restriction is assumed |
| No saved recipes and AI unavailable | The sheet says AI is unavailable once; library ideas still appear |
| AI unavailable or budget exhausted | The person's recipes, typed search, and library ideas keep working |
| A request breaks a restriction | "Essa receita conflita com {restrição}", with no way around it |
| No new dishes (12 requests used, or 120 dishes) | "Não há mais opções agora"; finish with what was chosen, or "Completar" from what is offered |
| Some meals still missing | "Usar este cardápio" asks once: "Faltam 2 lanches. Usar assim?"; the menu page offers to fill them later |
| A recipe is still being prepared | "Usar este cardápio" waits, with "Preparando receitas: 3 de 5"; a failed one offers "Tentar de novo" and "Trocar" |
| Left mid-flow | Everything is saved; "Continuar cardápio" on the first screen and on "Planejar" |
| Discard | The menu is archived and its options deleted; recipes already created stay |
| Undo after a recipe was created | The meals leave the menu; the recipe stays in the library |
| Restriction that cannot be checked | Each dish that may contain it says so |
| Substitute conflicts with a restriction | Refused, with the reason |
| Part of "Guardar tudo" fails | The screen says how many; trying again never counts anything twice |
| Nothing to store | Explains and links back to the list |

## Repeat or name a menu

"Repetir um cardápio", folded on the first screen, lists the last menus in use (at most five).

- **Repeating** starts a new menu with the same counts and people, starting today. Its dishes are already chosen, cooked the same way, and undated unless the original had days. Whatever is still missing is chosen on the board as usual.
- **What may be left out.** A dish whose recipe was archived, a dish that now conflicts with a restriction, a skipped meal, and a meal type the old menu no longer wants. Nothing says which dish was left out; fewer meals are filled. Restrictions are always checked again.
- **The old menu does not change,** and nothing reserves the pantry until the new one is in use.

## Bases and portions

When a chosen lunch or dinner uses something that can be prepared once for several dishes, the board shows a quiet row: "Base sugerida: Frango desfiado serve para Wrap e Escondidinho", with "Adicionar" and "Agora não". It never interrupts, and a menu of only snacks or breakfasts gets none.

- **"Adicionar"** puts the base on the menu, first in the order. It fills no meal. What it makes leaves the list ("Vem do pré-preparo"), and what it needs joins the list.
- **"Agora não"** keeps the item on the list as a required line; it can be bought ready-made.
- **When a base is cooked,** the finished screen proposes one portion per later use, named for what it makes, so later dishes find them in the pantry.
- **When a dish covers several meals,** the finished screen proposes a portion per later meal. "Hoje" shows the portions until they are eaten, with "Comer uma porção".
- **Storage.** Everything is a proposal the person can change. With the storage table off, it proposes the refrigerator only and states no date. Freezer proposals need the owner-reviewed table.
- **Repeating a menu** brings its dishes, not its bases. The items then stay on the list, and new bases may be offered.

## Offline list

A market often has no signal, so the list keeps working without it ([ADR-0012](../architecture/decisions/0012-offline-shopping-list.md)).

- **What opens.** The application, and the last lists opened or loaded on this device (at most five menus); the planning and home pages load the current one. Other screens say "Precisa de internet" and link to the list. The list shows when it was saved.
- **What works.** Checking a line, and "Comprar depois" and back. Marks reach the server by themselves when the connection returns, oldest first.
- **What does not.** "Comprei", "Já tenho", "Cheguei do mercado", substitutes, places, and package sizes need the server, so they are disabled and the screen says so.
- **Banner.** "Sem internet. Suas marcações serão enviadas depois."
- **A line that changed.** A mark on a line no longer on the list is not sent: "N marcações não se aplicam mais", with the names and a dismiss.
- **Leaving.** Logging out erases the device. Another person signing in erases the previous person's data, including unsent marks. An ended session goes to sign-in, keeps the unsent marks for the same person, and closes the offline list until then.

## Preparation session ("Começar o preparo", Slice 6)

After a plan is accepted, the dishes the person chose to make together merge into one route at `/app/compras/planejar/:sessionId/preparo`. The menu page and "Hoje" (on that day) link to it while a dish is still to finish.

- **What is on the route.** The dishes set with "Preparar vários de uma vez" for the earliest such day that still has something to cook (Slice 4 of PLAN-0065). Plans from before then, which asked for a preparation day in their setup, keep the dishes dated on it until a day is set this way. Plans by counts ask for no time budget, so the route shows only its own length.
- **Overview.** Every task with its minute, the total time, a note when it passes the time set aside, and: "Confira se dá para usar as bocas e o forno ao mesmo tempo." Recipes do not say which equipment each stage uses, so these conflicts are not detected.
- **One task per screen.** Large text, the dish's scaled ingredients, a timer, and the screen kept awake. "Deixar N min e seguir" leaves a passive part running as a countdown while the next task opens, with "Enquanto isso: ..." when the two overlap. A task that depends on a wait says so, and does not block: the person decides.
- **Skipping and going back.** "Pular" skips a task, and "Anterior" reopens the last one. Skipping a task does not skip its dish.
- **Cooking sessions.** The first task of a dish starts its cooking session from the menu entry. At the end, each dish is finished in turn on its normal cooking screen, bases first, so what a base makes is in the pantry for the dishes that use it. Only those screens change the pantry.
- **Resuming.** Progress and timers live on the server. A timer is a target time, so it stays right after a reload. Its alarm sounds only while the page is open.
- **The order does not adapt.** The route is computed from the recipes' times. If the person runs late, the order stays and the countdowns stay true.

## Notifications ("Avisos do plano", Slice 7)

- A person turns them on from the profile page, or from the invitation on an accepted menu. The browser asks for permission only after that tap, and "Agora não" is remembered on that browser.
- At most one a day, between 09:00 and 09:59 in the profile's time zone, and only when something to buy or thaw is due that day. Without a time zone, none arrive, and the option says so.
- The text is always the same: "Você tem algo do seu plano para resolver hoje". It never names food, and it opens "Hoje".
- On iPhone, they arrive only with the app installed on the home screen, and the profile page says so.
- A logout turns them off on that browser. The option is hidden when the server has no keys or the browser cannot receive them.
- Each browser subscribes separately. A notification missed because the service was down is not sent later in the day.

## Not yet

- Turning the storage table on for the test host, when the owner decides.
- A server queue for preparing recipes (PLAN-0039); preparation runs from the open app.
- Drag and drop, a calendar view, prices and budgets, curated pack sizes, loose list items, a shared household, and building a menu offline.
