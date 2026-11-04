# PLAN-0060: AI Recipe Direction, Suggestion History, and AI Revision

- **Status:** Validating
- **Type:** Implementation
- **Priority:** High (owner requests)
- **Owner:** Claude Code (claimed 2026-11-04)
- **Created:** 2026-11-04
- **Last updated:** 2026-11-04
- **Branch:** `agent/plan-0060-ai-direction-history-revision`
- **Pull request:** Draft, opened with this delivery
- **Related documents:** [`docs/ai/recipe-artifact-protocol.md`](../ai/recipe-artifact-protocol.md), [`docs/security/privacy-and-data-protection.md`](../security/privacy-and-data-protection.md), [PLAN-0056](PLAN-0056-owner-feedback-2026-10-30.md), [ADR-0011](../architecture/decisions/0011-shared-recipe-idea-library.md)

## Owner requests (2026-11-04)

1. Choose between more adaptation to what is at home and a bigger shopping list that stays more faithful to the original recipe.
2. "A função de pedir para gerar receita ainda falha a primeira vez e funciona na segunda."
3. A short text field (about 300 characters) for a wish, a recipe, or an ingredient. It must be treated as unsafe and proof against prompt injection, and it goes to the AI as a hint only, not as a hard rule.
4. A place to see the history of suggested recipes and continue or request one of them later.
5. A short description of each of the three candidates, naming the main ingredients and making the person want it.
6. Revise a recipe with AI: a short text (about 300 characters) says what to review, and the AI generates a new version.

## Root cause of request 2

The usage ledger (`ai.usage_ledger`, read-only) showed the failure on 2026-11-04 09:48: three attempts, status `Released`, flag `invalid:candidates.summary has words max`. The next request worked with one attempt. The suggestion protocol rejected any summary over 18 words, and the model broke that rule on all three attempts, so the first request failed by chance and the second usually passed. Earlier failures (2026-10-29/30) have the same shape, but they predate the flags, so their rule is not recorded.

**Fix:** length no longer rejects an answer. A summary is fitted to 320 characters by deterministic code, at a sentence end when possible. This is also what makes room for request 5.

**Not claimed:** other rules can still fail after every repair (for example the 36-word thumbnail description during expansion). The ledger flags will show any remaining rule. There is one flagged failure as evidence, so this is the observed cause, not proof that no other cause exists.

## Decisions

- **Hint defense in layers.**
  1. Length is checked on the raw text (at most 300 characters).
  2. `RecipeUntrustedText` normalizes it (NFKC) and removes control, invisible, bidirectional, and structural characters (`<>{}[]` and others).
  3. It travels only as a JSON string in `suggestionGuidance.personHint.text`, next to a fixed treatment rule; the system prompt names the field as data.
  4. No validator depends on it, so it cannot become a rule, and every existing validator still applies to the output.
  5. New rule: names, summaries, and recipe text with a link or markup are rejected.
  6. The raw hint is not sent to the expansion, which only sees the selected candidate's summary.
  7. Nothing shaped by a hint reaches the shared idea library, and hinted requests are not served from it.
  8. It is rendered as plain text, never logged, and never written to the usage ledger.
- **Why the library matters here.** Without layer 7, one person's hint, or an injection through it, could reach other people through ADR-0011's shared library. `PrivateDirection` is copied to any session reopened from a candidate, and it survives removing the hint.
- **AI revision is a proposal.** AGENTS.md §4 forbids AI from directly mutating authoritative state. The AI output is stored as a proposal after validation; only the owner's acceptance appends it as a new immutable revision (origin `AiRevised`), with optimistic concurrency on the base revision. Generation holds and settles usage like the other operations, atomically with the proposal.
- **Ingredient approach:** two closed codes, `adapt_to_pantry` and `stay_faithful`, with no choice meaning today's balance. The protocol limit on ingredients to buy rises from 5 to 12 so a faithful dish can list what is missing. Under `adapt_to_pantry`, more than 2 required purchases asks for one repair but never discards the answer.
- **History reopens through a new session,** the same path as saved candidates, so an old request's two-hour expiry does not matter and the candidate meets today's pantry. Reopened sessions record their source, which links the candidate to its recipe and keeps them out of the list.
- **Removing a history entry** clears the hint. This is an addition the owner did not ask for: the hint is free text that can be personal, and AGENTS.md §8 asks for deletion to be preserved.
- **No shared-library use for AI revisions:** they never call capture.

## Scope delivered

