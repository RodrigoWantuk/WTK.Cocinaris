# Ingredient Identity and Embeddings

- **Plan:** [PLAN-0055](../plans/PLAN-0055-ingredient-identity.md)
- **Decision:** [ADR-0010](../architecture/decisions/0010-ingredient-identity-catalog-embeddings-pgvector.md)
- **AI Gateway operation:** `ingredient.embed.v1`

## Purpose

Give every ingredient name a language-independent identity, so that:

- a recipe asking for "chicken breast" proposes the "Peito de frango" lot in the pantry;
- the shopping list counts "Cebola" in the pantry against "Onions" in a recipe;
- menu reservations reserve the same lots the cooking proposal would use;
- later work (a shared recipe-idea library and dish-image reuse by similarity) can index by canonical ingredients instead of free text.

## Components

| Component | Where | Role |
| --- | --- | --- |
| Curated catalog | `Cocinaris.Modules.Ingredients/Catalog/ingredient-catalog.json` | About 150 canonical ingredients with permanent ids, slugs, categories, and names in `en`, `pt`, `es` |
| Matching keys | `IngredientMatchKeys` | `ingredient:<slug>` for resolved names, normalized text otherwise |
| Matching contract | `IIngredientMatching` | Used by Recipes and Planning; never waits for a provider |
| Resolution job | `IngredientResolutionService`, `IngredientResolutionWorker` | Decides queued names in the background |
| Embedding provider | `OpenRouterEmbeddingProvider` (`Cocinaris.Modules.Ai.Embeddings`) | One batch per call, no retries |
| Store | `PostgreSqlIngredientStore`, schema `ingredients` | Catalog copy, aliases, queue, embedding cache, catalog vectors |

## Name normalization

`IngredientText.Normalize`: trim, lower case, remove accents, collapse spaces, at most 120 characters. The cooking proposal, shopping list, reservations, and alias table all use it.

## Request path (synchronous, no provider)

1. Normalize every pantry product name and recipe ingredient name involved.
2. A curated catalog name resolves from memory.
3. Otherwise a learned alias resolves from `ingredients.aliases`.
4. Otherwise the name keeps its normalized text as its key and is queued with the exact text first seen and the owner. A name already queued or unresolved is not queued again.

## Resolution job (background)

On each step the job first syncs the database copy of the catalog when its version or the *evaluation* changed. It then claims up to 32 due names (`FOR UPDATE SKIP LOCKED`, lease 120 s) and runs these steps in order:

1. **Curated name.** The catalog may have grown since the name was queued.
2. **Trigram.** `pg_trgm` similarity to curated names only. Accept when it is at least 0.75 and beats a different ingredient by 0.10. Measured on 2026-10-30: "azeite extra virgem" scores 0.77 against "azeite extravirgem", while "alho poro" (leek) scores only 0.5 against "alho" (garlic).
3. **Embedding.**
   - Make sure every catalog ingredient has a vector in the current space. The document text is all its names in all languages, embedded without an instruction.
   - For each remaining name, compute the cache key `SHA-256(space \n "query" \n instructionVersion \n exactText)`.
   - Reuse a cached vector for that key. Only the **exact** same text reuses a vector (owner decision 2026-10-30).
   - Embed the misses in one call per charged owner. Query input: `Instruct: <QueryInstruction>\nQuery: <exact text>`.
   - Compare with catalog vectors by cosine similarity (`pgvector`, `<=>`).
   - Accept when the similarity is at least 0.80 and beats the second ingredient by 0.03.
4. **Otherwise unresolved.** The name keeps matching by text. Its best candidate and score are kept for tuning.

A decided name is never rewritten by the job. A curated name always wins over a learned one.

### Evaluation and re-checks

The evaluation string is the catalog version, vector space, instruction version, and all four thresholds. When it changes, unresolved names go back to the queue. Their cached vectors make the re-check free, unless the space itself changed.

## Provider and model policy

| Setting | Value | Configuration |
| --- | --- | --- |
| Model | `qwen/qwen3-embedding-8b` | `Ai:Embeddings:Model` |
| Providers | `nebius`, `deepinfra`; no fallback outside the list | `Ai:Embeddings:ProviderOrder` |
| Data collection | `deny` (OpenRouter provider routing) | fixed in code |
| Stored dimensions | 1024 (first 1024 of 4096, renormalized) | `Ai:Embeddings:Dimensions` |
| Price used when the provider reports none | US$ 0.01 per million input tokens | `Ai:Embeddings:PriceUsdPerMillionTokens` |
| Timeout | 20 s | `Ai:Embeddings:TimeoutSeconds` |
| Enabled | false by default; also needs `Ai:Providers:OpenRouter:ApiKey` | `Ai:Embeddings:Enabled` |

**Why the providers are pinned.** On 2026-10-30, SiliconFlow served the same model in fp8 at four times the price. fp8 vectors differ slightly from the full-precision ones, and mixing them in one space would make similarities inconsistent.

