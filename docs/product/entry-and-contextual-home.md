# Public Entry and Contextual Home Experience

- **Status:** Accepted
- **Last updated:** 2026-07-30
- **Related journeys:** `docs/product/user-journeys.md`
- **Related release definition:** `docs/product/initial-release.md`
- **Related domain rules:** `docs/domain/planning-and-shopping.md`
- **Future implementation:** PLAN-0011

## Purpose

The public entry and authenticated home must communicate the product's central value without reducing it to a recipe catalog or a generic AI assistant.

Before authentication, a visitor should understand that the product helps transform available food into useful meals by considering inventory, urgency, plans, preferences, time, effort, equipment, and willingness to shop.

After authentication, the home experience should feel personal, intimate, and action-oriented. Its primary question is a localized equivalent of:

> What shall we cook today?

This question remains the stable orientation of the home experience. It must not prevent the user from navigating directly to inventory, planning, shopping, recipes, history, settings, or privacy.

## Public entry before authentication

### Required outcome

The public page must answer three questions quickly:

1. What is this product?
2. What useful outcomes can it provide?
3. Why should the visitor create an account or sign in?

The first viewport must provide a concise value statement and a clear authentication action. Supporting sections may explain:

- understanding what food is available and usable;
- deciding what to prepare now;
- using food that needs attention without forcing a choice;
- planning meals and purchases;
- adapting suggestions to preferences, restrictions, equipment, time, effort, and cleanup tolerance;
- following guided preparation and resolving problems while cooking;
- recording leftovers, freezing, consumption, and waste.

The page must not claim that a capability is live when it is only prototyped, planned, unavailable in the user's plan, or dependent on AI that is currently unavailable.

### Demonstration media

The public page may use a short video, animation, interactive sequence, or illustrated walkthrough to show the closed product cycle.

A useful demonstration sequence is:

```text
Food exists in pantry, refrigerator, or freezer
→ the product understands quantity, state, urgency, and user context
→ the user receives explainable preparation possibilities
→ the user chooses, prepares, and cooks
→ completion reconciles consumption, leftovers, freezing, and waste
```

Rich media is enhancement, not the only explanation. The page must remain understandable and convertible when media:

- is not loaded;
- is blocked;
- fails;
- is disabled by reduced-data or reduced-motion preferences;
- cannot autoplay;
- is being used with assistive technology.

Video requires captions, an accessible name, keyboard controls, and a transcript or equivalent text explanation. Audio must never autoplay. Motion must respect `prefers-reduced-motion`. A static poster or lightweight illustrated fallback is required.

### Public-page boundaries

- No authenticated or personal data is embedded in public output.
- Demonstration data is synthetic and clearly illustrative.
- Account creation and login start through the backend-managed authentication redirect.
- Policy links are visible without fabricating unapproved legal text. The age requirement belongs to the terms of use; the page shows no separate age notice (owner decision 2026-10-28, PLAN-0053).
- A visitor who already has a valid session goes from the public entry straight to the authenticated home. The public page itself never reads the session (owner decision 2026-10-28, PLAN-0053).
- Marketing analytics must not capture pantry contents, restrictions, private notes, or recipe requests.
- The page is localized in English, Portuguese (Brazil), and Spanish.
- The page remains useful at 360 px, 768 px, 1280 px, intermediate widths, 200% zoom, keyboard-only navigation, and screen-reader navigation.

## Authenticated home

### Personal greeting

The home may greet the user by a chosen display name when one is available and safe to display. It must provide a neutral localized fallback when the name is missing, empty, unavailable, or intentionally withheld.

The greeting should reflect the user's local daypart without becoming repetitive, presumptive, or overly familiar. Examples are illustrative localization intent, not fixed source strings:

```text
Good morning, Ana.
Good evening, Ana.
Welcome back.
```

The product must not infer mood, health, family status, or personal circumstances from the time of day.

### Primary question

