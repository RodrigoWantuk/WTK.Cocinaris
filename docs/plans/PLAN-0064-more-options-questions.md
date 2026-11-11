# PLAN-0064: "Quero Outras Opções" That Works and Asks

- **Status:** Validating
- **Type:** Implementation
- **Priority:** High (owner report)
- **Owner:** Claude Code
- **Created:** 2026-11-05
- **Last updated:** 2026-11-05
- **Branch:** `agent/plan-0064-more-options`
- **Pull request:** Draft, opened with this file
- **Related issues:** None
- **Related ADRs:** ADR-0009
- **Dependencies:** PLAN-0062 (merged), PLAN-0063 (merged)

## Objective

The owner reported three problems while building a menu: suggestions keep following the pantry when that is not wanted, "Não" does not change that, and "Quero outras opções" does not work. They asked for two or three quick questions: stick to the pantry and chosen items or not, very different or similar to the suggested dish, and an optional typed hint of up to 40 characters.

## Causes found in the code

- **New dishes joined the old queue.** A request added dishes, and they were ranked with the rest with no preference, so the same dish usually stayed on screen. This is the "does not work" the owner saw. The new integration test failed on this before the fix.
- **No sign of a request under way.** A request can take up to 60 seconds, and nothing changed while a dish was on screen.
- **Clicks could be lost.** A request made during an automatic batch was dropped silently.
- **Silent no-ops.** At the limit of five batches, or when every new dish repeated an old one, the same screen came back without a word.
- **"Não" carried almost no weight.** A refusal cost a dish's format 10 points, while the pantry adds up to 100 per ingredient needing attention. Requests to the model also always carried the pantry and asked to reuse it.

The cause in the owner's session is inferred from the code and from the database (two batches, 11 unseen cards). No click was observed.

## Decisions

Owner, 2026-11-05: the three questions above, with the hint opened on request.

Agent, within that direction:

| Topic | Decision |
|---|---|
| Defaults | "Pode ser qualquer coisa" and "Bem diferente", because the person opened this to get away from what was offered |
| Filters | Family, cuisine, and time stay under "Mais filtros" and use the same answers |
| "Não" | Each refusal in a row weakens the pantry and list by a third; after three they count for nothing, and the model stops seeing the pantry. An accepted dish restores them |
| A clear answer | Overrides the refusal count until the person answers differently |
| Persistence | The latest answers apply to later batches in this plan, and never reach the profile |

## Invariants kept

- Restrictions are sent and enforced as before, and no answer can relax them. The hint is untrusted text in its own field, with the same treatment as the anchor ingredient.
- A dish made under a hint keeps that hint through expansion, so it never reaches the shared idea library (PLAN-0060).
- A reference dish must be a card of the same plan, so another owner's dish name cannot enter a prompt.
- A failed request changes nothing.

## Acceptance criteria

- [x] A request puts its answer on screen first.
- [x] The panel asks the three questions and sends nothing until confirmed.
- [x] With "qualquer coisa", the model does not receive the pantry or the list, and the ranking gives them no advantage.
- [x] With "só o que tenho", the model is asked for few new ingredients.
- [x] Refusals weaken the pantry without any answer.
- [x] The person sees the wait, a request is never lost, and "nothing new" is said.
- [ ] The owner confirms that real suggestions follow the answers.

## Execution state

- **Current run delivery target:** The whole change.
- **Current checkpoint:** Implemented and validated with a fake provider and on the replica, in a draft pull request.
- **Last completed step:** See the log.
- **Exact next action:** Merged via #93 and deployed with `7333b25` (registry reconciled 2026-11-11); PLAN-0065 replaced its panel with the options sheet. The owner tries requests with the real model, which PLAN-0065 S1-AI also covers; then mark the plan completed.
- **Blockers:** None.
- **Partially modified areas:** None.
- **Documentation delivered:** This plan, the registry, the product spec, the planning domain document, the AI protocol, and the privacy document.
- **Validation performed:** See the log.
- **Known failures or limitations:** See the log.
- **Working tree state:** Clean after this commit.

## Progress log

### 2026-11-05 — Claude Code

- **Run delivery target:** From the owner's report to a draft pull request.
- **Changes:** `ShoppingPlanDeckPreference`, `ShoppingPlanDirection`, `ShoppingPlanRanker.PantryWeight`, `AddBatchAsync`, `RecipeAiRequestEnvelopes`, additive migration `AddShoppingPlanDeckPreference`, the panel in `ShoppingPlanDeckPage`, catalogs, OpenAPI and client, and `scripts/e2e/more-options-journey.mjs`.
- **Mistake during the work, repaired:** `dotnet ef migrations remove --force` ran with stale binaries while the build was broken, and it deleted the `AddPushSubscriptions` migration files from the working tree. Nothing was committed. Both databases were checked and were intact (33 migrations and both push tables), the files were restored from git, and the diff against `HEAD` was empty before the migration was generated again from a clean build.
- **Validation performed:** Backend unit and architecture tests, 6 new integration tests with a fake provider, and frontend tests. `more-options-journey.mjs` on the proxy replica: 10 of 10, including 320 px and recovery without AI. The panel was read at 390 px. Full gate results are in the pull request.
- **Not run:**
  - **The real model.** Nothing shows yet how well it follows "different", "similar", or a hint. The prompt changed, and evaluations G1-B and G5-A have not run either. The first real test is on the deployed site.
  - A screen reader and 200% zoom. `frontend-smoke` does not finish on this host (PLAN-0062 log).
- **Limitations:**
  - After the batch limit of five, the person is told there are no new options. Automatic batches count toward it.
  - With "qualquer coisa", dishes made before the request stay in the queue behind the new ones.
  - Saved recipes and library ideas are not filtered by the answers; only ranking affects them.
  - "Parecido" and "bem diferente" are requests to the model, not checked rules.
- **Next action:** See "Exact next action".

## Completion and handoff checklist

- [x] Tests and documentation are current.
- [x] `docs/plan-status.md` matches this plan.
- [ ] The owner has tried it with the real model.
