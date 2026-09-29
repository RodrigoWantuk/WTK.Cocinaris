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

/** Stable error codes mapped from recipe BFF Problem Details. */
export type RecipeApiErrorCode =
  | "authentication_required"
  | "validation_failed"
  | "not_found"
  | "conflict"
  | "domain_rule_violated"
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
  requestCandidates(options: {
    csrfToken: string;
    idempotencyKey: string;
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
      signal?: AbortSignal;
    },
  ): Promise<RecipeDetail>;
}