- **Backend:** `RecipeUntrustedText`, guidance `hint` and `adaptation`, `recipe.revise.v1` (registered operation, schema, envelope, validator), `RecipeRevisionProposal`, `RecipeAiInvocation` (repair loop extracted from the cook-now service, now shared), history and revision endpoints, migration `AddAiDirectionHistoryAndRevisionProposals`.
- **API:**
  - `GET /api/v1/recipes/suggestion-history`
  - `POST /api/v1/recipes/suggestion-history/{sessionId}/recipe`
  - `DELETE /api/v1/recipes/suggestion-history/{sessionId}`
  - `POST /api/v1/recipes/{recipeId}/revision-proposals`
  - `GET /api/v1/recipes/{recipeId}/revision-proposals/{proposalId}`
  - `POST /api/v1/recipes/{recipeId}/revision-proposals/{proposalId}/accept`
  - `DELETE /api/v1/recipes/{recipeId}/revision-proposals/{proposalId}`
- **Frontend:**
  - Suggestion page: wish field with counter and two ingredient-approach chips.
  - `/app/receitas/sugestoes`: "Sugestões anteriores", linked from "Cozinhar", "Receitas", and the suggestion page.
  - `/app/receitas/:recipeId/revisar`: "Revisar com IA", in the recipe's "Mais" menu; shows what was asked, the ingredients that entered and left, and the full new version before saving.
  - Copy in pt-BR, en, and es.
- **Migration:** additive columns on `recipes.generation_sessions`, new table `recipes.revision_proposals`, and two widened check constraints on `recipes.recipe_revisions`. No existing row changes. Rollback: `Down` drops them; it would fail while `AiRevised` revisions exist, so repair forward once any was accepted.

## Acceptance criteria

- [x] Adapt or stay faithful is selectable and reaches the AI as a closed rule, on suggestion and on expansion.
- [x] A long summary no longer fails a request (regression test from the ledger's failure).
- [x] A hint of at most 300 characters reaches the AI only as untrusted data, as a wish; injection attempts stay data, and output that follows them is rejected by the validators that can detect it.
- [x] The history lists past requests per owner; any candidate can still be asked for, including after expiry; a repeated key returns the same recipe.
- [x] Candidates ask for two inviting sentences that name the main ingredients.
- [x] AI revision proposes without changing the recipe, and acceptance creates a new revision while older ones stay; stale proposals are refused.
- [ ] The owner confirms on the host, after deployment, that the first request works and the descriptions read well.

## Execution state

- **Current run delivery target:** all six requests, end to end.
- **Current checkpoint:** implemented and validated as below; awaiting deployment and the owner's check.
- **Validation (lean):**
  - **Backend unit:** `RecipeAiDirection`, `RecipeCookNow`, `RecipeSuggestionGuidance`, `RecipeProtocol`, `RecipeAiLanguage`, `RecipeLibrary` filters.
  - **Backend integration (Testcontainers):** new history and revision tests, plus saved candidates, guidance, gateway concurrency, idea library, menu suggestions, and library.
  - **Contracts:** recipe AI contract test and drift check; OpenAPI regenerated (additions only); client regenerated and synced.
  - **Frontend:** typecheck, ESLint, Prettier; `src/features/recipes`, `src/features/planning`, `src/app/i18n`, shell navigation.
- **Not run:**
  - **Real-model evaluation:** the provider key is not read by agents, so the hint, the new summaries, and `recipe.revise.v1` have only run against the fake provider. AGENTS.md §9 expects an evaluation for each model-assisted operation; this remains open, including real injection attempts.
  - Browser walkthrough and phone captures of the new pages.
  - Full suites.
- **Known limits:**
  - Restrictions reach the model and take precedence over the hint by rule, but there is no deterministic allergen check of the output. That is the same as for expansions today.
  - Injection defense cannot detect every wrong dish a hint might steer the model to: output constraints bound what can happen, not what the model prefers.
  - An accepted AI revision with a new visual descriptor generates a new dish image, at the usual image cost.
- **Exact next action:** The owner runs the deployment from this branch (`! sudo scripts/deploy/deploy-linux-host.sh --ref agent/plan-0060-ai-direction-history-revision`), tries the suggestion and revision flows, and merges. Then run a real-model evaluation of the hint and `recipe.revise.v1`.
- **Blockers:** None.

## Progress log

- 2026-11-04: Read the ledger, found the summary word cap behind request 2, and delivered all six requests in one run on a branch from `main`. The uncommitted PLAN-0046 work in the main checkout was left untouched.
