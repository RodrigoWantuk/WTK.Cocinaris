# Cocinaris design system

Status: accepted by the owner on 2026-10-30. The owner chose the new guidelines with an emerald accent ([PLAN-0057](../plans/PLAN-0057-registry-and-ux-redesign.md), "Owner decision"). This replaces the 2026-10-29 direction of [PLAN-0054](../plans/PLAN-0054-everyday-cook-realignment.md): white ground, black primary pill, no effects. The [UX review of 2026-10-30](ux-review-2026-10-30.md) lists the findings that drive the screen changes. This document is the durable rule set.

## Principles

- **Purpose before decoration.** Every element serves the cook's current task. The screens look like a cooking app on a phone, not an admin panel. Lists are rows; a surface is boxed only when it is one unit (a card, a sheet, a status that must stand out).
- **Layered surfaces, not flat blocks.** Depth comes from elevation: the ground, then surfaces (cards), then elevated layers (menus, popovers, sheets). Neutrals come from one Zinc scale, never pure black or flat grey.
- **One accent, used sparingly.** Emerald marks the primary action, active and selected states, and focus rings. It never fills large areas. Status colors are separate and always come with words.
- **Tactile feedback.** Every interactive element responds at once: a background or border shift on hover and a short press scale. Motion stays at 150–200 ms and disappears when the person prefers reduced motion.
- **Accessibility first.** WCAG 2.2 AA contrast, with ratios noted next to the tokens. Focus is always visible, targets are at least 44 px, and state is in words, never color alone.

## Tokens

The tokens live in `apps/frontend/src/index.css` (`:root` and `.dark`) and are exposed through `apps/frontend/tailwind.config.js`. Components use the semantic names, never raw colors.

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| `background` | zinc-50 | zinc-950 | Page ground (lowest layer) |
| `card` | white | zinc-900 | Surfaces: cards, rows groups, inputs |
| `popover` | white | zinc-850 (`240 5% 13%`) | Elevated layers: menus, sheets |
| `foreground` | zinc-950 | zinc-50 | Text |
| `muted-foreground` | zinc-600 (7.4:1) | zinc-400 (6.9:1 on cards) | Supporting text |
| `primary` | emerald-700, white text (5.5:1) | emerald-500, zinc-950 text (7.8:1) | Primary action, selected state |
| `ring` | emerald-600 (3.6:1) | emerald-400 | Focus rings and focused input borders |
| `accent` | emerald-50, emerald-900 text | emerald-950, emerald-200 text | Gentle highlight (cooking in progress) |
| `secondary` / `muted` | zinc-100 | zinc-800 | Quiet fills, hover rows, chips |
| `border` | zinc-200 | zinc-800 | Decorative dividers and card edges |
| `input` | `#8E8E96` (3.1:1) | zinc-500 (3.7:1) | Control boundaries (need 3:1) |
| `success` / `success-soft` | green | green | "Tem", done, available |
| `warning` / `warning-soft` | amber | amber | "Usar logo", partial, attention |
| `destructive` | red | red | Errors and destructive actions |
| `radius` | 0.875rem | | Cards use `rounded-2xl`, controls `rounded-xl` |

The success green and the emerald accent are different hues and roles. Success always appears with its word ("Na despensa", "Tem"). The accent marks an action or a selection.

### Utilities

- `surface`: a card on the layered ground, with a rounded border at 70% border color. In dark mode it adds a faint inner ring (white at 4%) for a crisp edge without shadows.
- `status-pill`, `status-success`, `status-warning`: a short status label.
- `tabular`: tabular figures for quantities and dates in lists.

## Type

- **Family:** Inter, self-hosted through `@fontsource-variable/inter`. External font, script, and style hosts are forbidden by `guard:third-party-html`.
- **Titles:** 700 weight with -0.02em tracking (`font-display`).
- **Body:** 15–17px, never below 14px for content. Inputs are at least 16px on phones, so iOS does not zoom.
- **Metadata and badges:** 12–13px, semibold.
- **Hierarchy:** comes from weight, size, and the muted color. Muted text must still meet 4.5:1.

## Spacing and layout

- **Spacing:** a 4/8px scale: 4, 8, 12, 16, 24, 32, 48, 64. Related items sit 4–8px apart, and sections 24px or more.
- **Padding:** cards use at least 16px on phones and 24px or more on wide screens.
- **Layout:** mobile first, with no horizontal scroll at 320px and content that reflows at 200% zoom.

## Components

- **Button** (`components/ui/button.tsx`): `rounded-xl` with an emerald focus ring offset from the page, and `motion-safe:active:scale-[0.98]` on press.
  - Sizes: default 44px, large 56px for the main action of a screen, small 36px for secondary actions inside rows.
  - `default`: the emerald primary, once per view.
  - `secondary`: a quiet surface button with a border.
  - `ghost`: tertiary actions.
  - `destructive`: only inside confirmations. A delete entry point is a ghost button with destructive text at the bottom of the page.
