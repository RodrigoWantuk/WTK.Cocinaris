import {
  createCocinarisClient,
  readEtag,
  readProblemDetails,
  type components,
} from "@cocinaris/api-client";
import {
  InventoryApiError,
  type AdjustLotInput,
  type InventoryHistoryEntry,
  type InventoryListPage,
  type InventoryLotView,
  type InventoryQuantity,
  type InventoryRepository,
  type TransitionLotInput,
  type UpdateLotInput,
} from "./inventoryTypes";

type LotDto = components["schemas"]["LotResponse"];
type QuantityDto = components["schemas"]["QuantityResponse"];

const MEASURED_UNITS = new Set(["Gram", "Milliliter", "Unit"]);
const AVAILABILITY = new Set(["Available", "Low", "Unavailable"]);

/**
 * Maps a wire quantity with fail-closed mutual-exclusion checks.
 * Never invents an authoritative availability default.
 */
export function mapQuantity(q: QuantityDto): InventoryQuantity {
  const hasMeasured = q.measuredValue != null && q.measuredValue !== undefined;
  const hasUnit = Boolean(q.unit);
  const hasAvailability = Boolean(q.availabilityState);

  if (hasMeasured || hasUnit) {
    if (!hasMeasured || !hasUnit || hasAvailability) {
      throw new InventoryApiError(
        "unexpected",
        "Malformed measured quantity projection.",
        502,
      );
    }
    if (!MEASURED_UNITS.has(String(q.unit))) {
      throw new InventoryApiError(
        "unexpected",
        "Unsupported measured unit in projection.",
        502,
      );
    }
    const value = Number(q.measuredValue);
    if (!Number.isFinite(value)) {
      throw new InventoryApiError(
        "unexpected",
        "Non-finite measured value in projection.",
        502,
      );
    }
    return {
      kind: "measured",
      value,
      unit: q.unit as "Gram" | "Milliliter" | "Unit",
    };
  }

  if (!hasAvailability || !AVAILABILITY.has(String(q.availabilityState))) {
    throw new InventoryApiError(
      "unexpected",
      "Malformed qualitative quantity projection.",
      502,
    );
  }
  return {
    kind: "qualitative",
    availability: q.availabilityState as "Available" | "Low" | "Unavailable",
  };
}

function mapLot(dto: LotDto, etag: string | null): InventoryLotView {
  return {
    lotId: dto.lotId,
    productId: dto.productId,
    productName: dto.productName,
    quantity: mapQuantity(dto.quantity),
    storageLocation: dto.storageLocation,
    customLocation: dto.customLocation ?? null,
    packageState: dto.packageState ?? null,
    printedExpirationDate: dto.printedExpirationDate ?? null,
    notes: dto.notes ?? null,
    version: dto.version,
    etag: etag ?? dto.version,
    createdAt: dto.createdAt,
    updatedAt: dto.updatedAt,
    // Frontend and backend deploy independently: an API from before PLAN-0037 sends no
    // lifecycle, and every lot it knows was in the AsAcquired state.
    lifecycle: {
      state: dto.lifecycle?.state ?? "AsAcquired",
      openedAt: dto.lifecycle?.openedAt ?? null,
      frozenAt: dto.lifecycle?.frozenAt ?? null,
      thawStartedAt: dto.lifecycle?.thawStartedAt ?? null,
      thawedAt: dto.lifecycle?.thawedAt ?? null,
      parentLotId: dto.lifecycle?.parentLotId ?? null,
    },
  };
}

function toQuantityRequest(
  quantity: InventoryQuantity,
): components["schemas"]["QuantityRequest"] {
  if (quantity.kind === "measured") {
    return {
      measuredValue: quantity.value,
      unit: quantity.unit,
      availabilityState: null,
    };
  }
  return {
    measuredValue: null,
    unit: null,
    availabilityState: quantity.availability,
  };
}

function assertData<T>(
  data: T | undefined,
  response: Response,
): asserts data is T {
  if (!response.ok || data === undefined) {
    throw new InventoryApiError(
      "unexpected",
      "Unexpected empty success payload.",
      response.status,
    );
  }
}

