#!/usr/bin/env node
/**
 * Drift check for recipe AI schemas: schemas must remain parseable and
 * positive fixtures must validate; schema file names are pinned.
 */
import { accessSync, constants, readFileSync } from "node:fs";
import { join } from "node:path";
import {
  SCHEMA_FILES,
  compileSchemas,
  createAjv,
  fixturesDir,
  packageRoot,
  schemasDir,
} from "./lib/validate-core.mjs";

const required = [
  join(schemasDir, SCHEMA_FILES.suggest),
  join(schemasDir, SCHEMA_FILES.expand),
  join(schemasDir, SCHEMA_FILES.revise),
  join(schemasDir, SCHEMA_FILES.planDeck),
  join(fixturesDir, "responses/positive/plan-deck.valid.json"),
  join(fixturesDir, "responses/positive/suggest-candidates.valid.json"),
  join(fixturesDir, "responses/positive/expand-selected.valid.json"),
  join(packageRoot, "package.json"),
  join(packageRoot, "test.mjs"),
];

for (const path of required) {
  accessSync(path, constants.R_OK);
}

const ajv = createAjv();
const { validateSuggest, validateExpand } = compileSchemas(ajv);
const suggest = JSON.parse(
  readFileSync(
    join(fixturesDir, "responses/positive/suggest-candidates.valid.json"),
    "utf8",
  ),
);
const expand = JSON.parse(
  readFileSync(
    join(fixturesDir, "responses/positive/expand-selected.valid.json"),
    "utf8",
  ),
);

if (!validateSuggest(suggest)) {
  console.error("drift: positive suggest fixture failed schema validation");
  process.exit(1);
}
if (!validateExpand(expand)) {
  console.error("drift: positive expand fixture failed schema validation");
  process.exit(1);
}

console.log("recipe AI contract drift check passed");