- **ChoiceChips** (`components/choice/ChoiceChips.tsx`): one choice among up to about six short options. It is a labelled group of toggle buttons with `aria-pressed`, and the chosen chip carries the accent. Use it instead of a native select for action types, places, and states. Longer or open lists stay in a select or combobox.
- **Inputs and selects:** 44px high, `rounded-xl`, on the `card` surface with an `input` border. On focus, the border turns `ring` and a soft ring halo appears.
- **Rows:** lists use `divide-y` rows, with the name in 17px semibold and the detail in muted 14px.
- **Empty fields:** hidden. A page never lists "Nenhum" for data that was not entered.
- **Quantities:** 1,000 g or ml and above are shown in kg or L in lists.
- **Dates near today:** shown as "hoje", "amanhã", or the weekday, day, and month. The year appears only when it is not the current one.
- **Focused tasks:** guided cooking hides the bottom bar on phones and offers "Sair (o preparo fica salvo)" at the top.

## Page patterns (PLAN-0058)

- **Recipes are shown as image cards, not as text.**
  - `features/recipes/RecipeCard.tsx` renders a card with the image (or the dish initial on a tint picked from its name), the name in up to two lines, and one line of facts.
  - The library uses a grid: two cards per row on phones, three from `sm`, four on wide screens.
  - The home uses a horizontal snap carousel on phones that becomes a grid from `sm`.
- **Home sections** have a heading with an optional accent "Ver …" link (`HomeSection`), and they follow the task order: cooking now, today's menu, starting points, preparation, use soon, recipes.
- **Starting points** are tiles. The main action is a full-width accent tile, and the others are compact surface tiles with an icon.
- **Task lists** are rows on one surface. Each row has an icon in an accent circle, the task, its context, and a chevron, and the whole row is a link.
- **Key facts** (time, servings, times cooked) are small tiles with an icon, a label, and a value.
- **Steps** are a numbered timeline with one surface card per stage. Time chips have icons, and the "how to tell it worked" cue sits in an accent box.
- **Ingredients** are rows with a pantry status icon (success, warning, muted), the household measure under the name, and the amount on the right. A summary such as "Você tem 2 de 4" sits in the header.
- **Pantry lists** are grouped by place (fridge, freezer, cupboard, other). Each group has an icon and a count, and the rows sit on one surface.

- **Empty states** use `components/feedback/EmptyState.tsx`: an icon in a large accent circle, a title, one line of detail, and the first action. An empty list never shows a bare sentence.
- **First run.** While there are no recipes, "Hoje" replaces its empty sections with a welcome that has three linked steps.
- **Progress** (a shopping trip, profile completeness) is a summary card with an icon, a sentence, and a bar.
- **Calendar leaves** stand for days and menus: the weekday or month above, the day number in bold, and the accent for today.
- **Forms with several decisions** are numbered steps, each on its own surface, with optional details in a disclosure surface.
- **Waiting for AI** shows skeletons in the shape of the result, never only a sentence.

## Themes

- The theme follows the operating system by default (`prefers-color-scheme`).
- The account menu offers "Tema: Sistema, Claro, Escuro". The choice is stored per device (`cocinaris.theme`) and applied before the first render (`app/theme/theme.ts`).
- Both themes must meet the contrast rules above.

## Shell and navigation

- **Phones:** a bottom bar within thumb reach holds Início, Despensa, the round "+" (add to the pantry), Receitas, and Compras.
- **From `md` up:** the same element sits inline in the header, and "+" becomes an "Adicionar" button.
- **The menu lives inside Hoje.** Menu pages light up Hoje, and a menu's shopping list lights up Compras.
- **The account menu** (the avatar at the top right) holds Perfil, the interface language, the theme, and Sair. Without a display name, the avatar shows a person icon, never "?".
- **Rules:** `app/shell/AppNavigation.tsx`, `app/shell/AccountMenu.tsx`, and `app/ProductionApp.tsx`. The header must not create a containing block (no backdrop filter or transform), or the fixed bottom bar would attach to it.

## Main button and installable app (PLAN-0059)

- The central bar button carries the sparkles icon and opens a half-moon panel above it with "Adicionar à despensa" and "Cozinhar" (`app/shell/QuickActionMenu.tsx`). From `md` up it is a small panel under the header button.
- The app is installable: `public/manifest.webmanifest` with the emerald theme, icons rendered from `public/icons/icon.svg`, and an "Instale o Cocinaris" banner on "Hoje" for phones. On iOS the banner shows the Safari steps instead.

## Accessibility

- Touch targets are at least 44px.
- The bottom bar marks the current tab with `aria-current="page"`.
- The account menu closes on Escape and on an outside click.
- Status is always written in words, never shown by color alone.
- Focus is visible on every control (`ring` token, offset from the page).
