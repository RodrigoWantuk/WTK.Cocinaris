#!/usr/bin/env bash
# Ingredient identity report for the owner host (PLAN-0055): aliases by method, the resolution
# queue by state and reason, the learned aliases with the lowest scores (review these for wrong
# merges), and the best scores of unresolved names (for threshold tuning). Prints catalog slugs and
# normalized ingredient names only; never owners, vectors, or secrets.
#
# Usage: sudo scripts/ops/ingredient-identity-report.sh [rows]
#   rows defaults to 30 for the two detail lists.
# Remove a wrong learned alias (it is queued again on its next read):
#   DELETE FROM ingredients.aliases WHERE "NormalizedText" = '<name>' AND "Method" <> 'Catalog';
set -euo pipefail

DATABASE="${COCINARIS_DATABASE:-cocinaris}"
ROWS="${1:-30}"
[[ "$ROWS" =~ ^[0-9]+$ ]] || { echo "rows must be a number" >&2; exit 2; }

runuser -u postgres -- psql -d "$DATABASE" -v ON_ERROR_STOP=1 -v rows="$ROWS" -P pager=off <<'SQL'
\echo '== Catalog sync =='
SELECT "CatalogVersion", "Evaluation", "UpdatedAt" FROM ingredients.catalog_state;

\echo '== Aliases by method =='
SELECT "Method", count(*) AS aliases, round(avg("Score")::numeric, 3) AS avg_score, round(min("Score")::numeric, 3) AS min_score
FROM ingredients.aliases GROUP BY 1 ORDER BY 1;

\echo '== Resolution queue by state and reason =='
SELECT "State", COALESCE("Reason", '-') AS reason, count(*) AS names
FROM ingredients.resolution_queue GROUP BY 1, 2 ORDER BY 1, 2;

\echo '== Learned aliases with the lowest scores (review for wrong merges) =='
SELECT a."NormalizedText" AS name, c."Slug" AS ingredient, a."Method", round(a."Score"::numeric, 3) AS score, a."CreatedAt"
FROM ingredients.aliases a JOIN ingredients.catalog_items c ON c."Id" = a."IngredientId"
WHERE a."Method" <> 'Catalog'
ORDER BY a."Score", a."CreatedAt" DESC
LIMIT :rows;

\echo '== Unresolved names closest to an ingredient (threshold tuning) =='
SELECT q."NormalizedText" AS name, c."Slug" AS nearest, round(q."BestScore"::numeric, 3) AS best_score, q."Reason"
FROM ingredients.resolution_queue q LEFT JOIN ingredients.catalog_items c ON c."Id" = q."BestIngredientId"
WHERE q."State" = 'Unresolved'
ORDER BY q."BestScore" DESC NULLS LAST
LIMIT :rows;

\echo '== Embedding cache and catalog vectors by space =='
SELECT 'text_embeddings' AS store, "Space", count(*) AS vectors FROM ingredients.text_embeddings GROUP BY 1, 2
UNION ALL
SELECT 'catalog_vectors', "Space", count(*) FROM ingredients.catalog_vectors GROUP BY 1, 2
ORDER BY 1, 2;
SQL
