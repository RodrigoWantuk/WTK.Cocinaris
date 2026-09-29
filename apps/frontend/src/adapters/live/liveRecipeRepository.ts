import {
  createKitchenFlowClient,
  readProblemDetails,
  type components,
} from "@kitchenflow/api-client";
import {
  RecipeApiError,
  type RecipeApiErrorCode,
  type RecipeCandidate,
  type RecipeDetail,
  type RecipeGenerationSession,
  type RecipeRepository,
  type RecipeSummary,
} from "@/contracts/recipes";

type CandidateDto = components["schemas"]["RecipeCandidateResponse"];
type SessionDto = components["schemas"]["RecipeGenerationSessionResponse"];
type SummaryDto = components["schemas"]["RecipeSummaryResponse"];
type DetailDto = components["schemas"]["RecipeDetailResponse"];

const KNOWN_ERROR_CODES = new Set<RecipeApiErrorCode>([
  "authentication_required",
  "validation_failed",
  "not_found",
  "conflict",
  "domain_rule_violated",
  "ai_capability_unavailable",
  "ai_budget_exhausted",
  "ai_budget_unavailable",
  "ai_provider_timeout",
  "ai_provider_unavailable",
  "ai_output_invalid",
  "ai_operation_conflict",
  "unavailable",
  "cancelled",
  "unexpected",
]);

function asFiniteNumber(value: number | string, field: string): number {
  const n = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(n)) {
    throw new RecipeApiError(
      "unexpected",
      `Malformed recipe projection: missing ${field}.`,
      502,
    );
  }
  return n;
}

function assertString(value: unknown, field: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw new RecipeApiError(
      "unexpected",
      `Malformed recipe projection: missing ${field}.`,
      502,
    );
  }
  return value;
}

function mapCandidate(dto: CandidateDto): RecipeCandidate {
  return {
    candidateId: assertString(dto.candidateId, "candidateId"),
    candidateStrategy: assertString(dto.candidateStrategy, "candidateStrategy"),
    name: assertString(dto.name, "name"),
    targetMealType: assertString(dto.targetMealType, "targetMealType"),
    dishFormat: assertString(dto.dishFormat, "dishFormat"),
    primaryTechnique: assertString(dto.primaryTechnique, "primaryTechnique"),
    primaryIngredientRefs: Array.isArray(dto.primaryIngredientRefs)
      ? dto.primaryIngredientRefs.filter((item) => typeof item === "string")
      : [],
    summary: assertString(dto.summary, "summary"),
    servings: asFiniteNumber(dto.servings, "servings"),
    activeMinutes: asFiniteNumber(dto.activeMinutes, "activeMinutes"),
    passiveMinutes: asFiniteNumber(dto.passiveMinutes, "passiveMinutes"),
    totalMinutes: asFiniteNumber(dto.totalMinutes, "totalMinutes"),
    difficulty: assertString(dto.difficulty, "difficulty"),
    requiredEquipmentIds: Array.isArray(dto.requiredEquipmentIds)
      ? dto.requiredEquipmentIds.filter((item) => typeof item === "string")
      : [],
    requiredCapabilities: Array.isArray(dto.requiredCapabilities)
      ? dto.requiredCapabilities.filter((item) => typeof item === "string")
      : [],
  };
}

function mapSession(dto: SessionDto): RecipeGenerationSession {
  return {
    sessionId: assertString(dto.sessionId, "sessionId"),
    status: assertString(dto.status, "status"),
    candidates: Array.isArray(dto.candidates)
      ? dto.candidates.map(mapCandidate)
      : null,
    failureReason:
      typeof dto.failureReason === "string" ? dto.failureReason : null,
  };
}

function mapSummary(dto: SummaryDto): RecipeSummary {
  return {
    recipeId: assertString(dto.recipeId, "recipeId"),
    name: assertString(dto.name, "name"),
    mealTypes: Array.isArray(dto.mealTypes)
      ? dto.mealTypes.filter((item) => typeof item === "string")
      : [],
    servings: asFiniteNumber(dto.servings, "servings"),
    createdAt: assertString(dto.createdAt, "createdAt"),
  };
}

function mapDetail(dto: DetailDto): RecipeDetail {
  return {
    recipeId: assertString(dto.recipeId, "recipeId"),
    revisionNumber: asFiniteNumber(dto.revisionNumber, "revisionNumber"),
    name: assertString(dto.name, "name"),
    mealTypes: Array.isArray(dto.mealTypes)
      ? dto.mealTypes.filter((item) => typeof item === "string")
      : [],
    servings: asFiniteNumber(dto.servings, "servings"),
    normalizedRecipeJson: assertString(
      dto.normalizedRecipeJson,
      "normalizedRecipeJson",
    ),
    thumbnailVisualJson: assertString(
      dto.thumbnailVisualJson,
      "thumbnailVisualJson",
    ),
    createdAt: assertString(dto.createdAt, "createdAt"),
  };
}

