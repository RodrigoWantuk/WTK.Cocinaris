import { createCocinarisClient, type components } from "@cocinaris/api-client";
import type {
  ShoppingPlan,
  ShoppingPlanRepository,
  ShoppingPlanRoute,
  ShoppingPlanSetup,
  ShoppingPlanStart,
} from "@/contracts/shoppingPlan";
import { assertData, throwForFailure, wrapAbort } from "./liveRecipeRepository";

type PlanDto = components["schemas"]["ShoppingPlanView"];
type SetupDto = components["schemas"]["ShoppingPlanSetup"];
type StartDto = components["schemas"]["ShoppingPlanStartView"];

const num = (value: number | string) => Number(value);
const text = (value: unknown): string | null =>
  typeof value === "string" ? value : null;
const optionalNumber = (value: unknown): number | null =>
  value === null || value === undefined ? null : Number(value);

function mapRoute(
  dto: components["schemas"]["ShoppingPlanRouteView"],
): ShoppingPlanRoute {
  return {
    sessionId: String(dto.sessionId),
    menuId: String(dto.menuId),
    prepDate: text(dto.prepDate),
    totalMinutes: num(dto.totalMinutes),
    budgetMinutes: optionalNumber(dto.budgetMinutes),
    overBudget: dto.overBudget === true,
    recipes: (dto.recipes ?? []).map((recipe) => ({
      entryId: String(recipe.entryId),
      recipeId: String(recipe.recipeId),
      name: String(recipe.name),
      servings: num(recipe.servings),
      isComponent: recipe.isComponent === true,
      producesName: text(recipe.producesName),
      status: String(recipe.status),
      executionId: text(recipe.executionId),
      ingredients: (recipe.ingredients ?? []).map((item) => ({
        name: String(item.name),
        quantity: num(item.quantity),
        unit: String(item.unit),
        optional: item.optional === true,
      })),
    })),
    tasks: (dto.tasks ?? []).map((task) => ({
      taskKey: String(task.taskKey),
      entryId: String(task.entryId),
      kind: String(task.kind),
      name: String(task.name),
      instructions: String(task.instructions),
      sensoryCues: (task.sensoryCues ?? []).map(String),
      activeMinutes: num(task.activeMinutes),
      passiveMinutes: num(task.passiveMinutes),
      startMinute: num(task.startMinute),
      activeEndMinute: num(task.activeEndMinute),
      endMinute: num(task.endMinute),
      dependsOn: (task.dependsOn ?? []).map(String),
    })),
    progress: {
      done: (dto.progress?.done ?? []).map(String),
      skipped: (dto.progress?.skipped ?? []).map(String),
      timers: (dto.progress?.timers ?? []).map((timer) => ({
        taskKey: String(timer.taskKey),
        endsAt: String(timer.endsAt),
      })),
    },
  };
}

function mapSetup(dto: SetupDto): ShoppingPlanSetup {
  return {
    today: String(dto.today),
    days: num(dto.days),
    mealTypes: (dto.mealTypes ?? []).map(String),
    mealsOut: num(dto.mealsOut),
    cookingStyle: String(dto.cookingStyle),
    priority: String(dto.priority),
    prepDate: text(dto.prepDate),
    prepMinutes: optionalNumber(dto.prepMinutes),
    anchorIngredient: text(dto.anchorIngredient),
    people: num(dto.people),
  };
}

function mapStart(dto: StartDto): ShoppingPlanStart {
  return {
    lastSetup: dto.lastSetup ? mapSetup(dto.lastSetup as SetupDto) : null,
    people: num(dto.people),
    restrictions: (dto.restrictions ?? []).map((item) => ({
      code: String(item.code),
      category: String(item.category),
    })),
    unverifiableRestrictions: (dto.unverifiableRestrictions ?? []).map(String),
    hasFreezer: dto.hasFreezer === true,
    current: dto.current
      ? {
          sessionId: String(dto.current.sessionId),
          menuId: String(dto.current.menuId),
          status: String(dto.current.status),
        }
      : null,
    recent: (dto.recent ?? []).map((item) => ({
      menuId: String(item.menuId),
      name: String(item.name ?? ""),
      startDate: String(item.startDate),
      endDate: String(item.endDate),
      mealsTotal: num(item.mealsTotal),
      dishes: (item.dishes ?? []).map(String),
    })),
  };
}

