# PLAN-0059: Installable App, Main-Button Quick Actions, and the "Cozinhar" Page

- **Status:** In Progress
- **Type:** Implementation
- **Priority:** High (owner requests)
- **Owner:** Claude Code (claimed 2026-10-31)
- **Created:** 2026-10-31
- **Last updated:** 2026-10-31
- **Branch:** `agent/plan-0058-phase-3`. It shares a branch with PLAN-0058 Phase 3 because the requests arrived during that run; PLAN-0058 Phase 3 itself (the owner's walkthrough) has no code yet.
- **Pull request:** Draft, opened with the first commit
- **Related documents:** [`docs/design/design-system.md`](../design/design-system.md), [PLAN-0058](PLAN-0058-experience-redesign.md)

## Owner requests (2026-10-30/31)

1. "pode habilitar aquela feature no site do cocinaris que quando você acessa via mobile, ele sugere você instalar o site como aplicativo? tem no meu portfolio isso eu acho"
2. "Quando clica no + central verde da barra, ele abre um menu flutuante pequeno, meia lua, logo acima, pro usuário selecionar se quer cadastrar item à despensa ou cozinhar algo."
3. "Adicionar item à despensa leva pra página que leva atualmente, cozinhar leva pra uma página que ele pode selecionar algum item dele já salvo, ordenado pela quantidade de itens da receita que ele tem à disposição, ou opção pra ele criar nova receita."
4. "E talvez substituir o ícone de mais por outra coisa no botão principal, aquela estrela do botão em destaque da tela inicial talvez."

## Scope delivered

### Installable app

The owner's portfolio only declares a web manifest, which lets Chrome on Android offer the install by itself. Cocinaris does the same and adds its own offer:

- **Manifest** (`apps/frontend/public/manifest.webmanifest`):
  - name, start URL `/app/hoje`, scope `/`, standalone display;
  - emerald theme and Zinc background;
  - icons at 192 and 512 px plus a maskable one;
  - home-screen shortcuts (suggest, add to pantry, shopping list).
- **Icons:** generated from `public/icons/icon.svg` (a pot with steam on emerald) by `apps/frontend/scripts/generate-app-icons.mjs` with Playwright's Chromium. The PNGs are committed.
- **`index.html`:** links the manifest, favicons, and apple-touch-icon, and sets the iOS home-screen meta tags. Everything stays same-origin (`guard:third-party-html` passes).
- **No service worker.** Chromium reports no installability errors without one (CDP `Page.getInstallabilityErrors` returned an empty list on the proxy replica), so no offline caching was introduced.
- **"Instale o Cocinaris" banner** on "Hoje", phones only:
  - **Android and other Chromium browsers:** the browser's `beforeinstallprompt` is captured at startup (`app/install/installPrompt.ts`). The browser's mini-infobar is suppressed, and "Instalar" opens the browser's dialog.
  - **iPhone and iPad in Safari:** the banner explains Compartilhar → "Adicionar à Tela de Início", because Safari has no install event.
  - **When it hides:** it is not shown when the app already runs installed, and "Agora não" hides it on that device for 30 days (`cocinaris.install.dismissedAt`).

### Main button and half-moon menu

- The central button now shows the sparkles icon and opens a half-moon panel just above it, with "Adicionar à despensa" and "Cozinhar", over a dimmed page (`app/shell/QuickActionMenu.tsx`).
- From `md` up the same choices open as a small panel under the header button, now labelled "Começar".
- **Accessibility:** it follows the disclosure pattern. Opening focuses the first action; Escape, a tap outside, or a navigation closes it, and focus returns to the button.

### "Cozinhar" page (`/app/cozinhar`)

- **New recipe first:** "Sugerir com o que tenho" (suggestions from the pantry) and "Escrever receita".
- **Then the saved recipes, ordered by how many of their ingredients the pantry covers:** all of them first, then the fewest missing. Each row shows "Você tem tudo" or "Você tem X de Y", opens the recipe, and has a "Cozinhar" button that starts cooking at once.
- **Backend:** `GET /api/v1/recipes/readiness`.
  - Sources: one pantry read, one ingredient-matching pass, and the cooking proposal's deterministic matching for each active recipe (at most 200).
  - Counting: basics the recipe assumes and optional ingredients are left out.
  - It is read-only.
  - It uses the new `IRecipeStore.ListActiveAsync`.

## Acceptance criteria

- [x] On Android Chrome the site is installable and offers installation from "Hoje"; on iOS Safari the banner explains the steps; nothing shows when the app is already installed.
- [x] The main button opens the half-moon menu with the two actions; keyboard and screen-reader behavior follow the disclosure pattern.
- [x] "Cozinhar" lists saved recipes ordered by pantry coverage, starts cooking in one tap, and offers new-recipe options.
- [ ] The owner checks the installation on a phone after deployment.
- [x] Owner feedback on the half moon ("ficou horrível, desformatada… precisa ser mais delicada") addressed: a compact, translucent 220 × 110 px arc floating just above the button, with two small icons and one-word labels resting on its curve (full names stay as accessible names), a lighter backdrop, and the button turning into × while open; the desktop panel is unchanged.

## Execution state

- **Current run delivery target:** all four requests.
- **Current checkpoint:** implemented and validated as below.
- **Validation (lean):**
  - **Backend:**
    - `RecipeAvailabilitySays…` and the new `CookPageListsRecipesByHowMuchOfThemThePantryCovers` (ordering, counts, another owner sees nothing): 2/2.
    - `RecipeCookNow` unit tests: 13/13.
    - OpenAPI regenerated (additions only); client drift check passed.
  - **Frontend:**
    - Typecheck and ESLint clean; `src/features` and `src/app` 328/328.
    - New tests: install banner (Android prompt, "not now", installed, iOS detection), half-moon menu (open, focus, Escape, outside tap), and the cook page (ordering, one-tap cooking, empty state).
  - **Installability:** CDP `Page.getInstallabilityErrors` returned an empty list on the proxy replica; the manifest is served with 200.
  - **Visual:** phone captures of the open menu and of the cook page on the replica.
- **Not run:** a real phone install (needs the owner's device), full suites.
- **Exact next action:** The owner merges the half-moon fix and checks it on the phone, together with installing the app.
- **Blockers:** None.

## Progress log

### 2026-10-31T01:30:00Z — Claude Code

- Plan created from four owner messages and delivered in one run: installable app (manifest, icons, banner), half-moon quick actions on the main button, and the "Cozinhar" page with recipes ordered by pantry coverage.

### 2026-10-31T02:30:00Z — Claude Code

- Merged via #76 and deployed at `1d0e79f`. On the live site Chromium reports no installability errors. The manifest is served as `text/plain` by the host's Caddy; this does not block installation, and the deploy script does not manage the Caddy config.
- Reworked the half moon after the owner's feedback, into a compact translucent arc with small icons, short labels, and an × on the button while it is open. Checked in a phone capture on the replica.
