# ADR-0010 — Ingredient identity: curated catalog, exact-text embedding cache, OpenRouter Qwen3 Embedding, pgvector

- **Status:** Accepted
- **Date:** 2026-10-30
- **Owners:** Repository owner; Claude Code (implementation)
- **Decision scope:** Backend ingredient matching, AI Gateway embeddings, PostgreSQL extensions, development and deployment images
- **Related:** [PLAN-0055](../../plans/PLAN-0055-ingredient-identity.md), [ADR-0003](0003-primary-data-and-asynchronous-messaging.md), [ADR-0005](0005-ai-gateway-and-usage-governance.md), [`docs/ai/ingredient-identity.md`](../../ai/ingredient-identity.md)
- **Supersedes:** None
- **Superseded by:** None

## Context

Cocinaris matches recipe ingredients to pantry lots, and pantry lots to the shopping list and menu reservations, by comparing normalized names. "Peito de frango" in the pantry and "chicken breast" in a recipe never match. AI recipes, hand-written recipes, and pantry items use different words and languages for the same thing.

The owner wants fewer unnecessary AI calls for recipes and images. That needs a shared, language-independent identity for ingredients first. A recipe-idea library and image reuse by similarity both index by main ingredients, and neither works without it.

On 2026-10-30 the owner asked for:

- ingredient names standardized by what they really are;
- an external, very cheap model for this: OpenRouter `qwen/qwen3-embedding-8b`;
- a step before every embedding call that reuses the stored embedding of the **exact** same text, and only of the exact text;
- an environment that can be reproduced anywhere, with Docker Compose where it helps.

## Decision drivers

- Deterministic code owns every decision (AGENTS.md §4, §7). A model may suggest similarity but never decides alone.
- Optional AI must degrade gracefully: matching must keep working without a key, without the provider, and before a name is resolved.
- ADR-0003: PostgreSQL stays the source of truth; no second database without strong need.
- Cost and latency: a name is resolved once for every user, and a known name never waits for a provider.
- No silent rewrite of decided state.

## Considered options

### Option A: Tokenizer or self-hosted tokenization

A tokenizer splits text into frequent sub-word pieces. "frango", "chicken", and "pollo" share no tokens, so it adds nothing over text normalization. Rejected.

### Option B: Self-hosted multilingual embedding model (ONNX in .NET)

Free to run and private. It costs about 500 MB of RAM on a 3.8 GiB host and needs a model artifact and an update process. Kept as the fallback if provider terms or availability change. The `IEmbeddingProvider` port allows the swap.

### Option C: OpenRouter Qwen3 Embedding 8B behind the AI Gateway (chosen)

US$ 0.01 per million input tokens on 2026-10-30 (a name is 5–10 tokens). Multilingual, already reachable through the OpenRouter key the dish images use. Supports Matryoshka truncation.

### Option D: A separate search or NoSQL store (Elasticsearch, Typesense, MongoDB)

Adds operations, synchronization, and a second source of truth for a catalog of about 150 rows. PostgreSQL with `pg_trgm` and `pgvector` covers trigram and vector search. A derived search index fed by the outbox remains possible later and would never be authoritative. Rejected for now.

## Decision

1. **Curated catalog.** A project-owned catalog of canonical ingredients ships with the code as `apps/backend/src/Cocinaris.Modules.Ingredients/Catalog/ingredient-catalog.json`. It has permanent ids, slugs, categories, and names in English, Portuguese, and Spanish with common plurals and synonyms. Loading fails when one normalized name belongs to two ingredients. Ambiguous words (for example "pimenta" alone) stay out.
2. **Alias table.** `ingredients.aliases` maps a normalized name to a catalog ingredient. It holds curated names and names the job learned, with the method, score, and evaluation that decided each. The job never rewrites a decided alias. A curated name always wins.
3. **Matching keys.** Matching in the cooking proposal, shopping list, and menu reservations uses the catalog ingredient when the name has one, and the normalized name otherwise. Units are still compared separately. Without any alias, behavior is exactly what it was before.
4. **Non-blocking resolution.** A request never waits for a provider. Unknown names are queued (`ingredients.resolution_queue`), and a background job decides them with the cheapest step that can: curated name, then trigram similarity (`pg_trgm`, curated names only, threshold 0.75, margin 0.1), then embeddings.
5. **Exact-text embedding cache.** Before any embedding call, the job looks up `ingredients.text_embeddings` by `SHA-256(vector space, role, instruction version, exact text)`. Only the identical text in the identical setup reuses a vector. The text itself is not stored in that table.
6. **Provider.** `qwen/qwen3-embedding-8b` through OpenRouter, provider order pinned to `nebius, deepinfra`, `allow_fallbacks: false`, and `data_collection: "deny"`. The pin matters because the fp8 deployment of the model returns different vectors. Vectors are truncated to 1024 dimensions and renormalized. Queries carry the Qwen3 instruction prefix; catalog documents do not.
7. **Decision rule.** A name links to the nearest catalog ingredient only when cosine similarity is at least 0.80 and beats the next ingredient by 0.03. Otherwise it stays unresolved and keeps matching by text. Thresholds, the catalog version, and the vector space form an *evaluation* string. When it changes, unresolved names are re-checked; decided aliases are not.
8. **Governance.** `ingredient.embed.v1` is a registered AI Gateway operation. Every call reserves and settles in the usage ledger, charged to the owner whose name needed it. Calls no person triggered (catalog refresh, re-checks after a change) are charged to the system principal `SystemPrincipals.Maintenance`, an identity row whose issuer no sign-in can match.
9. **pgvector.** PostgreSQL gets the `vector` and `pg_trgm` extensions. Development and tests use `pgvector/pgvector:0.8.7-pg18-trixie`. The host installs the Debian package `postgresql-17-pgvector`. The deploy script creates both extensions as the superuser before migrations and refuses to deploy when pgvector is missing.

## Consequences

- Recipe ingredients and pantry lots match across languages and synonyms for curated names immediately. Other names match after the job resolves them, with no provider call when a curated or trigram match exists.
- A wrong learned alias could merge two different ingredients in a proposal or shopping line. Mitigations: high threshold and margin, units still compared, the cook confirms every proposal, and `scripts/ops/ingredient-identity-report.sh` lists learned aliases by score for review and deletion.
- Allergen and restriction decisions do **not** use ingredient identity yet. Using it for food safety requires curated allergen data and its own validation.
- Every environment now needs pgvector; the plain `postgres` image no longer migrates.
- Unresolved names keep their exact text (no owner) so a later catalog can re-check them. This is documented in the privacy record.
- Thresholds are initial values. They must be tuned with real names once the key is active, using the report and the evaluation fixture.

## References

- OpenRouter embeddings and provider routing: https://openrouter.ai/docs/api/reference/embeddings
- Qwen3 Embedding: https://huggingface.co/Qwen/Qwen3-Embedding-8B
- pgvector: https://github.com/pgvector/pgvector
- pg_trgm: https://www.postgresql.org/docs/current/pgtrgm.html