async function throwForFailure(
  response: Response,
  fallback: string,
  /** The generated client's parsed `error` body; the response body is already consumed. */
  parsedBody?: unknown,
): Promise<never> {
  if (response.status === 401) {
    throw new InventoryApiError("authentication_required", fallback, 401);
  }
  if (response.status === 412) {
    throw new InventoryApiError(
      "precondition_failed",
      "The inventory lot changed. Reload before retrying.",
      412,
    );
  }
  if (response.status === 428) {
    throw new InventoryApiError(
      "precondition_required",
      "A current version (If-Match) is required.",
      428,
    );
  }
  if (response.status === 404) {
    throw new InventoryApiError("not_found", "Lot not found.", 404);
  }
  if (response.status === 409) {
    throw new InventoryApiError("conflict", fallback, 409);
  }
  if (response.status >= 500 || response.status === 0) {
    throw new InventoryApiError(
      "unavailable",
      fallback,
      response.status || 503,
    );
  }
  const problem = await readProblemDetails(response, parsedBody);
  const fieldErrors: Record<string, string[]> = {};
  if (problem?.errors) {
    for (const [key, value] of Object.entries(problem.errors)) {
      if (value) fieldErrors[key] = value;
    }
  }
  throw new InventoryApiError(
    "validation_failed",
    problem?.detail ?? problem?.title ?? fallback,
    response.status,
    fieldErrors,
  );
}

/**
 * Live inventory boundary over the generated OpenAPI client.
 * Performs no authoritative arithmetic; backend owns lifecycle transitions.
 */
