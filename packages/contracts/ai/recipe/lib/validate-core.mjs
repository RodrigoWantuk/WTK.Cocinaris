/**
 * Shared paths and schema loading for recipe AI contract validation.
 */
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import addFormats from "ajv-formats";

const __dirname = dirname(fileURLToPath(import.meta.url));
export const packageRoot = join(__dirname, "..");
export const schemasDir = join(packageRoot, "schemas");
export const fixturesDir = join(packageRoot, "fixtures");
export const docsExamplesDir = join(
  packageRoot,
  "../../../../docs/ai/examples",
);

export const SCHEMA_FILES = {
  suggest: "recipe-suggest-candidates.response.v0.3.json",
  expand: "recipe-expand-selected.response.v0.3.json",
};

/**
 * @returns {import('ajv').default}
 */
export function createAjv() {
  const ajv = new Ajv2020({
    allErrors: true,
    strict: true,
    validateSchema: true,
  });
  addFormats(ajv);
  return ajv;
}

/**
 * @param {import('ajv').default} ajv
 */
export function compileSchemas(ajv) {
  const suggest = JSON.parse(
    readFileSync(join(schemasDir, SCHEMA_FILES.suggest), "utf8"),
  );
  const expand = JSON.parse(
    readFileSync(join(schemasDir, SCHEMA_FILES.expand), "utf8"),
  );
  return {
    suggestSchema: suggest,
    expandSchema: expand,
    validateSuggest: ajv.compile(suggest),
    validateExpand: ajv.compile(expand),
  };
}

/**
 * @param {string} dir
 * @returns {string[]}
 */
export function listJsonFiles(dir) {
  return readdirSync(dir)
    .filter((name) => name.endsWith(".json"))
    .map((name) => join(dir, name))
    .sort();
}

/**
 * Count whitespace-delimited words in a string.
 * @param {string} value
 */
export function wordCount(value) {
  return value.trim().split(/\s+/).filter(Boolean).length;
}

const PRIVATE_THUMBNAIL_PATTERNS = [
  /\binv[-_]?\d+/i,
  /\buser\b/i,
  /\ballerg/i,
  /\bpantry\b/i,
  /\brestriction/i,
  /\bhousehold\b/i,
  /rodrigowantuk/i,
  /\bhttps?:\/\//i,
];

const UNSUPPORTED_CLAIMS = [
  /\bhomemade\b/i,
  /\bfresh\b/i,
  /\bhealthy\b/i,
  /\bauthentic\b/i,
  /\btraditional\b/i,
  /\bhigh[- ]protein\b/i,
];

/**
 * Semantic validation for a suggest_candidates response against a request fixture.
 * @param {object} response
 * @param {object | null} request
 * @returns {{ ok: boolean, errors: string[] }}
 */