function mapPlan(dto: PlanDto): ShoppingPlan {
  return {
    sessionId: String(dto.sessionId),
    menuId: String(dto.menuId),
    status: String(dto.status),
    setup: mapSetup(dto.setup as SetupDto),
    progress: {
      mealsTotal: num(dto.progress.mealsTotal),
      mealsFilled: num(dto.progress.mealsFilled),
      listItemCount: num(dto.progress.listItemCount),
      sessionMinutesUsed: num(dto.progress.sessionMinutesUsed),
      sessionMinutesBudget: optionalNumber(dto.progress.sessionMinutesBudget),
    },
    queue: (dto.queue ?? []).map((card) => ({
      cardId: String(card.cardId),
      source: String(card.source),
      name: String(card.name),
      summary: String(card.summary ?? ""),
      mealType: String(card.mealType),
      servings: num(card.servings),
      totalMinutes: num(card.totalMinutes),
      activeMinutes: num(card.activeMinutes),
      newItemCount: num(card.newItemCount),
      reusedNames: (card.reusedNames ?? []).map(String),
      reasonCode: text(card.reasonCode),
      reasonIngredient: text(card.reasonIngredient),
      overBudget: card.overBudget === true,
      guardCode: text(card.guardCode),
      guardIngredient: text(card.guardIngredient),
      leadIngredient: text(card.leadIngredient),
      component: card.component
        ? {
            outputName: String(card.component.outputName),
            outputQuantity: num(card.component.outputQuantity),
            outputUnit: String(card.component.outputUnit),
            usedBy: (card.component.usedBy ?? []).map(String),
          }
        : null,
      ingredients: (card.ingredients ?? []).map((item) => ({
        name: String(item.name),
        quantity: num(item.quantity),
        unit: String(item.unit),
        optional: item.optional === true,
      })),
    })),
    accepted: (dto.accepted ?? []).map((card) => ({
      cardId: String(card.cardId),
      name: String(card.name),
      mealType: String(card.mealType),
      repeats: num(card.repeats),
      mode: text(card.mode),
      expansionState: String(card.expansionState),
      failureCode: text(card.failureCode),
      recipeId: text(card.recipeId),
      guardCode: text(card.guardCode),
      guardIngredient: text(card.guardIngredient),
      isComponent: card.isComponent === true,
    })),
    needsBatch: dto.needsBatch === true,
    offerMoreOptions: dto.offerMoreOptions === true,
    exhausted: dto.exhausted === true,
    canUndo: dto.canUndo === true,
  };
}