Immediately after or alongside the greeting, the home must keep a localized equivalent of:

> What shall we cook today?

The tone should be warm, direct, and collaborative. It must avoid:

- guilt about expired or wasted food;
- pressure to follow the menu;
- alarmist food-safety language;
- infantilizing copy;
- a named AI mascot or simulated human relationship;
- claims that the system knows what the user wants before sufficient context exists.

### Local-time context

The home uses the user's local timezone and current local time to determine meal context and presentation order.

Timezone priority is:

1. an explicit timezone saved in the user's profile;
2. an IANA timezone reported by the browser for the current session;
3. a neutral fallback when neither is available.

The system must not use the server timezone as the user's meal context. Precise geolocation permission is not required. The user must be able to review or override the saved timezone. Daylight-saving transitions must be handled through IANA timezone rules rather than fixed UTC offsets.

Daypart boundaries may vary by locale, region, and user preference. A future implementation may define defaults, but it must not assume that one global breakfast/lunch/dinner schedule fits every user.

Time of day influences relevance and wording. It does not alter authoritative inventory state, food-safety evidence, expiration dates, or accepted plans.

### Current production home (2026-10-29)

The production "Hoje" page shows only live, authoritative sources, each loaded and failing independently (PLAN-0021 deterministic tiers, PLAN-0042):

1. **Cooking in progress.** Every recipe execution in progress, with "Continuar".
2. **"O que cozinho agora?"** Opens the recipe suggestion flow, with a shortcut to add a pantry item.
3. **"Use logo".** The five most urgent items of the inventory attention list (PLAN-0037), each with its advisory label and a link to the item.
4. **"Suas receitas".** The three most recent saved recipes.

The accepted-menu tier waits for PLAN-0029. The quick chooser and the AI-ranked recommendations wait for their own plans. The prototype keeps the full contextual-home presentation on mock adapters, and production never shows fixtures.

## Suggestion-source priority

The authenticated home presents preparation possibilities in a stable, explainable priority order. A higher tier appears before a lower tier when it has relevant, valid candidates.

### Tier 1 — Accepted menu entries

Show recipes or preparations from the accepted menu that are relevant to the current local date, daypart, or flexible meal window.

Each candidate should expose, when available:

- planned time or window;
- readiness;
- missing required or optional products;
- thawing or advance-preparation actions;
- conflicts or uncertainty;
- actions to start, adapt, replace, reschedule, or ignore the entry.

A planned entry is an intention, not an obligation. The home must not silently start execution, consume inventory, or replace the accepted plan.

### Tier 2 — Current-inventory suggestions

When relevant accepted menu entries do not fully answer the current need, show suggestions based on usable inventory.

Within this tier, prioritize candidates that use products or lots needing attention soon, while considering:

- quantity and availability confidence;
- storage and lifecycle state;
- entered or estimated shelf-life evidence;
- reservations and locked quantities;
- current meal context;
- time, effort, equipment, cleanup, and shopping tolerance when known;
- preservation alternatives when cooking is not the best action.

Urgency is advisory. The user is never forced to choose the most urgent product. Every urgency-based suggestion must explain which products influenced it and offer correction or preservation paths when the underlying data may be wrong.

### Tier 3 — Profile-based suggestions

Show suggestions that fit the user's established profile even when they do not primarily use current inventory.

Relevant context may include:

- preferences and dislikes;
- allergies, intolerances, and restrictions;
- goals that are safe and appropriate for the product;
- skill and desired instruction detail;
- available time and effort;
- cleanup tolerance;
- equipment and techniques;
- variety and recent history;
- willingness to shop.

Restrictions remain hard constraints where applicable. Preferences are not treated as permanent facts and remain editable.

### Tier 4 — Quick chooser

Provide a visible action equivalent to:

> Help me choose.

The quick chooser asks one or two short questions before returning suggestions. It should ask only questions that can materially narrow the current choice and should not repeat context already known with sufficient confidence.

