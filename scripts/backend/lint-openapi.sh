#!/usr/bin/env bash
set -euo pipefail

# Pin the linter invocation so every workstation and CI run evaluates the checked-in OpenAPI 3.1
# artifact with the same rules without committing generated Node dependencies to this repository.
npx --yes @redocly/cli@2.41.2 lint --config packages/contracts/openapi/redocly.yaml packages/contracts/openapi/cocinaris-v1.json
