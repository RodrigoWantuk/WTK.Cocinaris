import {
  createCocinarisClient,
  readProblemDetails,
  type components,
} from "@cocinaris/api-client";
import {
  RecipeApiError,
  type RecipeApiErrorCode,
  type RecipeCandidate,
  type RecipeExecution,
  type RecipeExecutionSummary,
  type RecipeDetail,
  type RecipeGenerationSession,
  type RecipeHistory,
  type RecipeRepository,
  type RecipeSummary,
  type RecipeThumbnail,
} from "@/contracts/recipes";

type CandidateDto = components["schemas"]["RecipeCandidateResponse"];
type SessionDto = components["schemas"]["RecipeGenerationSessionResponse"];
type SummaryDto = components["schemas"]["RecipeSummaryResponse"];
type DetailDto = components["schemas"]["RecipeDetailResponse"];
type ExecutionDto = components["schemas"]["RecipeExecutionResponse"];
type ExecutionSummaryDto =
  components["schemas"]["RecipeExecutionSummaryResponse"];
type HistoryDto = components["schemas"]["RecipeHistoryResponse"];

const text = (value: unknown): string | null =>
  typeof value === "string" ? value : null;
const count = (value: unknown): number | null =>
  value === null || value === undefined || value === "" ? null : Number(value);