Examples of useful question dimensions include:

- how much time is available;
- desired effort or cleanup;
- light meal versus substantial meal;
- whether shopping is acceptable;
- whether the user wants to prioritize something already available;
- whether the user wants a familiar option or something different.

The chooser must never become a long onboarding flow. One question is preferred when it resolves the main uncertainty; two are allowed when necessary. The user can cancel or skip without losing previous state.

Answers are request context, not silent permanent profile changes. Any proposal to save them as preferences requires explicit confirmation.

## Presentation and explanation

The home may present tiers as sections, a ranked feed with visible source labels, or another accessible composition, but the source priority must remain testable.

A suggestion should expose enough information to support a decision, including when available:

- why it is being suggested;
- suggestion source: planned, use soon, profile fit, or quick answers;
- estimated active and total time;
- effort or cleanup expectation;
- available and missing products;
- readiness and preparation requirements;
- whether shopping is required or optional;
- food-safety or uncertainty notices without overclaiming certainty.

The interface must not disguise paid placement, advertising, affiliate influence, or sponsored content as personalized recommendation.

## Missing-context behavior

Each source tier degrades independently:

- No accepted menu: skip Tier 1 without presenting an error.
- Empty or unavailable inventory: skip or explain Tier 2 and offer inventory entry.
- Incomplete profile: use only confirmed context and offer progressive profile completion without blocking suggestions.
- No useful candidate: offer the quick chooser, saved recipes, favorites, manual search, or a clear empty state.
- API failure: preserve navigation and show recoverable, source-specific failure states.
- Stale or conflicting data: do not merge or overwrite silently; load the latest state and let the user review or retry.
- Capability not yet available for every personalized source (menu, inventory, and profile all report a permanent unavailable status, as in production before live sources exist): show one notice instead of three empty source sections. The notice says personalized suggestions are coming and links to the pantry and profile, which feed them. Partial unavailability keeps the per-source presentation above. User-facing copy never names internal plans, routes, or implementation terms.

## AI-unavailable behavior

AI degradation does not remove the home.

The system should still provide any available:

- accepted menu entries;
- readiness and missing-item information derived deterministically;
- saved recipes and favorites;
- deterministic inventory attention;
- previously generated normalized recipes whose instructions are stored;
- quick-chooser filtering over deterministic or saved candidates;
- links to inventory, planning, shopping, active cooking, history, and privacy.

AI-only generation or adaptation fails transparently and can be retried later. The frontend never calls an AI provider directly, and AI never mutates authoritative state without explicit application validation and user confirmation.

## Privacy and telemetry

- Local-time context uses timezone, not precise location.
- Timezone collection and storage are documented and editable.
- Telemetry may record anonymous or pseudonymous events such as tier shown, suggestion selected, questionnaire started/completed, or source failure.
- Telemetry must not include product names, pantry contents, private notes, restrictions, raw questionnaire answers, recipe text, or authentication secrets unless a later accepted privacy design explicitly permits a narrowly defined field.
- Recommendation and questionnaire events respect consent and analytics preferences.
- Correlation identifiers and trace IDs must not become cross-user tracking identifiers.

## Localization and accessibility

- All visible copy uses localization keys.
- English, Portuguese (Brazil), and Spanish resources are complete.
- Greetings, dates, dayparts, quantities, units, and pluralization are locale-aware.
- Text expansion must not break the composition.
- Keyboard focus follows route and dialog changes.
- Suggestion source and urgency are never communicated by color alone.
- Cards, lists, carousels, dialogs, and questionnaires expose semantic names, state, position, and errors.
- Reduced motion, captions, transcripts, high contrast, touch targets, safe areas, browser zoom, and virtual keyboards are supported.

## Acceptance summary

The experience is accepted only when a visitor can understand the product before login and an authenticated user can quickly answer “What shall we cook today?” through an explainable path that respects accepted plans, current inventory, urgency, profile, local time, and user choice.
