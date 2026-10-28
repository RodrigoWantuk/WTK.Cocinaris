#!/usr/bin/env bash
set -euo pipefail

output_file="${1:-artifacts/dotnet-vulnerabilities.json}"
mkdir -p "$(dirname "$output_file")"

dotnet list apps/backend/Cocinaris.slnx package \
  --vulnerable \
  --include-transitive \
  --format json > "$output_file"

# The .NET JSON shape omits vulnerability arrays when none exist. Traverse package objects so the
# gate remains stable across projects/frameworks and fails on any advisory severity.
jq -e '[.. | objects | .vulnerabilities? // empty | .[]] | length == 0' "$output_file" >/dev/null
