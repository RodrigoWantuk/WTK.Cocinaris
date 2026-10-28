import {
  createCocinarisClient,
  readEtag,
  readProblemDetails,
} from "@cocinaris/api-client";
import {
  ProfileApiError,
  type ProfileApiErrorCode,
  type ProfileRepository,
} from "@/contracts/profile";
import {
  mapCompleteness,
  mapEquipmentCollection,
  mapEquipmentInputsToRequest,
  mapPreferenceCommandsToRequest,
  mapPreferencesCollection,
  mapProfilePatchToRequest,
  mapProfileResponse,
} from "./mapProfile";

const KNOWN_ERROR_CODES = new Set<ProfileApiErrorCode>([
  "validation_failed",
  "authentication_required",
  "forbidden",
  "conflict",
  "profile_already_exists",
  "precondition_failed",
  "domain_rule_violated",
  "precondition_required",
  "unavailable",
  "cancelled",
  "malformed",
  "unexpected",
]);

function assertData<T>(
  data: T | undefined,
  response: Response,
): asserts data is T {
  if (!response.ok || data === undefined) {
    throw new ProfileApiError(
      "malformed",
      "Unexpected empty success payload.",
      response.status,
    );
  }
}

/**
 * Maps an HTTP failure to a {@link ProfileApiError}. Prefers the Problem Details
 * `errorCode` extension when the backend supplies a known code; otherwise falls back
 * to a deterministic status-based mapping so unexpected backend changes fail closed
 * rather than surfacing an unmapped error code to application code.
 */
async function throwForFailure(
  response: Response,
  fallback: string,
  /** The generated client's parsed `error` body; the response body is already consumed. */
  parsedBody?: unknown,
): Promise<never> {
  const problem = await readProblemDetails(response, parsedBody);
  const fieldErrors: Record<string, string[]> = {};
  if (problem?.errors) {
    for (const [key, value] of Object.entries(problem.errors)) {
      if (value) fieldErrors[key] = value;
    }
  }
  const traceId = problem?.traceId ?? undefined;
  const detail = problem?.detail ?? problem?.title ?? fallback;

  const declaredCode = problem?.errorCode as ProfileApiErrorCode | undefined;
  if (declaredCode && KNOWN_ERROR_CODES.has(declaredCode)) {
    throw new ProfileApiError(declaredCode, detail, response.status, {
      fieldErrors,
      traceId,
    });
  }

  if (response.status === 401) {
    throw new ProfileApiError("authentication_required", detail, 401, {
      traceId,
    });
  }
  if (response.status === 403) {
    throw new ProfileApiError("forbidden", detail, 403, { traceId });
  }
  if (response.status === 400) {
    throw new ProfileApiError("validation_failed", detail, 400, {
      fieldErrors,
      traceId,
    });
  }
  if (response.status === 409) {
    throw new ProfileApiError("conflict", detail, 409, { traceId });
  }
  if (response.status === 412) {
    throw new ProfileApiError(
      "precondition_failed",
      "The profile changed. Reload before retrying.",
      412,
      { traceId },
    );
  }
  if (response.status === 422) {
    throw new ProfileApiError("domain_rule_violated", detail, 422, { traceId });
  }
  if (response.status === 428) {
    throw new ProfileApiError(
      "precondition_required",
      "A current version (If-Match) is required.",
      428,
      { traceId },
    );
  }
  if (response.status >= 500 || response.status === 0) {
    throw new ProfileApiError("unavailable", fallback, response.status || 503, {
      traceId,
      retryable: true,
    });
  }
  throw new ProfileApiError("unexpected", detail, response.status, { traceId });
}

/**
 * Live profile/household/preferences/equipment boundary over the generated OpenAPI
 * client. Performs no authoritative arithmetic, catalog validation, or completeness
 * computation; the backend owns those.
 */
export function createLiveProfileRepository(options?: {
  fetchImpl?: typeof fetch;
  baseUrl?: string;
}): ProfileRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });

  async function guarded<T>(
    signal: AbortSignal | undefined,
    run: () => Promise<T>,
  ): Promise<T> {
    try {
      return await run();
    } catch (err) {
      if (err instanceof ProfileApiError) throw err;
      if (err instanceof DOMException && err.name === "AbortError") {
        throw new ProfileApiError("cancelled", "Request cancelled.", 0);
      }
      if (signal?.aborted) {
        throw new ProfileApiError("cancelled", "Request cancelled.", 0);
      }
      if (err instanceof SyntaxError) {
        // openapi-fetch calls response.json() directly; an empty or non-JSON
        // success body throws here rather than yielding `data: undefined`.
        throw new ProfileApiError(
          "malformed",
          "Unexpected empty or invalid success payload.",
          502,
        );
      }
      throw new ProfileApiError(
        "unavailable",
        "The profile service is unavailable.",
        503,
        {
          retryable: true,
        },
      );
    }
  }

  return {
    async getProfile(signal) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.GET("/api/v1/profile", {
          signal,
          credentials: "include",
        });
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load profile.", error);
        }
        assertData(data, response);
        const etag = data.profileExists ? readEtag(response) : null;
        return mapProfileResponse(data, etag);
      });
    },

    async patchProfile(patch, { csrfToken, etag, signal }) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.PATCH(
          "/api/v1/profile",
          {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "If-Match": etag ?? undefined,
              },
            },
            body: mapProfilePatchToRequest(patch),
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to update profile.", error);
        }
        assertData(data, response);
        return mapProfileResponse(data, readEtag(response));
      });
    },

    async replaceProfile(patch, { csrfToken, etag, signal }) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.PUT("/api/v1/profile", {
          params: {
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "If-Match": etag ?? undefined,
            },
          },
          body: mapProfilePatchToRequest(patch),
          signal,
          credentials: "include",
        });
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to replace profile.", error);
        }
        assertData(data, response);
        return mapProfileResponse(data, readEtag(response));
      });
    },

    async getPreferences(signal) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.GET(
          "/api/v1/profile/preferences",
          { signal, credentials: "include" },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load preferences.", error);
        }
        assertData(data, response);
        return mapPreferencesCollection(data, readEtag(response));
      });
    },

    async mutatePreferences(commands, { csrfToken, etag, signal }) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.PUT(
          "/api/v1/profile/preferences",
          {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "If-Match": etag ?? undefined,
              },
            },
            body: mapPreferenceCommandsToRequest(commands),
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to update preferences.",
            error,
          );
        }
        assertData(data, response);
        return mapPreferencesCollection(data, readEtag(response));
      });
    },

    async getEquipment(signal) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.GET(
          "/api/v1/profile/equipment",
          {
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(response, "Unable to load equipment.", error);
        }
        assertData(data, response);
        return mapEquipmentCollection(data, readEtag(response));
      });
    },

    async replaceEquipment(entries, { csrfToken, etag, signal }) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.PUT(
          "/api/v1/profile/equipment",
          {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "If-Match": etag ?? undefined,
              },
            },
            body: mapEquipmentInputsToRequest(entries),
            signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to replace equipment.",
            error,
          );
        }
        assertData(data, response);
        return mapEquipmentCollection(data, readEtag(response));
      });
    },

    async getCompleteness(signal) {
      return guarded(signal, async () => {
        const { data, error, response } = await client.GET(
          "/api/v1/profile/completeness",
          { signal, credentials: "include" },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to load profile completeness.",
            error,
          );
        }
        assertData(data, response);
        return mapCompleteness(data);
      });
    },
  };
}
