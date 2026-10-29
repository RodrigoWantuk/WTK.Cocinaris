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

/** `generated` (AI), `manual` (written by the owner), or `derived` (variation of another recipe). */
export type RecipeOrigin = "generated" | "manual" | "derived" | string;

/** Owned recipe library row (PLAN-0038). */
export interface RecipeSummary {
  recipeId: string;
  name: string;
  mealTypes: string[];
  servings: number;
  createdAt: string;
  revisionNumber: number;
  origin: RecipeOrigin;
  isFavorite: boolean;
  archivedAt: string | null;
  updatedAt: string;
  /** Completed cooking sessions. */
  timesCooked: number;
  lastCookedAt: string | null;
  lastRating: number | null;
}

/** One revision of an owned recipe with its library state. */
export interface RecipeDetail {
  recipeId: string;
  /** Revision shown; older than `currentRevisionNumber` when viewing history. */
  revisionNumber: number;
  name: string;
  mealTypes: string[];
  servings: number;
  /** Opaque validated normalized recipe JSON string from the backend. */
  normalizedRecipeJson: string;
  thumbnailVisualJson: string;
  createdAt: string;
  currentRevisionNumber: number;
  updatedAt: string;
  origin: RecipeOrigin;
  isFavorite: boolean;
  archivedAt: string | null;
  parentRecipeId: string | null;
  parentRevisionNumber: number | null;
  derivationReason: string | null;
  /** `generated`, `manual`, `edited`, or `derived`. */
  revisionOrigin: string;
  changeReason: string | null;
}

/** Library filter; archived recipes are listed only on request. */
export interface RecipeListFilter {
  archived?: boolean;
  favorites?: boolean;
  search?: string;
}

/** A recipe's revisions and finished cooking sessions, newest first. */
export interface RecipeHistory {
  revisions: Array<{
    revisionNumber: number;
    name: string;
    origin: string;
    changeReason: string | null;
    sourceExecutionId: string | null;
    createdAt: string;
  }>;
  cooking: Array<{
    executionId: string;
    revisionNumber: number;
    servings: number;
    /** `Completed` or `Abandoned`. */
    status: string;
    startedAt: string;
    finishedAt: string | null;
    rating: number | null;
    notes: string | null;
  }>;
}

/**
 * Editable recipe content. `ingredientId` and `stageId` come from the revision being edited; the
 * backend keeps the pantry link and dependencies of rows that keep their identifier.
 */
export interface RecipeContentInput {
  name: string;
  mealTypes: string[];
  servings: number;
  yield: string | null;
  ingredients: Array<{
    ingredientId: string | null;
    displayName: string;
    quantity: number;
    /** `g`, `ml`, or `unit`. */
    unit: string;
    optional: boolean;
  }>;
  equipment: string[];
  stages: Array<{
    stageId: string | null;
    name: string;
    instructions: string;
    activeMinutes: number;
    passiveMinutes: number;
    sensoryCues: string[];
  }>;
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
  /** Active library by default; `filter.archived` lists archived recipes instead. */
  listRecipes(
    signal?: AbortSignal,
    filter?: RecipeListFilter,
  ): Promise<RecipeSummary[]>;
  getRecipe(recipeId: string, signal?: AbortSignal): Promise<RecipeDetail>;
  getRecipeRevision(
    recipeId: string,
    revisionNumber: number,
    signal?: AbortSignal,
  ): Promise<RecipeDetail>;
  getRecipeHistory(
    recipeId: string,
    signal?: AbortSignal,
  ): Promise<RecipeHistory>;
  /** Writes a recipe from scratch; a retry with the same key returns the same recipe. */
  createRecipe(
    content: RecipeContentInput,
    options: {
      csrfToken: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<RecipeDetail>;
  /** Saves an edit as the next revision; fails with `precondition_failed` when a newer revision exists. */
  reviseRecipe(
    recipeId: string,
    input: {
      expectedRevisionNumber: number;
      content: RecipeContentInput;
      changeReason: string | null;
      sourceExecutionId: string | null;
    },
    options: { csrfToken: string; signal?: AbortSignal },
  ): Promise<RecipeDetail>;
  /** Creates a new recipe derived from a revision; the parent is not changed. */
  deriveRecipe(
    recipeId: string,
    input: {
      parentRevisionNumber: number;
      content: RecipeContentInput;
      derivationReason: string;
      sourceExecutionId: string | null;
    },
    options: {
      csrfToken: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<RecipeDetail>;
  setFavorite(
    recipeId: string,
    isFavorite: boolean,
    options: { csrfToken: string; signal?: AbortSignal },
  ): Promise<RecipeDetail>;
  /** Archives (hides, keeping history) or brings a recipe back. */
  setArchived(
    recipeId: string,
    archived: boolean,
    options: { csrfToken: string; signal?: AbortSignal },
  ): Promise<RecipeDetail>;
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
  /**
   * Starts cooking, or returns the recipe's execution already in progress. `menuEntryId` links a
   * new session to the menu meal it was started from (PLAN-0029).
   */
  startExecution(
    recipeId: string,
    options: {
      csrfToken: string;
      servings?: number;
      menuEntryId?: string;
      signal?: AbortSignal;
    },
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