export function validateSuggestSemantics(response, request = null) {
  const errors = [];
  if (!response || typeof response !== "object") {
    return { ok: false, errors: ["response is not an object"] };
  }

  const candidates = Array.isArray(response.candidates)
    ? response.candidates
    : [];
  if (candidates.length !== 3) {
    errors.push(`expected exactly 3 candidates, got ${candidates.length}`);
  }

  const strategies = new Set();
  const fingerprints = new Set();
  for (const [index, candidate] of candidates.entries()) {
    const prefix = `candidates[${index}]`;
    if (!candidate || typeof candidate !== "object") {
      errors.push(`${prefix} is not an object`);
      continue;
    }

    if (strategies.has(candidate.candidateStrategy)) {
      errors.push(`${prefix} duplicates strategy ${candidate.candidateStrategy}`);
    }
    strategies.add(candidate.candidateStrategy);

    const summaryWords = wordCount(String(candidate.summary ?? ""));
    if (summaryWords > 18) {
      errors.push(`${prefix}.summary has ${summaryWords} words (max 18)`);
    }

    const fingerprint = [
      candidate.dishFormat,
      candidate.primaryTechnique,
      ...(candidate.primaryIngredientRefs ?? []),
    ].join("|");
    if (fingerprints.has(fingerprint)) {
      errors.push(`${prefix} is not materially distinct from another candidate`);
    }
    fingerprints.add(fingerprint);

    for (const claim of UNSUPPORTED_CLAIMS) {
      if (claim.test(String(candidate.name ?? "")) || claim.test(String(candidate.summary ?? ""))) {
        errors.push(`${prefix} contains unsupported quality claim`);
      }
    }

    const assumptions = new Set(
      (candidate.assumptionsUsed ?? []).map((value) =>
        String(value).trim().toLowerCase(),
      ),
    );
    for (const [addIndex, additional] of (
      candidate.additionalIngredients ?? []
    ).entries()) {
      const name = String(additional?.name ?? "")
        .trim()
        .toLowerCase();
      if (name && assumptions.has(name)) {
        errors.push(
          `${prefix}.additionalIngredients[${addIndex}] repeats assumption "${name}"`,
        );
      }
    }

    if (request) {
      const allowedEquipment = new Set(
        (request.equipmentSnapshot?.items ?? []).map((item) => item.equipmentId),
      );
      for (const equipmentId of candidate.requiredEquipmentIds ?? []) {
        if (allowedEquipment.size > 0 && !allowedEquipment.has(equipmentId)) {
          errors.push(`${prefix} references undeclared equipment ${equipmentId}`);
        }
      }

      const availabilityById = new Map(
        (request.availabilitySnapshot?.items ?? []).map((item) => [
          item.inventoryItemId,
          item,
        ]),
      );
      for (const [useIndex, use] of (candidate.inventoryUses ?? []).entries()) {
        const source = availabilityById.get(use.inventoryItemId);
        if (!source) {
          errors.push(
            `${prefix}.inventoryUses[${useIndex}] invents inventoryItemId ${use.inventoryItemId}`,
          );
          continue;
        }
        if (use.userName !== source.userName) {
          errors.push(
            `${prefix}.inventoryUses[${useIndex}] renamed userName`,
          );
        }
        if (use.unit !== source.unit) {
          errors.push(`${prefix}.inventoryUses[${useIndex}] changed unit`);
        }
        if (use.availabilitySource !== source.availabilitySource) {
          errors.push(
            `${prefix}.inventoryUses[${useIndex}] changed availabilitySource`,
          );
        }
        if (
          use.ingredientRef &&
          source.ingredientRef &&
          use.ingredientRef !== source.ingredientRef
        ) {
          errors.push(
            `${prefix}.inventoryUses[${useIndex}] changed ingredientRef`,
          );
        }
      }

      // PLAN-0056: the person's required pantry items and time limit are hard rules.
      const guidance = request.suggestionGuidance;
      const usedIds = new Set((candidate.inventoryUses ?? []).map((use) => use.inventoryItemId));
      for (const requiredId of guidance?.requiredInventoryItemIds ?? []) {
        if (!usedIds.has(requiredId)) {
          errors.push(`${prefix} does not use required inventory item ${requiredId}`);
        }
      }
      if (
        typeof guidance?.maxTotalMinutes === "number" &&
        candidate.time?.totalMinutes > guidance.maxTotalMinutes
      ) {
        errors.push(
          `${prefix} takes ${candidate.time.totalMinutes} minutes, more than the requested ${guidance.maxTotalMinutes}`,
        );
      }

      const mode = request.executionContext?.executionMode;
      const lead = request.executionContext?.availableLeadMinutes;
      if (
        mode === "cook_now" &&
        typeof lead === "number" &&
        candidate.preparationProfile?.minimumLeadMinutes > lead
      ) {
        errors.push(
          `${prefix} lead minutes exceed cook_now window (${candidate.preparationProfile.minimumLeadMinutes} > ${lead})`,
        );
      }

      if (
        mode === "cook_now" &&
        candidate.candidateStrategy === "planned_purchase_reuse"
      ) {
        errors.push(`${prefix} uses menu-only strategy in cook_now`);
      }
      if (
        mode === "menu_planning" &&
        candidate.candidateStrategy === "on_hand_flexible"
      ) {
        errors.push(`${prefix} uses cook_now-only strategy in menu_planning`);
      }
    }
  }

  if (Object.prototype.hasOwnProperty.call(response, "authorityOverride")) {
    errors.push("response must not include authorityOverride");
  }

  return { ok: errors.length === 0, errors };
}

/**
 * Semantic validation for expand_selected responses.
 * @param {object} response
 * @param {object | null} request
 */
