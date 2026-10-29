# Cocinaris design system

Status: accepted by the owner on 2026-10-29 ("Vamos fechar assim por hora, ficou bem melhor já"), direction 2 of [PLAN-0054](../plans/PLAN-0054-everyday-cook-realignment.md). The design canvas from that review is the visual reference. This document is the durable rule set.

## Principles

- **An app, not a system.** The screens look like a cooking app on a phone, with no admin panels. Plain white ground, large titles, and rows instead of boxes. A surface is boxed only when it is one tappable unit or a status that must stand out.
- **One action per moment.** Each screen has one black primary pill. Secondary actions are grey pills or text.
- **Color informs.** Neutral by default. Soft tints appear only where they carry meaning: green for "you have it", amber for "use soon", peach for the gentle accent. Color never carries the meaning alone; text says it too.
- **No effects.** No shadows, blur, gradients, or decorative motion. One typeface.

## Tokens

The tokens live in `apps/frontend/src/index.css` (`:root` and `.dark`) and are exposed through `apps/frontend/tailwind.config.js`.

| Token | Light | Use |
| --- | --- | --- |
| `background` / `foreground` | white / #111 | Ground and text |
| `primary` | #111, white text | The primary pill, the add button, the selected chip |
| `secondary` / `muted` | #F4F4F2 | Secondary pills, filled inputs, hover rows |
| `muted-foreground` | #5F5F5F | Supporting text (meets 4.5:1 on white) |
| `border` | #EDEDED | Row dividers |
| `success` / `success-soft` | #1D6B36 / #E6F4EA | "Tem", done, compatible |
| `warning` / `warning-soft` | #7A4E00 / #FFF1D6 | "Usar logo", attention |
| `accent` | #FFE4DA | Gentle highlight |
| `radius` | 1rem | Cards and sheets; pills are fully rounded |

Utilities `status-pill`, `status-success`, and `status-warning` give a short status label.

## Type

Inter, self-hosted through `@fontsource-variable/inter`. External font, script, and style hosts are forbidden by `guard:third-party-html`. Titles are 700 weight with -0.02em letter-spacing (`font-display`). Body text is 15–17px, never below 14px for content.

## Components

- **Button** (`components/ui/button.tsx`): fully rounded. The default is 44px high and the large size is 56px for the main action of a screen. No shadows. A visible focus ring is required.
- **Inputs and selects**: 44px high, rounded-xl, and at least 15px text, so iOS does not zoom.
- **Rows**: lists use `divide-y` rows with the name in 17px semibold and the detail in muted 14px. Pantry, shopping, and recipe lists follow this pattern.

## Shell and navigation

- **Phones**: a bottom bar within thumb reach holds Hoje, Despensa, the black round "+" (add to the pantry), Receitas, and Compras.
- **From `md` up**: the same element sits inline in the header, and "+" becomes a black "Adicionar" pill.
- **The menu lives inside Hoje.** Menu pages light up Hoje, and a menu's shopping list lights up Compras. `/app/compras` opens the list of the current menu.
- **The account menu** (the avatar at the top right) holds Perfil, the interface language, and Sair. The household joins it in Phase 5.
- **Rules**: `app/shell/AppNavigation.tsx`, `app/shell/AccountMenu.tsx`, and `app/ProductionApp.tsx`. The header must not create a containing block (no backdrop filter or transform), or the fixed bottom bar would attach to it.

## Accessibility

- Touch targets are at least 44px.
- The bottom bar marks the current tab with `aria-current="page"`.
- The account menu closes on Escape and on an outside click.
- Status is always written in words, never shown by color alone.
