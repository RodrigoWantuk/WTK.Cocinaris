# Frontend asset license audit (PLAN-0014)

Date: 2026-07-31  
Scope: `apps/frontend/public` and referenced remote media in the Emergent snapshot.

## Findings

| Asset class | Location / usage | License / provenance note | Action |
|---|---|---|---|
| App icons / favicon | `public/` CRA defaults and snapshot assets | Inherited from Emergent snapshot; treat as product-owned after import | Keep; replace if licensing becomes unclear before public marketing use |
| Recipe / pantry imagery | Remote URLs embedded in mock fixtures (`src/lib/mockData.js`) | Third-party stock/demo URLs used only for mock prototype | Do not ship mock remote imagery as production marketing assets without replacement or explicit license |
| UI icons | `lucide-react` | ISC | Keep |
| Fonts | Lora and Manrope, bundled from `@fontsource-variable/lora` and `@fontsource-variable/manrope` 5.3.0 (since PLAN-0032; previously loaded from the Google Fonts CDN) | SIL Open Font License 1.1 | Keep; the license permits bundling and redistribution |

## Conclusion

No secret credentials or Emergent private assets were found in `public/`. Mock remote images remain prototype-only and must not be treated as cleared production photography. A follow-up plan should replace fixture imagery before a public marketing launch.

## Correction — 2026-10-28 (PLAN-0032)

The conclusion above missed `public/index.html`. It loaded `https://assets.emergent.sh/scripts/emergent-main.js` and initialized a PostHog analytics snippet with session recording, using an Emergent-owned project key. It also loaded Google Fonts and had the title "Emergent | Fullstack App". All of this shipped in production builds.

PLAN-0032 rewrote the entry page with same-origin content only and bundled the fonts. It added `yarn guard:third-party-html` (CI) and an equivalent check in the deploy script. The live deployment was hot-fixed the same day; see PLAN-0031 and PLAN-0032 progress logs.