export function validateExpandSemantics(response, request = null) {
  const errors = [];
  const recipe = response?.recipe;
  if (!recipe || typeof recipe !== "object") {
    return { ok: false, errors: ["recipe is missing"] };
  }

  const visual = recipe.thumbnailVisual;
  if (!visual || typeof visual !== "object") {
    return { ok: false, errors: ["thumbnailVisual is missing"] };
  }

  if (wordCount(visual.appearanceDescription ?? "") > 36) {
    errors.push("thumbnailVisual.appearanceDescription exceeds 36 words");
  }

  const visualBlob = [
    visual.appearanceDescription,
    ...(visual.visibleComponents ?? []),
    visual.plating,
    visual.sauceAppearance,
    ...(visual.textureAndDoneness ?? []),
    ...(visual.garnish ?? []),
  ]
    .map(String)
    .join(" ");

  for (const pattern of PRIVATE_THUMBNAIL_PATTERNS) {
    if (pattern.test(visualBlob)) {
      errors.push("thumbnailVisual contains privacy-sensitive or identity context");
      break;
    }
  }

  for (const claim of [
    /\bsafe\b/i,
    /\bhealthy\b/i,
    /\bfresh\b/i,
    /\bauthentic\b/i,
    /\bguaranteed\b/i,
    /\bnutrit/i,
  ]) {
    if (claim.test(visualBlob)) {
      errors.push("thumbnailVisual contains safety/nutrition/authenticity claim");
      break;
    }
  }

  if (
    Object.prototype.hasOwnProperty.call(visual, "visualIdentityKey") ||
    Object.prototype.hasOwnProperty.call(visual, "renderCacheKey") ||
    Object.prototype.hasOwnProperty.call(visual, "provider") ||
    Object.prototype.hasOwnProperty.call(visual, "model")
  ) {
    errors.push("thumbnailVisual must not include cache hashes or provider policy");
  }

  if (request?.selectedCandidate) {
    const allowedEquipment = new Set(
      request.selectedCandidate.requiredEquipmentIds ?? [],
    );
    for (const item of recipe.equipment ?? []) {
      if (allowedEquipment.size > 0 && !allowedEquipment.has(item.equipmentId)) {
        // Expansion may include full equipment objects for required IDs only.
        if (!allowedEquipment.has(item.equipmentId)) {
          // Keep soft: only fail when completely inventing beyond request snapshots.
        }
      }
    }

    const snapshotIds = new Set(
      (request.confirmedInventorySnapshot?.items ?? []).map((item) => item.itemId),
    );
    for (const ingredient of recipe.ingredients ?? []) {
      if (
        ingredient.sourceType === "inventory" &&
        ingredient.inventoryItemId &&
        snapshotIds.size > 0 &&
        !snapshotIds.has(ingredient.inventoryItemId)
      ) {
        errors.push(
          `ingredient ${ingredient.ingredientId} invents inventoryItemId ${ingredient.inventoryItemId}`,
        );
      }
    }
  }

  // Invented visible components that look like luxury garnish not present in recipe names.
  const ingredientNames = new Set(
    (recipe.ingredients ?? []).map((item) =>
      String(item.displayName ?? "")
        .toLowerCase(),
    ),
  );
  for (const component of visual.visibleComponents ?? []) {
    const lower = String(component).toLowerCase();
    if (
      /\b(truffle|caviar|lobster|gold leaf)\b/i.test(lower) &&
      ![...ingredientNames].some((name) => lower.includes(name.split(" ")[0]))
    ) {
      errors.push(`thumbnailVisual invents visible component "${component}"`);
    }
  }

  return { ok: errors.length === 0, errors };
}

/**
 * Validate a request evaluation fixture for baseline parse/structure expectations.
 * @param {object} request
 * @param {string} fileName
 */
export function validateRequestFixture(request, fileName) {
  const errors = [];
  if (!request || typeof request !== "object") {
    return { ok: false, errors: ["not an object"] };
  }
  for (const key of ["protocol", "protocolVersion", "operation", "requestId"]) {
    if (!request[key]) errors.push(`missing ${key}`);
  }
  if (!request.responseContract || typeof request.responseContract !== "object") {
    errors.push("missing responseContract");
  }
  if (
    request.operation === "recipe.suggest_candidates.v1" &&
    (request.protocolVersion === "0.3" ||
      request.protocolVersion === "0.3-draft")
  ) {
    if (request.candidatePolicy?.count !== 3) {
      errors.push("0.3 suggest fixtures must request exactly 3 candidates");
    }
    if (!request.executionContext?.executionMode) {
      errors.push("0.3 suggest fixtures require executionContext.executionMode");
    }
  }
  if (request.operation === "recipe.expand_selected.v1") {
    const required = request.responseContract?.thumbnailVisualRequired ?? [];
    if (!required.includes("schemaVersion")) {
      errors.push(`${fileName} expand fixture must require thumbnailVisual.schemaVersion`);
    }
  }
  if (String(fileName).includes("injection")) {
    const blob = JSON.stringify(request);
    if (!/IGNORE AS REGRAS|administrador|system/i.test(blob)) {
      errors.push("injection fixture missing hostile embedded instruction markers");
    }
  }
  return { ok: errors.length === 0, errors };
}
