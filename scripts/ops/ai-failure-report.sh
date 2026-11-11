#!/usr/bin/env bash
# Why recipe AI generations fail (PLAN-0039 R6). Reads counts, codes, rule labels, and models from
# ai.generation_attempts, ai.usage_ledger, and recipes.generation_sessions; never any recipe, pantry,
# or typed text, and no owner. Usage: scripts/ops/ai-failure-report.sh [days]   (default 30)
# On the owner host it runs psql as postgres; set PSQL to point elsewhere, for example a replica.
set -euo pipefail
DAYS="${1:-30}"
[[ "$DAYS" =~ ^[0-9]+$ ]] || { echo "days must be a number" >&2; exit 2; }
PSQL="${PSQL:-sudo -u postgres psql -d cocinaris}"
run() { $PSQL -X -q -P pager=off -v ON_ERROR_STOP=1 -c "$1"; }

echo "== Generations by result, last $DAYS days (one request = one correlation id)"
run "WITH requests AS (
  SELECT \"Operation\", \"CorrelationId\",
         bool_or(\"Outcome\" IN ('accepted','accepted_with_issues','quality_issues')) AS answered,
         bool_or(\"Outcome\" = 'accepted' AND \"Round\" = 1 AND \"Call\" = 1) AS first_call,
         count(*) AS calls, max(\"Round\") AS rounds
  FROM ai.generation_attempts
  WHERE \"OccurredAt\" > now() - make_interval(days => $DAYS)
  GROUP BY 1, 2)
SELECT \"Operation\" AS operation, count(*) AS requests,
       count(*) FILTER (WHERE first_call) AS first_call,
       count(*) FILTER (WHERE answered AND NOT first_call) AS recovered,
       count(*) FILTER (WHERE NOT answered) AS failed,
       round(avg(calls), 2) AS calls_per_request,
       count(*) FILTER (WHERE rounds = 2) AS used_fallback_round
FROM requests GROUP BY 1 ORDER BY 1;"

echo "== Calls by outcome and model"
run "SELECT \"Operation\" AS operation, coalesce(\"Model\", '-') AS model, \"Outcome\" AS outcome, count(*) AS calls,
       round(avg(\"LatencyMs\")) AS avg_ms
FROM ai.generation_attempts
WHERE \"OccurredAt\" > now() - make_interval(days => $DAYS)
GROUP BY 1, 2, 3 ORDER BY 1, 4 DESC;"

echo "== Rules broken by invalid answers (top 20)"
run "SELECT \"Operation\" AS operation, \"Rules\" AS rules, count(*) AS calls
FROM ai.generation_attempts
WHERE \"OccurredAt\" > now() - make_interval(days => $DAYS) AND \"Outcome\" = 'invalid_output'
GROUP BY 1, 2 ORDER BY 3 DESC LIMIT 20;"

echo "== Fields fitted instead of failing (R1)"
run "SELECT \"Operation\" AS operation, \"Rules\" AS fitted, count(*) AS calls
FROM ai.generation_attempts
WHERE \"OccurredAt\" > now() - make_interval(days => $DAYS) AND \"Outcome\" = 'accepted' AND \"Rules\" LIKE 'normalized:%'
GROUP BY 1, 2 ORDER BY 3 DESC LIMIT 20;"

echo "== Generations people saw fail, by reason"
run "SELECT \"ExecutionMode\" AS mode, coalesce(\"FailureReason\", '-') AS reason, count(*) AS sessions
FROM recipes.generation_sessions
WHERE \"CreatedAt\" > now() - make_interval(days => $DAYS) AND \"Status\" = 'Failed'
GROUP BY 1, 2 ORDER BY 3 DESC;"

echo "== Reservations released as abandoned (R4) and still open"
run "SELECT count(*) FILTER (WHERE \"QualityFlags\" = 'abandoned') AS abandoned,
       count(*) FILTER (WHERE \"Status\" = 'Reserved') AS open_now
FROM ai.usage_ledger WHERE \"CreatedAt\" > now() - make_interval(days => $DAYS);"
