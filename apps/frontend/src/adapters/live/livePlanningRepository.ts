import { createCocinarisClient, type components } from "@cocinaris/api-client";
import { RecipeApiError } from "@/contracts/recipes";
import type {
  Menu,
  MenuEntry,
  MenuSummary,
  MealReadiness,
  PlanningRepository,
  Reservations,
  ShoppingList,
} from "@/contracts/planning";
import { assertData, throwForFailure, wrapAbort } from "./liveRecipeRepository";

type MenuDto = components["schemas"]["MenuPlanResponse"];
type EntryDto = components["schemas"]["MenuEntryResponse"];
type SummaryDto = components["schemas"]["MenuPlanSummaryResponse"];
type ShoppingDto = components["schemas"]["ShoppingListResponse"];
type ReservationsDto = components["schemas"]["ReservationsResponse"];
type ReadinessDto = components["schemas"]["MealReadinessResponse"];

function mapReadiness(dto: ReadinessDto): MealReadiness {
  return {
    entryId: String(dto.entryId),
    readiness: String(dto.readiness),
    missing: (dto.missing ?? []).map((item) => ({
      name: String(item.name),
      unit: String(item.unit),
      quantity: num(item.quantity),
    })),
  };
}

function mapReservations(dto: ReservationsDto): Reservations {
  return {
    from: String(dto.from),
    lots: (dto.lots ?? []).map((lot) => ({
      lotId: String(lot.lotId),
      productName: String(lot.productName),
      unit: String(lot.unit),
      quantity: num(lot.quantity),
      protectedQuantity: num(lot.protectedQuantity),
      reservedQuantity: num(lot.reservedQuantity),
      freeQuantity: num(lot.freeQuantity),
      protection: lot.protection
        ? {
            kind: String(lot.protection.kind),
            minimumQuantity:
              lot.protection.minimumQuantity === null ||
              lot.protection.minimumQuantity === undefined
                ? null
                : num(lot.protection.minimumQuantity),
          }
        : null,
      reservations: (lot.reservations ?? []).map((item) => ({
        menuEntryId: String(item.menuEntryId),
        recipeName: String(item.recipeName),
        plannedDate: text(item.plannedDate),
        quantity: num(item.quantity),
      })),
    })),
    meals: (dto.meals ?? []).map(mapReadiness),
  };
}

const num = (value: number | string) => Number(value);
const text = (value: unknown): string | null =>
  typeof value === "string" ? value : null;

function mapEntry(dto: EntryDto): MenuEntry {
  return {
    entryId: String(dto.entryId),
    recipeId: String(dto.recipeId),
    recipeName: String(dto.recipeName),
    revisionNumber: num(dto.revisionNumber),
    revisionPinned: dto.revisionPinned === true,
    plannedDate: text(dto.plannedDate),
    mealType: text(dto.mealType),
    servings: num(dto.servings),
    status: String(dto.status),
    executionId: text(dto.executionId),
    reserveIngredients: dto.reserveIngredients !== false,
    readiness: dto.readiness ? mapReadiness(dto.readiness) : null,
  };
}

function mapMenu(dto: MenuDto): Menu {
  return {
    menuId: String(dto.menuId),
    name: String(dto.name ?? ""),
    startDate: String(dto.startDate),
    endDate: String(dto.endDate),
    status: String(dto.status),
    acceptedAt: text(dto.acceptedAt),
    updatedAt: String(dto.updatedAt),
    entries: (dto.entries ?? []).map(mapEntry),
  };
}

function mapSummary(dto: SummaryDto): MenuSummary {
  return {
    menuId: String(dto.menuId),
    name: String(dto.name ?? ""),
    startDate: String(dto.startDate),
    endDate: String(dto.endDate),
    status: String(dto.status),
    entryCount: num(dto.entryCount),
    updatedAt: String(dto.updatedAt),
  };
}

function mapShopping(dto: ShoppingDto): ShoppingList {
  return {
    menuId: String(dto.menuId),
    from: String(dto.from),
    mealCount: num(dto.mealCount),
    items: (dto.items ?? []).map((item) => ({
      itemKey: String(item.itemKey),
      displayName: String(item.displayName),
      unit: String(item.unit),
      category: String(item.category),
      requiredQuantity: num(item.requiredQuantity),
      availableQuantity: num(item.availableQuantity),
      toBuyQuantity: num(item.toBuyQuantity),
      checked: item.checked === true,
      uses: (item.uses ?? []).map((use) => ({
        entryId: String(use.entryId),
        recipeName: String(use.recipeName),
        plannedDate: text(use.plannedDate),
        quantity: num(use.quantity),
        optional: use.optional === true,
      })),
    })),
  };
}

/**
 * Live planning boundary over the generated OpenAPI client (PLAN-0029). Read-only toward the
 * pantry: the backend derives the shopping list and never reserves inventory from it.
 */