**Why 1024 dimensions.** OpenRouter lists no `dimensions` parameter for this model. Qwen3 Embedding supports Matryoshka truncation, so the adapter keeps the first 1024 values. pgvector cannot index more than 2000 dimensions.

## Bounds and validation

- At most 32 texts per call. Each text is at most 120 characters, and a request matches at most 1,000 distinct names.
- The response must contain exactly one vector per input with distinct indexes, or it is rejected whole. Each vector must be finite, at least the stored dimension long, and non-zero.
- The response body is capped at 8 MB.
- The provider returns only vectors. Thresholds, margins, and units are decided by code.

## Usage, cost, and quotas

- Each call reserves 1 usage credit and its token estimate in the usage ledger (`ai.usage_ledger`), then settles at the reported cost. If no cost is reported, the cost is computed from tokens × price.
- Calls count against the monthly AI ceiling and the rolling token windows.
- A call is charged to the owner whose name needed it. Catalog vectors and re-checks after a change, when no owner is attached, are charged to `SystemPrincipals.Maintenance` (`00000000-0000-0000-0000-00000000c0c1`).
- Budget refusals defer the names for an hour and do not use up an attempt. Transient failures retry after 10 and 20 minutes; after three attempts the name is unresolved.
- Expected cost: the first catalog embedding is about 5 calls of 32 documents (well under US$ 0.001). After that, each new name costs about US$ 0.0000001.

## Degraded behavior

| Situation | Behavior |
| --- | --- |
| No key or `Ai:Embeddings:Enabled=false` | Curated and trigram matches work; other names are unresolved (`embedding_unavailable`) and re-checked automatically when embeddings are enabled |
| Provider down or rate limited | Names deferred and retried; matching by text meanwhile |
| Budget exhausted | Names deferred one hour |
| Job not running | Curated names still match from memory; others match by text |

## Privacy

- The queue keeps the owner only while a name is pending. Deciding a name clears it.
- An unresolved name keeps its exact text so a later catalog can re-check it.
- Aliases and the embedding cache hold no owner. The cache also holds no text, only its hash and vector.
- Names sent to the provider are single ingredient names, never recipes, notes, allergies, or profiles.
- The privacy record lists these tables under [Ingredient identity](../security/privacy-and-data-protection.md#ingredient-identity).

## Food-safety boundary

Ingredient identity does not drive allergy or restriction decisions in this release. A wrong alias could at most propose a wrong pantry lot, which the cook sees and confirms, or merge two shopping lines. Allergen data in the catalog needs its own curated source and validation plan.

## Evaluation

`apps/backend/tests/Cocinaris.UnitTests/IngredientIdentityTests.cs` and `apps/backend/tests/Cocinaris.IntegrationTests/IngredientIdentityApiTests.cs` cover the deterministic rules:

- catalog ambiguity;
- matching keys;
- the decision rule;
- cache keys;
- the adapter's routing, reordering, truncation, and rejection;
- cross-language proposals and shopping;
- the job end to end on PostgreSQL with pgvector.

**Live-model evaluation** runs with `IngredientLiveEvaluationTests`. The test uses a throwaway Testcontainers database and the real model, and it never touches the deployment database. It runs only when the key is supplied:

```sh
cd apps/backend
COCINARIS_EVAL_OPENROUTER_API_KEY="$(sudo sed -n 's/^Ai__Providers__OpenRouter__ApiKey=//p' /etc/wtk-cocinaris/api.env)" \
  dotnet test tests/Cocinaris.IntegrationTests --filter IngredientLiveEvaluation --logger "console;verbosity=detailed"
```

It prints one line per fixture name (verdict, linked ingredient, method and score, or nearest candidate and score) and a summary. It fails only on a **wrong link**, because a wrong link could merge two foods. A missed match is reported for tuning but is not a failure. The cost is well under US$ 0.001.

The same check is possible on live data: The fixture `apps/backend/tests/fixtures/ingredient-identity-eval.json` lists names with their expected ingredient, or `null` when the correct outcome is unresolved. After enabling the key:

1. Add the fixture names through the pantry, or let real use fill the queue.
2. Run `scripts/ops/ingredient-identity-report.sh`.
3. Compare learned aliases and unresolved best scores with the fixture.
4. Tune `Ingredients:Resolution:EmbeddingThreshold` and `EmbeddingMargin` only with that evidence. A change re-checks unresolved names and never rewrites decided ones.

## Operations

- **Report:** `sudo scripts/ops/ingredient-identity-report.sh`. It shows aliases by method, the queue by state and reason, the learned aliases with the lowest scores, and unresolved best scores.
- **Removing a wrong learned alias:** `DELETE FROM ingredients.aliases WHERE "NormalizedText" = '<name>' AND "Method" <> 'Catalog';`. The name is queued again the next time it is read. Fix the catalog first if the name belongs in it.
- **Catalog change:** edit the JSON (ids are permanent; never reuse one), deploy, and the job syncs on its next step.
