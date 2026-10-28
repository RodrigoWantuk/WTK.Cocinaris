# Audience, Household, and Profile Model

- **Status:** Accepted
- **Last updated:** 2026-07-28

## Audience priority

The first experience is designed for adults with limited time, organizational capacity, or cooking confidence, especially people living alone.

Priority segments are:

1. adults living alone;
2. beginners and low-confidence cooks;
3. capable cooks who struggle with planning and inventory;
4. frequent delivery users;
5. users seeking better everyday food quality.

Household size alone does not determine fit. Time, unpredictability, effort, and fragmented responsibility are stronger signals.

## Initial household model

The first release has one authenticated account managing one household context.

The account holder records the combined preferences and restrictions that must be respected for the people involved in meals. Individual member accounts, invitations, concurrent editing, and per-member permissions are future capabilities.

The data model must not make future collaboration impossible. Meal-level serving count may differ from the household default.

## Adult-only policy

Cocinaris is intended only for users aged 18 or older who have legal capacity to accept the service terms in their jurisdiction.

The product records an adult-capacity declaration and the accepted terms version rather than collecting a full birth date without another documented purpose. Country availability and legal wording require professional review before launch.

The interface, advertising, onboarding, and community features must not be directed to children.

## Profile information

### Household context

- default number of people;
- variable serving counts;
- region, timezone, language, currency, and measurement system;
- typical meals at home;
- preferred planning and shopping cadence.

### Cooking capability

- overall skill and confidence;
- familiar techniques;
- techniques the user wants to learn;
- equipment, cookware, utensils, and storage capabilities;
- known equipment constraints;
- expected instruction detail.

Overall skill is separate from familiarity with a specific recipe or technique.

### Food preferences and restrictions

The product distinguishes:

- preference;
- dislike;
- dietary pattern;
- intolerance;
- allergy;
- religious or ethical restriction;
- medical restriction.

These categories may have different severity, explanation, and validation behavior. Allergies and medical restrictions are never inferred from behavior.

### Effort and routine

- ordinary maximum preparation time;
- exceptional or special-occasion time;
- energy and effort tolerance;
- cleanup and dish tolerance;
- willingness to repeat meals;
- reheating and leftover preferences;
- freezing and preservation preferences;
- common reasons for abandoning cooking.

Context at cooking time can override profile defaults without changing them permanently.

### Goals and priorities

Users can order goals such as:

- reduce delivery;
- reduce cost;
- improve food quality;
- preserve spontaneous choice;
- reduce waste;
- learn cooking;
- reduce time;
- reduce cleanup;
- shop less often;
- use equipment better.

Recommendation weights follow these priorities but remain explainable and editable.

## Progressive onboarding

Account creation is mandatory but short. The product may collect richer information through:

- an optional complete initial questionnaire;
- questions asked only when relevant;
- post-execution confirmation;
- explicit suggestions to remember a choice;
- editable profile reviews.

A user must reach a useful recipe, inventory, shopping, or planning outcome without completing every profile field.

## Learning policy

Observed behavior creates hypotheses, not silent permanent truth.

The product may notice repeated portion corrections, salt reductions, avoided reheating, or preferred equipment. It can ask whether to save the pattern. The user can inspect, edit, reject, or delete learned preferences.

Sensitive restrictions, identity information, and household membership are never inferred.
