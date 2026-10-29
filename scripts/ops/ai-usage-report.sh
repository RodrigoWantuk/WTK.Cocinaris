#!/usr/bin/env bash
# AI Gateway usage report for the owner host (PLAN-0040): this month's provider spend against the
# monthly ceiling, tokens in the rolling 24-hour window, calls by operation, model and outcome,
# provider latency p50/p95, repairs, and quality flags. Reads only the usage ledger; prints no prompts, outputs, users, or secrets.
#
# Usage: sudo scripts/ops/ai-usage-report.sh [ceiling-usd]
#   ceiling-usd defaults to 5 (Ai:Usage:MonthlyCostCeilingUsd); pass the configured value if changed.
set -euo pipefail

DATABASE="${COCINARIS_DATABASE:-cocinaris}"
CEILING="${1:-5}"

runuser -u postgres -- psql -d "$DATABASE" -v ON_ERROR_STOP=1 -v ceiling="$CEILING" -P pager=off <<'SQL'
\echo '== Spend this UTC month (closed calls at real cost, open ones at estimate) =='
SELECT round(sum(COALESCE("CostMicroUsd", CASE WHEN "Status" = 'Reserved' THEN "EstimatedCostMicroUsd" ELSE 0 END)) / 1e6, 4) AS spent_usd,
       :'ceiling'::numeric AS ceiling_usd,
       count(*) AS calls,
       count(*) FILTER (WHERE "CostMicroUsd" IS NULL AND "Status" <> 'Reserved') AS calls_without_cost_before_plan_0040
FROM ai.usage_ledger
WHERE "CreatedAt" >= date_trunc('month', now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC';

\echo '== Tokens in the last 24 hours, deployment and busiest owner (closed calls at reported tokens, open ones at estimate) =='
WITH window_rows AS (
    SELECT "OwnerUserId",
           CASE WHEN "Status" = 'Reserved' THEN "EstimatedTokens" ELSE COALESCE("PromptTokens", 0) + COALESCE("CompletionTokens", 0) END AS tokens
    FROM ai.usage_ledger
    WHERE "CreatedAt" > now() - interval '24 hours'
), per_owner AS (
    SELECT sum(tokens) AS tokens FROM window_rows GROUP BY "OwnerUserId"
)
SELECT (SELECT COALESCE(sum(tokens), 0) FROM window_rows) AS deployment_tokens,
       (SELECT COALESCE(max(tokens), 0) FROM per_owner) AS busiest_owner_tokens;

\echo '== Calls by operation, model and outcome (last 30 days) =='
SELECT "Operation", COALESCE("Model", '-') AS model, "Status", count(*) AS calls,
       round(avg("Attempts"), 2) AS avg_attempts,
       count(*) FILTER (WHERE "Attempts" > 1) AS repaired_or_fallback,
       round(sum(COALESCE("CostMicroUsd", 0)) / 1e6, 4) AS cost_usd
FROM ai.usage_ledger
WHERE "CreatedAt" >= now() - interval '30 days'
GROUP BY 1, 2, 3 ORDER BY 1, 2, 3;

\echo '== Provider latency in ms, all attempts summed (last 30 days, calls that reached the provider) =='
SELECT "Operation", COALESCE("Model", '-') AS model, count(*) AS samples,
       percentile_cont(0.5) WITHIN GROUP (ORDER BY "LatencyMs")::int AS p50_ms,
       percentile_cont(0.95) WITHIN GROUP (ORDER BY "LatencyMs")::int AS p95_ms,
       max("LatencyMs") AS max_ms
FROM ai.usage_ledger
WHERE "CreatedAt" >= now() - interval '30 days' AND "LatencyMs" IS NOT NULL
GROUP BY 1, 2 ORDER BY 1, 2;

\echo '== Answers accepted with quality flags (last 30 days) =='
SELECT "Operation", "QualityFlags", count(*) AS calls
FROM ai.usage_ledger
WHERE "CreatedAt" >= now() - interval '30 days' AND "QualityFlags" IS NOT NULL AND "QualityFlags" <> ''
GROUP BY 1, 2 ORDER BY 1, 2;
SQL