export function createLiveInventoryRepository(options?: {
  fetchImpl?: typeof fetch;
  baseUrl?: string;
}): InventoryRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });

  return {
    async listLots(params = {}) {
      try {
        const { data, error, response } = await client.GET(
          "/api/v1/inventory/lots",
          {
            params: {
              query: {
                search: params.search,
                status: params.status,
                storageLocation: params.storageLocation,
                cursor: params.cursor,
                pageSize: params.pageSize,
              },
            },
            signal: params.signal,
            credentials: "include",
          },
        );
        if (!response.ok || !data) {
          await throwForFailure(
            response,
            "Unable to list inventory lots.",
            error,
          );
        }
        assertData(data, response);
        return {
          items: data.items.map((item) => mapLot(item, item.version)),
          nextCursor: data.nextCursor ?? null,
        } satisfies InventoryListPage;
      } catch (err) {
        if (err instanceof InventoryApiError) throw err;
        if (err instanceof DOMException && err.name === "AbortError") {
          throw new InventoryApiError("cancelled", "Request cancelled.", 0);
        }
        throw new InventoryApiError(
          "unavailable",
          "Unable to list inventory lots.",
          503,
        );
      }
    },

    async getLot(lotId, signal) {
      const { data, error, response } = await client.GET(
        "/api/v1/inventory/lots/{lotId}",
        {
          params: { path: { lotId } },
          signal,
          credentials: "include",
        },
      );
      if (!response.ok || !data) {
        await throwForFailure(response, "Unable to load lot.", error);
      }
      assertData(data, response);
      return mapLot(data, readEtag(response) ?? data.version);
    },

    async createLot(input, { csrfToken, idempotencyKey, signal }) {
      const body: components["schemas"]["CreateLotRequest"] = {
        productName: input.productName,
        quantity: toQuantityRequest(input.quantity),
        storageLocation: input.storageLocation,
        customLocation: input.customLocation ?? null,
        packageState: input.packageState ?? null,
        printedExpirationDate: input.printedExpirationDate ?? null,
        notes: input.notes ?? null,
      };
      const { data, error, response } = await client.POST(
        "/api/v1/inventory/lots",
        {
          params: {
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
        await throwForFailure(response, "Unable to create lot.", error);
      }
      assertData(data, response);
      return mapLot(data, readEtag(response) ?? data.version);
    },

    async updateLot(lotId, input: UpdateLotInput, { csrfToken, etag, signal }) {
      if (!input.storageLocation) {
        throw new InventoryApiError(
          "validation_failed",
          "storageLocation is required for updates.",
          400,
        );
      }
      const body: components["schemas"]["UpdateLotRequest"] = {
        productName: input.productName ?? null,
        storageLocation: input.storageLocation,
        customLocation: input.customLocation ?? null,
        packageState: input.packageState ?? null,
        printedExpirationDate: input.printedExpirationDate ?? null,
        notes: input.notes ?? null,
      };
      const { data, error, response } = await client.PATCH(
        "/api/v1/inventory/lots/{lotId}",
        {
          params: {
            path: { lotId },
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "If-Match": etag,
            },
          },
          body,
          signal,
          credentials: "include",
        },
      );
      if (!response.ok || !data) {
        await throwForFailure(response, "Unable to update lot.", error);
      }
      assertData(data, response);
      return mapLot(data, readEtag(response) ?? data.version);
    },

    async adjustLot(
      lotId,
      input: AdjustLotInput,
      { csrfToken, etag, idempotencyKey, signal },
    ) {
      const body: components["schemas"]["AdjustmentRequest"] = {
        type: input.type,
        value: input.value ?? null,
        availabilityState: input.availabilityState ?? null,
        reasonCode: input.reasonCode ?? null,
        note: input.note ?? null,
      };
      const { data, error, response } = await client.POST(
        "/api/v1/inventory/lots/{lotId}/adjustments",
        {
          params: {
            path: { lotId },
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "If-Match": etag,
              "Idempotency-Key": idempotencyKey,
            },
          },
          body,
          signal,
          credentials: "include",
        },
      );
      if (!response.ok || !data) {
        await throwForFailure(response, "Unable to adjust lot.", error);
      }
      assertData(data, response);
      return mapLot(data, readEtag(response) ?? data.version);
    },

    async transitionLot(
      lotId,
      input: TransitionLotInput,
      { csrfToken, etag, idempotencyKey, signal },
    ) {
      const partial = input.partialValue != null;
      const body: components["schemas"]["TransitionRequest"] = {
        kind: input.kind,
        quantity: partial
          ? {
              measuredValue: input.partialValue!,
              unit: input.unit ?? null,
              availabilityState: null,
            }
          : null,
        targetStorageLocation: input.targetStorageLocation ?? null,
        targetCustomLocation: input.targetCustomLocation ?? null,
      };
      const { data, error, response } = await client.POST(
        "/api/v1/inventory/lots/{lotId}/transitions",
        {
          params: {
            path: { lotId },
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "If-Match": etag,
              "Idempotency-Key": idempotencyKey,
            },
          },
          body,
          signal,
          credentials: "include",
        },
      );
      if (!response.ok || !data) {
        await throwForFailure(response, "Unable to change the lot.", error);
      }
      assertData(data, response);
      return mapLot(data, readEtag(response) ?? data.version);
    },

    async deleteLot(lotId, { csrfToken, etag, signal }) {
      const { error, response } = await client.DELETE(
        "/api/v1/inventory/lots/{lotId}",
        {
          params: {
            path: { lotId },
            header: {
              "X-CSRF-TOKEN": csrfToken,
              "If-Match": etag,
            },
          },
          signal,
          credentials: "include",
        },
      );
      if (!response.ok && response.status !== 204) {
        await throwForFailure(response, "Unable to delete lot.", error);
      }
    },

    async getHistory(lotId, signal) {
      const { data, error, response } = await client.GET(
        "/api/v1/inventory/lots/{lotId}/history",
        {
          params: { path: { lotId } },
          signal,
          credentials: "include",
        },
      );
      if (!response.ok || !data) {
        await throwForFailure(response, "Unable to load history.", error);
      }
      assertData(data, response);
      return data.map(
        (entry): InventoryHistoryEntry => ({
          entryId: entry.entryId,
          kind: entry.kind,
          type: entry.type ?? null,
          reasonCode: entry.reasonCode ?? null,
          changedFields: entry.changedFields ?? null,
          occurredAt: entry.occurredAt,
          transition: entry.transition
            ? {
                kind: entry.transition.kind,
                previousState: entry.transition.previousState,
                resultingState: entry.transition.resultingState,
                previousStorageLocation:
                  entry.transition.previousStorageLocation,
                resultingStorageLocation:
                  entry.transition.resultingStorageLocation,
                relatedLotId: entry.transition.relatedLotId ?? null,
              }
            : null,
        }),
      );
    },
  };
}
