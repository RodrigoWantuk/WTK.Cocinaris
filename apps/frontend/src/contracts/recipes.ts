/**
 * Application-owned recipe cook-now contracts.
 * Isolated from generated OpenAPI DTO shapes until recipe paths are regenerated.
 */

/** One cook-now candidate returned by a generation session. */
export interface RecipeCandidate {
  candidateId: string;
  candidateStrategy: string;
  name: string;
  targetMealType: string;
  dishFormat: string;
  primaryTechnique: string;
  primaryIngredientRefs: string[];
  summary: string;
  servings: number;
  activeMinutes: number;
  passiveMinutes: number;
  totalMinutes: number;
  difficulty: string;
  requiredEquipmentIds: string[];
  requiredCapabilities: string[];
}

/** Generation session projection after suggest or replay. */
export interface RecipeGenerationSession {
  sessionId: string;
  status: string;
  candidates: RecipeCandidate[] | null;
  failureReason: string | null;
}

/** Owned recipe list row. */
export interface RecipeSummary {
  recipeId: string;
  name: string;
  mealTypes: string[];
  servings: number;
  createdAt: string;
}

/** Owned recipe detail after expansion or list navigation. */
export interface RecipeDetail {
  recipeId: string;
  revisionNumber: number;
  name: string;
  mealTypes: string[];
  servings: number;
  /** Opaque validated normalized recipe JSON string from the backend. */
  normalizedRecipeJson: string;
  thumbnailVisualJson: string;
  createdAt: string;
}

/** One recipe ingredient matched against the pantry (PLAN-0042). */
export interface ExecutionProposalLine {
  ingredientId: string;
  displayName: string;
  optional: boolean;
  /** Quantity for the servings cooked, in `unit`. */
  requiredQuantity: number;
  /** Lot canonical unit: `Gram`, `Milliliter`, or `Unit`. */
  unit: string;
  /** `ready`, `insufficient`, `lot_unavailable`, or `not_tracked`. */
  status: string;
  lotId: string | null;
  proposedQuantity: number | null;
}

/** An active measured pantry lot the cook can pick for any line. */
export interface ExecutionPantryLot {
  lotId: string;
  productName: string;
  quantity: number;
  unit: string;
  storageLocation: string;
  /** Lot version read with the proposal; completion fails if the lot changed. */
  version: string;
}

/** One recipe execution; `proposal` is present while in progress. */
export interface RecipeExecution {
  executionId: string;
  recipeId: string;
  revisionNumber: number;
  recipeName: string;
  servings: number;
  recipeServings: number;
  status: "InProgress" | "Completed" | "Abandoned" | string;
  startedAt: string;
  finishedAt: string | null;
  inventoryBatchId: string | null;
  rating: number | null;
  notes: string | null;
  normalizedRecipeJson: string;
  proposal: {
    lines: ExecutionProposalLine[];
    pantryLots: ExecutionPantryLot[];
  } | null;
}

/** Compact in-progress execution. */
export interface RecipeExecutionSummary {
  executionId: string;
  recipeId: string;
  recipeName: string;
  servings: number;
  startedAt: string;
}

/** What the cook confirms at completion; nothing is inferred server-side. */
export interface CompleteExecutionInput {
  consumptions: Array<{
    lotId: string;
    quantity: number;
    unit: string;
    version: string;
  }>;
  leftover: {
    portions: number;
    storageLocation: string;
    useBy: string | null;
  } | null;
  rating: number | null;
  notes: string | null;
}

/** Stable error codes mapped from recipe BFF Problem Details. */
export type RecipeApiErrorCode =
  | "authentication_required"
  | "validation_failed"
  | "not_found"
  | "conflict"
  | "domain_rule_violated"
  | "precondition_failed"
  | "ai_capability_unavailable"
  | "ai_budget_exhausted"
  | "ai_budget_unavailable"
  | "ai_provider_timeout"
  | "ai_provider_unavailable"
  | "ai_output_invalid"
  | "ai_operation_conflict"
  | "unavailable"
  | "cancelled"
  | "unexpected";

/**
 * Fail-closed recipe boundary error. UI maps codes to localized unavailable /
 * retry / invalid-output states without inventing success.
 */
export class RecipeApiError extends Error {
  readonly code: RecipeApiErrorCode;
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;
  readonly traceId?: string;

  constructor(
    code: RecipeApiErrorCode,
    message: string,
    status: number,
    options?: {
      fieldErrors?: Record<string, string[]>;
      traceId?: string;
    },
  ) {
    super(message);
    this.name = "RecipeApiError";
    this.code = code;
    this.status = status;
    this.fieldErrors = options?.fieldErrors ?? {};
    this.traceId = options?.traceId;
  }
}

/** Live or unavailable recipe cook-now repository boundary. */
export interface RecipeRepository {
  listRecipes(signal?: AbortSignal): Promise<RecipeSummary[]>;
  getRecipe(recipeId: string, signal?: AbortSignal): Promise<RecipeDetail>;
  /** `language` is the interface locale; the AI writes in it unless the profile sets one. */
  requestCandidates(options: {
    csrfToken: string;
    idempotencyKey: string;
    language?: string;
    signal?: AbortSignal;
  }): Promise<RecipeGenerationSession>;
  getGenerationSession(
    sessionId: string,
    signal?: AbortSignal,
  ): Promise<RecipeGenerationSession>;
  selectCandidate(
    sessionId: string,
    candidateId: string,
    options: {
      csrfToken: string;
      idempotencyKey: string;
      language?: string;
      signal?: AbortSignal;
    },
  ): Promise<RecipeDetail>;
  /** Starts cooking, or returns the recipe's execution already in progress. */
  startExecution(
    recipeId: string,
    options: { csrfToken: string; servings?: number; signal?: AbortSignal },
  ): Promise<RecipeExecution>;
  getExecution(
    executionId: string,
    signal?: AbortSignal,
  ): Promise<RecipeExecution>;
  listExecutionsInProgress(
    signal?: AbortSignal,
  ): Promise<RecipeExecutionSummary[]>;
  /** Consumes the confirmed lots and stores leftovers atomically; a retry with the same key replays. */
  completeExecution(
    executionId: string,
    input: CompleteExecutionInput,
    options: {
      csrfToken: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<RecipeExecution>;
  abandonExecution(
    executionId: string,
    options: { csrfToken: string; signal?: AbortSignal },
  ): Promise<RecipeExecution>;
}
