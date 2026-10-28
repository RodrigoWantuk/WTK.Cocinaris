#!/usr/bin/env bash
set -euo pipefail

output_file="${1:-artifacts/cocinaris-migrations.sql}"
mkdir -p "$(dirname "$output_file")"
output_directory="$(cd "$(dirname "$output_file")" && pwd)"
output_file="$output_directory/$(basename "$output_file")"

(
  cd apps/backend
  dotnet tool restore
  dotnet ef migrations script \
    --idempotent \
    --project src/Cocinaris.Infrastructure/Cocinaris.Infrastructure.csproj \
    --startup-project src/Cocinaris.Api/Cocinaris.Api.csproj \
    --configuration Release \
    --no-build \
    --output "$output_file"
)

test -s "$output_file"