const KNOWN_ERROR_CODES = new Set<RecipeApiErrorCode>([
  "authentication_required",
  "validation_failed",
  "not_found",
  "conflict",
  "domain_rule_violated",
  "precondition_failed",
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

// The image URLs come from the API; only this API's own thumbnail path is accepted, so a
// malformed or hostile value can never point an <img> elsewhere. The version may carry an
// encoding suffix (".w1" = WebP), and the card URL adds "size=small".
const THUMBNAIL_PATH =
  /^\/api\/v1\/recipes\/[0-9a-f-]{36}\/thumbnail\?(revision=\d+&)?(size=small&)?v=[0-9a-f]{16}(\.w\d+)?$/;

function mapThumbnail(value: unknown): RecipeThumbnail {
  const dto = (value ?? {}) as {
    state?: unknown;
    url?: unknown;
    smallUrl?: unknown;
  };
  if (
    dto.state === "ready" &&
    typeof dto.url === "string" &&
    THUMBNAIL_PATH.test(dto.url)
  )
    return {
      state: "ready",
      url: dto.url,
      // A missing or rejected card URL falls back to the full image.
      smallUrl:
        typeof dto.smallUrl === "string" && THUMBNAIL_PATH.test(dto.smallUrl)
          ? dto.smallUrl
          : null,
    };
  return {
    state: dto.state === "pending" ? "pending" : "none",
    url: null,
    smallUrl: null,
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
    revisionNumber: asFiniteNumber(dto.revisionNumber, "revisionNumber"),
    origin: assertString(dto.origin, "origin"),
    isFavorite: dto.isFavorite === true,
    archivedAt: text(dto.archivedAt),
    updatedAt: assertString(dto.updatedAt, "updatedAt"),
    timesCooked: asFiniteNumber(dto.timesCooked, "timesCooked"),
    lastCookedAt: text(dto.lastCookedAt),
    lastRating: count(dto.lastRating),
    thumbnail: mapThumbnail(dto.thumbnail),
  };
}

function mapHistory(dto: HistoryDto): RecipeHistory {
  return {
    revisions: (dto.revisions ?? []).map((item) => ({
      revisionNumber: asFiniteNumber(item.revisionNumber, "revisionNumber"),
      name: String(item.name),
      origin: String(item.origin),
      changeReason: text(item.changeReason),
      sourceExecutionId: text(item.sourceExecutionId),
      createdAt: String(item.createdAt),
    })),
    cooking: (dto.cooking ?? []).map((item) => ({
      executionId: String(item.executionId),
      revisionNumber: asFiniteNumber(item.revisionNumber, "revisionNumber"),
      servings: asFiniteNumber(item.servings, "servings"),
      status: String(item.status),
      startedAt: String(item.startedAt),
      finishedAt: text(item.finishedAt),
      rating: count(item.rating),
      notes: text(item.notes),
    })),
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
    currentRevisionNumber: asFiniteNumber(
      dto.currentRevisionNumber,
      "currentRevisionNumber",
    ),
    updatedAt: assertString(dto.updatedAt, "updatedAt"),
    origin: assertString(dto.origin, "origin"),
    isFavorite: dto.isFavorite === true,
    archivedAt: text(dto.archivedAt),
    parentRecipeId: text(dto.parentRecipeId),
    parentRevisionNumber: count(dto.parentRevisionNumber),
    derivationReason: text(dto.derivationReason),
    revisionOrigin: assertString(dto.revisionOrigin, "revisionOrigin"),
    changeReason: text(dto.changeReason),
    thumbnail: mapThumbnail(dto.thumbnail),
  };
}

function mapExecution(dto: ExecutionDto): RecipeExecution {
  const proposal = dto.proposal;
  return {
    executionId: assertString(dto.executionId, "executionId"),
    recipeId: assertString(dto.recipeId, "recipeId"),
    revisionNumber: asFiniteNumber(dto.revisionNumber, "revisionNumber"),
    recipeName: assertString(dto.recipeName, "recipeName"),
    servings: asFiniteNumber(dto.servings, "servings"),
    recipeServings: asFiniteNumber(dto.recipeServings, "recipeServings"),
    status: assertString(dto.status, "status"),
    startedAt: assertString(dto.startedAt, "startedAt"),
    finishedAt: typeof dto.finishedAt === "string" ? dto.finishedAt : null,
    inventoryBatchId:
      typeof dto.inventoryBatchId === "string" ? dto.inventoryBatchId : null,
    rating: typeof dto.rating === "number" ? dto.rating : null,
    notes: typeof dto.notes === "string" ? dto.notes : null,
    normalizedRecipeJson: assertString(
      dto.normalizedRecipeJson,
      "normalizedRecipeJson",
    ),
    proposal: proposal
      ? {
          lines: (proposal.lines ?? []).map((line) => ({
            ingredientId: String(line.ingredientId),
            displayName: String(line.displayName),
            optional: line.optional === true,
            requiredQuantity: asFiniteNumber(
              line.requiredQuantity,
              "requiredQuantity",
            ),
            unit: String(line.unit),
            status: String(line.status),
            lotId: typeof line.lotId === "string" ? line.lotId : null,
            proposedQuantity:
              line.proposedQuantity === null ||
              line.proposedQuantity === undefined
                ? null
                : asFiniteNumber(line.proposedQuantity, "proposedQuantity"),
            protectedQuantity: Number(line.protectedQuantity ?? 0),
            reservedElsewhere: (line.reservedElsewhere ?? []).map((item) => ({
              menuEntryId: String(item.menuEntryId),
              recipeName: String(item.recipeName),
              plannedDate:
                typeof item.plannedDate === "string" ? item.plannedDate : null,
              quantity: Number(item.quantity),
            })),
          })),
          pantryLots: (proposal.pantryLots ?? []).map((lot) => ({
            lotId: assertString(lot.lotId, "lotId"),
            productName: String(lot.productName),
            quantity: asFiniteNumber(lot.quantity, "quantity"),
            unit: String(lot.unit),
            storageLocation: String(lot.storageLocation),
            version: assertString(lot.version, "version"),
            protectedQuantity: Number(lot.protectedQuantity ?? 0),
            reservedQuantity: Number(lot.reservedQuantity ?? 0),
            reservedFor: (lot.reservedFor ?? []).map((item) => ({
              menuEntryId: String(item.menuEntryId),
              recipeName: String(item.recipeName),
              plannedDate:
                typeof item.plannedDate === "string" ? item.plannedDate : null,
              quantity: Number(item.quantity),
            })),
          })),
        }
      : null,
  };
}

function mapExecutionSummary(dto: ExecutionSummaryDto): RecipeExecutionSummary {
  return {
    executionId: assertString(dto.executionId, "executionId"),
    recipeId: assertString(dto.recipeId, "recipeId"),
    recipeName: assertString(dto.recipeName, "recipeName"),
    servings: asFiniteNumber(dto.servings, "servings"),
    startedAt: assertString(dto.startedAt, "startedAt"),
  };
}

/** Asserts a successful payload; shared with the planning adapter. */
export function assertData<T>(
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

/** Maps Problem Details to {@link RecipeApiError}; shared with the planning adapter. */
export async function throwForFailure(
  response: Response,
  fallback: string,
  // openapi-fetch has already consumed the error body; pass its parsed value.
  parsedBody?: unknown,
): Promise<never> {
  const problem = await readProblemDetails(response, parsedBody);
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

/** Rethrows boundary errors and maps aborts and network failures; shared with the planning adapter. */
export function wrapAbort(err: unknown): never {
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
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });

  return {
    async listRecipes(signal, filter) {
      try {
        const { data, error, response } = await client.GET("/api/v1/recipes", {
          params: {
            query: {
              archived: filter?.archived || undefined,
              favorites: filter?.favorites || undefined,
              search: filter?.search?.trim() || undefined,
            },
          },
          signal,
          credentials: "include",
        });
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to list recipes.", error);
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
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/{recipeId}",
          {
            params: { path: { recipeId } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load recipe.", error);
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async getRecipeRevision(recipeId, revisionNumber, signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/{recipeId}/revisions/{revisionNumber}",
          {
            params: { path: { recipeId, revisionNumber } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to load the revision.",
            error,
          );
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async getRecipeHistory(recipeId, signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/{recipeId}/history",
          {
            params: { path: { recipeId } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load history.", error);
        }
        assertData(data, response);
        return mapHistory(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async createRecipe(content, { csrfToken, idempotencyKey, signal }) {
      try {
        const { data, error, response } = await client.POST("/api/v1/recipes", {
          params: {
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "Idempotency-Key": idempotencyKey,
            },
          },
          body: { content },
          signal,
          credentials: "include",
        });
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to save the recipe.", error);
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async reviseRecipe(recipeId, input, { csrfToken, signal }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/{recipeId}/revisions",
          {
            params: {
              path: { recipeId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: input,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to save the recipe.", error);
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async deriveRecipe(recipeId, input, { csrfToken, idempotencyKey, signal }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/{recipeId}/derivations",
          {
            params: {
              path: { recipeId },
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            body: input,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to save the recipe.", error);
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async setFavorite(recipeId, isFavorite, { csrfToken, signal }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/{recipeId}/favorite",
          {
            params: {
              path: { recipeId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: { isFavorite },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to update the recipe.",
            error,
          );
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async setArchived(recipeId, archived, { csrfToken, signal }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/{recipeId}/archive",
          {
            params: {
              path: { recipeId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: { archived },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to update the recipe.",
            error,
          );
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async requestCandidates({
      csrfToken,
      idempotencyKey,
      language,
      menuPlanning,
      guidance,
      signal,
    }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/generation-sessions",
          {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            body:
              menuPlanning || guidance
                ? {
                    menuPlanning: menuPlanning ?? null,
                    guidance: guidance ?? null,
                  }
                : undefined,
            headers: language ? { "Accept-Language": language } : undefined,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to request recipe candidates.",
            error,
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
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/generation-sessions/{sessionId}",
          {
            params: { path: { sessionId } },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to load generation session.",
            error,
          );
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
      { csrfToken, idempotencyKey, language, signal },
    ) {
      try {
        const body: components["schemas"]["SelectCandidateRequest"] = {
          candidateId,
        };
        const { data, error, response } = await client.POST(
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
            headers: language ? { "Accept-Language": language } : undefined,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to select recipe candidate.",
            error,
          );
        }
        assertData(data, response);
        return mapDetail(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async startExecution(
      recipeId,
      { csrfToken, servings, menuEntryId, signal },
    ) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/{recipeId}/executions",
          {
            params: {
              path: { recipeId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: {
              servings: servings ?? null,
              menuEntryId: menuEntryId ?? null,
            },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to start cooking.", error);
        }
        assertData(data, response);
        return mapExecution(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async getExecution(executionId, signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/executions/{executionId}",
          { params: { path: { executionId } }, signal, credentials: "include" },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to load the cooking session.",
            error,
          );
        }
        assertData(data, response);
        return mapExecution(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async listExecutionsInProgress(signal) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/recipes/executions/in-progress",
          { signal, credentials: "include" },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to list cooking sessions.",
            error,
          );
        }
        assertData(data, response);
        return Array.isArray(data) ? data.map(mapExecutionSummary) : [];
      } catch (err) {
        wrapAbort(err);
      }
    },

    async completeExecution(
      executionId,
      input,
      { csrfToken, idempotencyKey, signal },
    ) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/executions/{executionId}/completion",
          {
            params: {
              path: { executionId },
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            body: input,
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to finish cooking.", error);
        }
        assertData(data, response);
        return mapExecution(data);
      } catch (err) {
        wrapAbort(err);
      }
    },

    async abandonExecution(executionId, { csrfToken, signal }) {
      try {
        const { data, error, response } = await client.POST(
          "/api/v1/recipes/executions/{executionId}/abandonment",
          {
            params: {
              path: { executionId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to stop cooking.", error);
        }
        assertData(data, response);
        return mapExecution(data);
      } catch (err) {
        wrapAbort(err);
      }
    },
  };
}