export function createLivePlanningRepository(options?: {
  fetchImpl?: typeof fetch;
  baseUrl?: string;
}): PlanningRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });

  // Every call shares the same failure mapping; `run` keeps each method to its request.
  async function run<T, R>(
    call: () => Promise<{ data?: T; error?: unknown; response: Response }>,
    fallback: string,
    map: (data: T) => R,
  ): Promise<R> {
    try {
      const { data, error, response } = await call();
      if (!response.ok || data === undefined) {
        await throwForFailure(response, fallback, error);
      }
      assertData(data, response);
      return map(data as T);
    } catch (err) {
      wrapAbort(err);
    }
  }

  const include = { credentials: "include" as const };

  return {
    listMenus: (signal, filter) =>
      run(
        () =>
          client.GET("/api/v1/planning/menus", {
            params: { query: { archived: filter?.archived || undefined } },
            signal,
            ...include,
          }),
        "Unable to list menus.",
        (data) => {
          if (!Array.isArray(data))
            throw new RecipeApiError("unexpected", "Malformed menu list.", 502);
          return data.map(mapSummary);
        },
      ),
    getMenu: (menuId, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/menus/{menuId}", {
            params: { path: { menuId } },
            signal,
            ...include,
          }),
        "Unable to load the menu.",
        mapMenu,
      ),
    createMenu: (input, { csrfToken, idempotencyKey, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/menus", {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            body: input,
            signal,
            ...include,
          }),
        "Unable to create the menu.",
        mapMenu,
      ),
    changeMenu: (menuId, input, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT("/api/v1/planning/menus/{menuId}", {
            params: {
              path: { menuId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: input,
            signal,
            ...include,
          }),
        "Unable to change the menu.",
        mapMenu,
      ),
    acceptMenu: (menuId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/menus/{menuId}/acceptance", {
            params: {
              path: { menuId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to accept the menu.",
        mapMenu,
      ),
    archiveMenu: (menuId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/menus/{menuId}/archive", {
            params: {
              path: { menuId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to archive the menu.",
        mapMenu,
      ),
    addEntry: (menuId, input, { csrfToken, idempotencyKey, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/menus/{menuId}/entries", {
            params: {
              path: { menuId },
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            // Pinning a revision is not offered yet; entries follow the recipe's current revision.
            body: { ...input, pinRevision: false },
            signal,
            ...include,
          }),
        "Unable to add the meal.",
        mapMenu,
      ),
    changeEntry: (menuId, entryId, input, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT("/api/v1/planning/menus/{menuId}/entries/{entryId}", {
            params: {
              path: { menuId, entryId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: input,
            signal,
            ...include,
          }),
        "Unable to change the meal.",
        mapMenu,
      ),
    removeEntry: (menuId, entryId, { csrfToken, signal }) =>
      run(
        () =>
          client.DELETE("/api/v1/planning/menus/{menuId}/entries/{entryId}", {
            params: {
              path: { menuId, entryId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to remove the meal.",
        mapMenu,
      ),
    getShopping: (menuId, from, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/menus/{menuId}/shopping", {
            params: { path: { menuId }, query: { from } },
            signal,
            ...include,
          }),
        "Unable to load the shopping list.",
        mapShopping,
      ),
    setChecked: (menuId, itemKey, checked, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT("/api/v1/planning/menus/{menuId}/shopping/checks", {
            params: {
              path: { menuId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: { itemKey, checked },
            signal,
            ...include,
          }),
        "Unable to update the shopping list.",
        () => undefined,
      ),
    getReservations: (from, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/reservations", {
            params: { query: { from } },
            signal,
            ...include,
          }),
        "Unable to load reservations.",
        mapReservations,
      ),
    protectLot: (lotId, protection, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT("/api/v1/planning/protections/{lotId}", {
            params: {
              path: { lotId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            body: protection,
            signal,
            ...include,
          }),
        "Unable to protect the item.",
        mapReservations,
      ),
    unprotectLot: (lotId, { csrfToken, signal }) =>
      run(
        () =>
          client.DELETE("/api/v1/planning/protections/{lotId}", {
            params: {
              path: { lotId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to remove the protection.",
        mapReservations,
      ),
    getToday: (date, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/today", {
            params: { query: { date } },
            signal,
            ...include,
          }),
        "Unable to load today's menu.",
        (data) =>
          (Array.isArray(data) ? data : []).map((item) => ({
            menuId: String(item.menuId),
            menuName: String(item.menuName ?? ""),
            entry: mapEntry(item.entry),
          })),
      ),
  };
}

/** Prototype stand-in: planning is not available outside the live runtime. */
export function createUnavailablePlanningRepository(): PlanningRepository {
  const fail = async (): Promise<never> => {
    throw new RecipeApiError(
      "unavailable",
      "Menu planning is not available in this runtime.",
      503,
    );
  };
  return {
    listMenus: fail,
    getMenu: fail,
    createMenu: fail,
    changeMenu: fail,
    acceptMenu: fail,
    archiveMenu: fail,
    addEntry: fail,
    changeEntry: fail,
    removeEntry: fail,
    getShopping: fail,
    setChecked: fail,
    getReservations: fail,
    protectLot: fail,
    unprotectLot: fail,
    getToday: fail,
  };
}