/** Live shopping plan repository (PLAN-0062) over the generated API client. */
export function createLiveShoppingPlanRepository(options?: {
  baseUrl?: string;
  fetchImpl?: typeof fetch;
}): ShoppingPlanRepository {
  const fetchImpl =
    options?.fetchImpl ??
    ((input: RequestInfo | URL, init?: RequestInit) =>
      globalThis.fetch(input, init));
  const client = createCocinarisClient({
    baseUrl: options?.baseUrl ?? "",
    fetch: (request) => fetchImpl(request),
  });
  const include = { credentials: "include" as const };

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

  const headers = (language?: string) =>
    language ? { "Accept-Language": language } : undefined;

  return {
    getStart: (signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/shopping-plans/start", {
            signal,
            ...include,
          }),
        "Unable to load the plan's defaults.",
        mapStart,
      ),
    create: (input, { csrfToken, idempotencyKey, language, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/shopping-plans", {
            params: {
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            headers: headers(language),
            body: input,
            signal,
            ...include,
          }),
        "Unable to create the plan.",
        mapPlan,
      ),
    get: (sessionId, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/shopping-plans/{sessionId}", {
            params: { path: { sessionId } },
            signal,
            ...include,
          }),
        "Unable to load the plan.",
        mapPlan,
      ),
    addBatch: (
      sessionId,
      input,
      { csrfToken, idempotencyKey, language, signal },
    ) =>
      run(
        () =>
          client.POST("/api/v1/planning/shopping-plans/{sessionId}/batches", {
            params: {
              path: { sessionId },
              header: {
                "X-CSRF-TOKEN": csrfToken,
                "Idempotency-Key": idempotencyKey,
              },
            },
            headers: headers(language),
            body: {
              dishFamilies: input.dishFamilies ?? null,
              cuisine: input.cuisine ?? null,
              time: input.time ?? null,
            },
            signal,
            ...include,
          }),
        "Unable to find more dishes.",
        mapPlan,
      ),
    decide: (sessionId, cardId, input, { csrfToken, signal }) =>
      run(
        () =>
          client.POST(
            "/api/v1/planning/shopping-plans/{sessionId}/cards/{cardId}/decision",
            {
              params: {
                path: { sessionId, cardId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              body: {
                decision: input.decision,
                mode: input.mode ?? null,
                repeats: input.repeats ?? null,
              },
              signal,
              ...include,
            },
          ),
        "Unable to save the decision.",
        mapPlan,
      ),
    setReason: (sessionId, cardId, reason, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT(
            "/api/v1/planning/shopping-plans/{sessionId}/cards/{cardId}/reason",
            {
              params: {
                path: { sessionId, cardId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              body: { reason },
              signal,
              ...include,
            },
          ),
        "Unable to save the reason.",
        mapPlan,
      ),
    undo: (sessionId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/shopping-plans/{sessionId}/undo", {
            params: {
              path: { sessionId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to undo.",
        mapPlan,
      ),
    expand: (sessionId, cardId, { csrfToken, language, signal }) =>
      run(
        () =>
          client.POST(
            "/api/v1/planning/shopping-plans/{sessionId}/cards/{cardId}/expansion",
            {
              params: {
                path: { sessionId, cardId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              headers: headers(language),
              signal,
              ...include,
            },
          ),
        "Unable to prepare the recipe.",
        mapPlan,
      ),
    removeCard: (sessionId, cardId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST(
            "/api/v1/planning/shopping-plans/{sessionId}/cards/{cardId}/removal",
            {
              params: {
                path: { sessionId, cardId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              signal,
              ...include,
            },
          ),
        "Unable to remove the dish.",
        mapPlan,
      ),
    review: (sessionId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/shopping-plans/{sessionId}/review", {
            params: {
              path: { sessionId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to review the plan.",
        mapPlan,
      ),
    replace: (sessionId, entryId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST(
            "/api/v1/planning/shopping-plans/{sessionId}/replacements",
            {
              params: {
                path: { sessionId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              body: { entryId },
              signal,
              ...include,
            },
          ),
        "Unable to replace the dish.",
        mapPlan,
      ),
    accept: (sessionId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST(
            "/api/v1/planning/shopping-plans/{sessionId}/acceptance",
            {
              params: {
                path: { sessionId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              signal,
              ...include,
            },
          ),
        "Unable to use the plan.",
        mapPlan,
      ),
    abandon: (sessionId, { csrfToken, signal }) =>
      run(
        () =>
          client.POST("/api/v1/planning/shopping-plans/{sessionId}/abandon", {
            params: {
              path: { sessionId },
              header: { "X-CSRF-TOKEN": csrfToken },
            },
            signal,
            ...include,
          }),
        "Unable to discard the plan.",
        mapPlan,
      ),
    getRoute: (sessionId, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/shopping-plans/{sessionId}/route", {
            params: { path: { sessionId } },
            signal,
            ...include,
          }),
        "Unable to load the preparation session.",
        mapRoute,
      ),
    saveRouteProgress: (sessionId, progress, { csrfToken, signal }) =>
      run(
        () =>
          client.PUT(
            "/api/v1/planning/shopping-plans/{sessionId}/route/progress",
            {
              params: {
                path: { sessionId },
                header: { "X-CSRF-TOKEN": csrfToken },
              },
              body: progress,
              signal,
              ...include,
            },
          ),
        "Unable to save the preparation session.",
        () => undefined,
      ),
    findRouteLink: ({ menuId, date }, signal) =>
      run(
        () =>
          client.GET("/api/v1/planning/shopping-plans/route-link", {
            params: { query: { menuId, date } },
            signal,
            ...include,
          }),
        "Unable to find the preparation session.",
        (dto) => text(dto.sessionId),
      ),
  };
}
