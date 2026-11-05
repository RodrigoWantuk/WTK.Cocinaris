#!/usr/bin/env node
/**
 * Runs deterministic recipe AI contract validation (schemas + request fixtures).
 */
import { readFileSync } from "node:fs";
import { basename, join } from "node:path";
import {
  compileSchemas,
  createAjv,
  docsExamplesDir,
  fixturesDir,
  listJsonFiles,
  validateExpandSemantics,
  validPlanDeckComponents,
  validatePlanDeckSemantics,
  validateRequestFixture,
  validateSuggestSemantics,
} from "./lib/validate-core.mjs";

const ajv = createAjv();
const { validateSuggest, validateExpand, validatePlanDeck, validatePlanDeckV2 } =
  compileSchemas(ajv);

let failures = 0;

function fail(message) {
  failures += 1;
  console.error(`FAIL: ${message}`);
}

function pass(message) {
  console.log(`PASS: ${message}`);
}

for (const file of listJsonFiles(docsExamplesDir)) {
  const raw = readFileSync(file, "utf8");
  let data;
  try {
    data = JSON.parse(raw);
  } catch (error) {
    fail(`${basename(file)} JSON parse: ${error.message}`);
    continue;
  }
  const result = validateRequestFixture(data, basename(file));
  if (!result.ok) {
    fail(`${basename(file)} request structure: ${result.errors.join("; ")}`);
  } else {
    pass(`request fixture ${basename(file)}`);
  }
}

for (const file of listJsonFiles(join(fixturesDir, "responses/positive"))) {
  const data = JSON.parse(readFileSync(file, "utf8"));
  const name = basename(file);
  if (name.startsWith("suggest-")) {
    if (!validateSuggest(data)) {
      fail(`${name} schema: ${ajv.errorsText(validateSuggest.errors)}`);
    } else {
      const semantic = validateSuggestSemantics(data);
      if (!semantic.ok) fail(`${name} semantics: ${semantic.errors.join("; ")}`);
      else pass(`positive ${name}`);
    }
  } else if (name.startsWith("plan-deck-v2")) {
    if (!validatePlanDeckV2(data)) {
      fail(`${name} schema: ${ajv.errorsText(validatePlanDeckV2.errors)}`);
    } else if (!validatePlanDeckSemantics(data).ok) {
      fail(`${name} semantics`);
    } else if (validPlanDeckComponents(data).length !== data.components.length) {
      fail(`${name} dropped a valid component`);
    } else {
      pass(`positive ${name}`);
    }
  } else if (name.startsWith("plan-deck-")) {
    if (!validatePlanDeck(data)) {
      fail(`${name} schema: ${ajv.errorsText(validatePlanDeck.errors)}`);
    } else {
      const semantic = validatePlanDeckSemantics(data);
      if (!semantic.ok) fail(`${name} semantics: ${semantic.errors.join("; ")}`);
      else pass(`positive ${name}`);
    }
  } else if (name.startsWith("expand-")) {
    if (!validateExpand(data)) {
      fail(`${name} schema: ${ajv.errorsText(validateExpand.errors)}`);
    } else {
      const semantic = validateExpandSemantics(data);
      if (!semantic.ok) fail(`${name} semantics: ${semantic.errors.join("; ")}`);
      else pass(`positive ${name}`);
    }
  }
}

const negativeExpectations = {
  "suggest-wrong-count.json": { schemaFail: true },
  "suggest-extra-property.json": { schemaFail: true },
  "suggest-noncanonical-unit.json": { schemaFail: true },
  "suggest-injection-authority-field.json": { schemaFail: true },
  "expand-thumbnail-cache-hash.json": { schemaFail: true },
  "expand-too-many-stages.json": { schemaFail: true },
  "suggest-invented-equipment-id-format-ok-but-semantic.json": {
    schemaFail: false,
    semanticFail: true,
    request: join(docsExamplesDir, "08-suggest-cook-now-local-meal.request.json"),
  },
  "suggest-excessive-lead-minutes.json": {
    schemaFail: false,
    semanticFail: true,
    request: join(docsExamplesDir, "08-suggest-cook-now-local-meal.request.json"),
  },
  "suggest-assumption-as-additional.json": {
    schemaFail: false,
    semanticFail: true,
  },
  "expand-private-thumbnail.json": {
    schemaFail: false,
    semanticFail: true,
  },
  "expand-invented-visible-component.json": {
    schemaFail: false,
    semanticFail: true,
  },
  "plan-deck-v2-three-components.json": { schemaFail: true },
  "plan-deck-v2-component-unknown-user.json": { componentDropped: true },
  "plan-deck-v2-component-short-output.json": { componentDropped: true },
  "plan-deck-v2-component-wrong-name.json": { componentDropped: true },
  "plan-deck-five-candidates.json": { schemaFail: true },
  "plan-deck-seven-candidates.json": { schemaFail: true },
  "plan-deck-extra-property.json": { schemaFail: true },
  "plan-deck-duplicate-fingerprint.json": {
    schemaFail: false,
    semanticFail: true,
  },
  "plan-deck-wrong-meal.json": {
    schemaFail: false,
    semanticFail: true,
    request: "inline:planDeckLunchOnly",
  },
};

const inlineRequests = {
  planDeckLunchOnly: { planDeck: { mealTypes: ["lunch"] } },
};

for (const file of listJsonFiles(join(fixturesDir, "responses/negative"))) {
  const name = basename(file);
  const expectation = negativeExpectations[name];
  if (!expectation) {
    fail(`missing expectation for negative fixture ${name}`);
    continue;
  }
  const data = JSON.parse(readFileSync(file, "utf8"));
  const isSuggest = name.startsWith("suggest-");
  const isPlanDeck = name.startsWith("plan-deck-");
  const isPlanDeckV2 = name.startsWith("plan-deck-v2");
  const validate = isPlanDeckV2
    ? validatePlanDeckV2
    : isPlanDeck ? validatePlanDeck : isSuggest ? validateSuggest : validateExpand;
  const schemaOk = validate(data);
  if (expectation.schemaFail) {
    if (schemaOk) fail(`${name} expected schema failure`);
    else pass(`negative schema ${name}`);
    continue;
  }
  if (!schemaOk) {
    fail(`${name} unexpectedly failed schema: ${ajv.errorsText(validate.errors)}`);
    continue;
  }
  if (expectation.componentDropped) {
    // A bad component never fails the batch: the dishes stay valid, and only it is dropped.
    if (!validatePlanDeckSemantics(data).ok) fail(`${name} failed the batch`);
    else if (validPlanDeckComponents(data).length !== 0) fail(`${name} kept the component`);
    else pass(`negative component ${name}`);
    continue;
  }
  const request = expectation.request?.startsWith("inline:")
    ? inlineRequests[expectation.request.slice("inline:".length)]
    : expectation.request
      ? JSON.parse(readFileSync(expectation.request, "utf8"))
      : null;
  const semantic = isPlanDeck
    ? validatePlanDeckSemantics(data, request)
    : isSuggest
      ? validateSuggestSemantics(data, request)
      : validateExpandSemantics(data, request);
  if (expectation.semanticFail) {
    if (semantic.ok) fail(`${name} expected semantic failure`);
    else pass(`negative semantic ${name}`);
  } else if (!semantic.ok) {
    fail(`${name} unexpected semantic failure: ${semantic.errors.join("; ")}`);
  } else {
    pass(`negative ${name}`);
  }
}

if (failures > 0) {
  console.error(`\n${failures} failure(s)`);
  process.exit(1);
}

console.log("\nAll deterministic recipe AI contract checks passed.");