function assertData<T>(
  data: T | undefined,
  response: Response,
): asserts data is T {
  if (!response.ok || data === undefined) {
    throw new RecipeApiError(
      "unexpected",
      "Unexpected empty success payload.",
      response.status,
    );
  }
}

async function throwForFailure(
  response: Response,
  fallback: string,
): Promise<never> {
  const problem = await readProblemDetails(response);
  const fieldErrors: Record<string, string[]> = {};
  if (problem?.errors) {
    for (const [key, value] of Object.entries(problem.errors)) {
      if (value) fieldErrors[key] = value;
    }
  }
  const detail = problem?.detail ?? problem?.title ?? fallback;
  const rawCode = problem?.errorCode;

  if (rawCode === "resource_not_found") {
    throw new RecipeApiError("not_found", detail, response.status, {
      fieldErrors,
      traceId: problem?.traceId ?? undefined,
    });
  }

  const declared = rawCode as RecipeApiErrorCode | undefined;

  if (declared && KNOWN_ERROR_CODES.has(declared)) {
    throw new RecipeApiError(declared, detail, response.status, {
      fieldErrors,
      traceId: problem?.traceId ?? undefined,
    });
  }

  if (response.status === 401) {
    throw new RecipeApiError("authentication_required", detail, 401, {
      traceId: problem?.traceId ?? undefined,
    });
  }
  if (response.status === 404) {
    throw new RecipeApiError("not_found", detail, 404, {
      traceId: problem?.traceId ?? undefined,
    });
  }
  if (response.status === 400) {
    throw new RecipeApiError("validation_failed", detail, 400, {
      fieldErrors,
      traceId: problem?.traceId ?? undefined,
    });
  }
  if (response.status === 409) {
    throw new RecipeApiError("conflict", detail, 409, {
      traceId: problem?.traceId ?? undefined,
    });
  }
  if (response.status === 422) {
    throw new RecipeApiError("ai_output_invalid", detail, 422, {
      fieldErrors,
      traceId: problem?.traceId ?? undefined,
    });
  }
  if (response.status >= 500 || response.status === 0) {
    throw new RecipeApiError("unavailable", fallback, response.status || 503, {
      traceId: problem?.traceId ?? undefined,
    });
  }
  throw new RecipeApiError("unexpected", detail, response.status, {
    fieldErrors,
    traceId: problem?.traceId ?? undefined,
  });
}

function wrapAbort(err: unknown): never {
  if (err instanceof RecipeApiError) throw err;
  if (err instanceof DOMException && err.name === "AbortError") {
    throw new RecipeApiError("cancelled", "Request cancelled.", 0);
  }
  throw new RecipeApiError("unavailable", "Recipe service unavailable.", 503);
}

/**
 * Live recipe cook-now boundary over the generated OpenAPI client.
 * Performs no AI orchestration or inventory mutation; backend owns validation.
 */
export function createLiveRecipeRepository(options?: {
  fetchImpl?: typeof fetch;
  baseUrl?: string;
}): RecipeRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createKitchenFlowClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });

  return {
    async listRecipes(signal) {
      try {
        const { data, response } = await client.GET("/api/v1/recipes", {
          signal,
          credentials: "include",
        });
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to list recipes.");
        }
        assertData(data, response);
        if (!Array.isArray(data)) {
          throw new RecipeApiError(
            "unexpected",
            "Malformed recipe list projection.",
            502,
          );
        }
        return data.map(mapSummary);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async getRecipe(recipeId, signal) {
      try {
        const { data, response } = await client.GET(
          "/api/v1/recipes/{recipeId}",
          {
            params: { path: { recipeId } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load recipe.");
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async requestCandidates({ csrfToken, idempotencyKey, signal }) {
      try {
        const { data, response } = await client.POST(
          "/api/v1/recipes/generation-sessions",
          {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to request recipe candidates.",
          );
        }
        assertData(data, response);
        return mapSession(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async getGenerationSession(sessionId, signal) {
      try {
        const { data, response } = await client.GET(
          "/api/v1/recipes/generation-sessions/{sessionId}",
          {
            params: { path: { sessionId } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load generation session.");
        }
        assertData(data, response);
        return mapSession(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async selectCandidate(
      sessionId,
      candidateId,
      { csrfToken, idempotencyKey, signal },
    ) {
      try {
        const body: components["schemas"]["SelectCandidateRequest"] = {
          candidateId,
        };
        const { data, response } = await client.POST(
          "/api/v1/recipes/generation-sessions/{sessionId}/selection",
          {
            params: {
              path: { sessionId },
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            body,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to select recipe candidate.");
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },
  };
}
