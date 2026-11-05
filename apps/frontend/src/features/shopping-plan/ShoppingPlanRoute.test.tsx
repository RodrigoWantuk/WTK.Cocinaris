import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { createUnavailableRecipeRepository } from "@/adapters/live/unavailableRecipeRepository";
import { RecipeApiError, type RecipeRepository } from "@/contracts/recipes";
import type {
  ShoppingPlanRepository,
  ShoppingPlanRoute,
} from "@/contracts/shoppingPlan";
import { RecipeProvider } from "@/features/recipes/RecipeProvider";
import { createSessionAdapter } from "@/features/inventory/testUtils";
import { ShoppingPlanProvider } from "./ShoppingPlanProvider";
import { ShoppingPlanRoutePage } from "./ShoppingPlanRoutePage";
import { SessionRouteLink } from "./SessionRouteLink";

const task = (
  taskKey: string,
  entryId: string,
  start: number,
  active: number,
  passive: number,
  dependsOn: string[] = [],
) => ({
  taskKey,
  entryId,
  kind: "stage",
  name: `Tarefa ${taskKey}`,
  instructions: `Faça ${taskKey}`,
  sensoryCues: [],
  activeMinutes: active,
  passiveMinutes: passive,
  startMinute: start,
  activeEndMinute: start + active,
  endMinute: start + active + passive,
  dependsOn,
});

const recipe = (entryId: string, name: string, status = "planned") => ({
  entryId,
  recipeId: `r-${entryId}`,
  name,
  servings: 4,
  isComponent: false,
  producesName: null,
  status,
  executionId: status === "planned" ? null : `x-${entryId}`,
  ingredients: [{ name: "Feijão", quantity: 200, unit: "g", optional: false }],
});

const ROUTE: ShoppingPlanRoute = {
  sessionId: "s1",
  menuId: "m1",
  prepDate: "2026-11-05",
  totalMinutes: 70,
  budgetMinutes: 60,
  overBudget: true,
  recipes: [recipe("a", "Feijoada"), recipe("b", "Salada")],
  tasks: [
    task("a1", "a", 0, 10, 60),
    task("b1", "b", 10, 20, 0),
    task("a2", "a", 70, 5, 0, ["a1"]),
  ],
  progress: { done: [], skipped: [], timers: [] },
};

function repository(
  route: ShoppingPlanRoute = ROUTE,
  overrides: Partial<ShoppingPlanRepository> = {},
) {
  return {
    getRoute: vi.fn(async () => route),
    saveRouteProgress: vi.fn(async () => undefined),
    findRouteLink: vi.fn(async () => null),
    ...overrides,
  } as unknown as ShoppingPlanRepository;
}

function recipesRepo(
  startExecution = vi.fn(async () => ({ executionId: "x-a" })),
) {
  return {
    ...createUnavailableRecipeRepository(),
    startExecution,
  } as unknown as RecipeRepository;
}

function renderRoute(
  repo: ShoppingPlanRepository,
  recipes: RecipeRepository = recipesRepo(),
) {
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={createSessionAdapter()}>
        <RecipeProvider repository={recipes}>
          <ShoppingPlanProvider repository={repo}>
            <MemoryRouter initialEntries={["/app/compras/planejar/s1/preparo"]}>
              <Routes>
                <Route
                  path="/app/compras/planejar/:sessionId/preparo"
                  element={<ShoppingPlanRoutePage />}
                />
                <Route
                  path="/app/compras/planejar/:sessionId"
                  element={<p>quadro</p>}
                />
                <Route
                  path="/app/receitas/cozinhar/:executionId"
                  element={<p>tela de concluir</p>}
                />
              </Routes>
            </MemoryRouter>
          </ShoppingPlanProvider>
        </RecipeProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

afterEach(() => vi.useRealTimers());

describe("ShoppingPlanRoutePage", () => {
  it("shows the whole route and warns that it passes the time set aside", async () => {
    renderRoute(repository());

    await screen.findByTestId("plan-route-overview");
    expect(screen.getAllByTestId("plan-route-overview-task")).toHaveLength(3);
    expect(screen.getByTestId("plan-route-over-budget").textContent).toContain(
      "70 min",
    );
    expect(screen.getByText(/bocas e o forno/)).toBeTruthy();
  });

  it("starts the dish's cooking session on its first task, once", async () => {
    const start = vi.fn(async () => ({ executionId: "x-a" }));
    const repo = repository();
    renderRoute(repo, recipesRepo(start));

    fireEvent.click(await screen.findByTestId("plan-route-start"));
    await screen.findByTestId("plan-route-task");

    await waitFor(() => expect(start).toHaveBeenCalledTimes(1));
    expect(start).toHaveBeenCalledWith(
      "r-a",
      expect.objectContaining({ menuEntryId: "a", servings: 4 }),
    );
  });

  it("leaves a passive part running and says what to do meanwhile", async () => {
    const repo = repository();
    renderRoute(repo);

    fireEvent.click(await screen.findByTestId("plan-route-start"));
    expect(
      (await screen.findByTestId("plan-route-next")).textContent,
    ).toContain("Enquanto isso: Tarefa b1");
    fireEvent.click(screen.getByTestId("plan-route-done"));

    expect(screen.getByTestId("plan-route-task-name").textContent).toBe(
      "Tarefa b1",
    );
    expect(screen.getByTestId("plan-route-timer").textContent).toContain(
      "Tarefa a1",
    );
    await waitFor(() =>
      expect(repo.saveRouteProgress).toHaveBeenCalledWith(
        "s1",
        expect.objectContaining({
          done: ["a1"],
          timers: [expect.objectContaining({ taskKey: "a1" })],
        }),
        expect.anything(),
      ),
    );
  });

  it("names the wait before a task that depends on it, without blocking", async () => {
    const endsAt = new Date(Date.now() + 30 * 60_000).toISOString();
    renderRoute(
      repository({
        ...ROUTE,
        progress: {
          done: ["a1", "b1"],
          skipped: [],
          timers: [{ taskKey: "a1", endsAt }],
        },
      }),
    );

    expect(
      (await screen.findByTestId("plan-route-waiting")).textContent,
    ).toContain("Tarefa a1");
    expect(
      (screen.getByTestId("plan-route-done") as HTMLButtonElement).disabled,
    ).toBe(false);
  });

  it("resumes where the person stopped after a reload", async () => {
    renderRoute(
      repository({
        ...ROUTE,
        progress: { done: ["a1"], skipped: [], timers: [] },
      }),
    );

    expect(
      (await screen.findByTestId("plan-route-task-name")).textContent,
    ).toBe("Tarefa b1");
    expect(screen.queryByTestId("plan-route-overview")).toBeNull();
  });

  it("can skip a task and go back to it", async () => {
    const repo = repository();
    renderRoute(repo);

    fireEvent.click(await screen.findByTestId("plan-route-start"));
    fireEvent.click(await screen.findByTestId("plan-route-skip"));
    expect(screen.getByTestId("plan-route-task-name").textContent).toBe(
      "Tarefa b1",
    );

    fireEvent.click(screen.getByTestId("plan-route-previous"));
    expect(screen.getByTestId("plan-route-task-name").textContent).toBe(
      "Tarefa a1",
    );
    await waitFor(() =>
      expect(repo.saveRouteProgress).toHaveBeenLastCalledWith(
        "s1",
        { done: [], skipped: [], timers: [] },
        expect.anything(),
      ),
    );
  });

  it("restores a running timer after a reload and alarms once when it ends", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const endsAt = new Date(Date.now() + 2000).toISOString();
    renderRoute(
      repository({
        ...ROUTE,
        progress: {
          done: ["a1"],
          skipped: [],
          timers: [{ taskKey: "a1", endsAt }],
        },
      }),
    );
    await screen.findByTestId("plan-route-timer");
    expect(screen.queryByTestId("plan-route-timer-dismiss")).toBeNull();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000);
    });

    expect(screen.getByTestId("plan-route-timer-dismiss")).toBeTruthy();
    expect(screen.getByText("Tarefa a1: tempo acabou")).toBeTruthy();
  });

  it("finishes each dish in turn on its cooking screen, in route order", async () => {
    const start = vi.fn(async () => ({ executionId: "x-a" }));
    renderRoute(
      repository({
        ...ROUTE,
        recipes: [recipe("a", "Feijoada", "cooked"), recipe("b", "Salada")],
        progress: { done: ["a1", "a2", "b1"], skipped: [], timers: [] },
      }),
      recipesRepo(start),
    );

    const next = await screen.findByTestId("plan-route-finish-next");
    expect(next.textContent).toContain("Concluir Salada");
    fireEvent.click(next);

    await screen.findByText("tela de concluir");
    expect(start).toHaveBeenCalledWith(
      "r-b",
      expect.objectContaining({ menuEntryId: "b" }),
    );
  });

  it("says the session is over when every dish is cooked", async () => {
    renderRoute(
      repository({
        ...ROUTE,
        recipes: [
          recipe("a", "Feijoada", "cooked"),
          recipe("b", "Salada", "cooked"),
        ],
        progress: { done: ["a1", "a2", "b1"], skipped: [], timers: [] },
      }),
    );

    await screen.findByTestId("plan-route-complete");
    expect(screen.queryByTestId("plan-route-finish-next")).toBeNull();
  });

  it("explains an empty route", async () => {
    renderRoute(repository({ ...ROUTE, recipes: [], tasks: [] }));

    expect(
      (await screen.findByTestId("plan-route-empty")).textContent,
    ).toContain("Nenhum prato");
  });

  it("sends a menu not yet accepted back to its board", async () => {
    const repo = repository(ROUTE, {
      getRoute: vi.fn(async () => {
        throw new RecipeApiError("domain_rule_violated", "x", 422);
      }),
    });
    renderRoute(repo);

    await screen.findByText("quadro");
  });
});

describe("SessionRouteLink", () => {
  const renderLink = (repo?: ShoppingPlanRepository) => {
    const link = (
      <MemoryRouter>
        <SessionRouteLink query={{ menuId: "m1" }} />
      </MemoryRouter>
    );
    return render(
      <ProductionI18nProvider initialLocale="pt-BR">
        {repo ? (
          <ShoppingPlanProvider repository={repo}>{link}</ShoppingPlanProvider>
        ) : (
          link
        )}
      </ProductionI18nProvider>,
    );
  };

  it("opens the session when there is one", async () => {
    renderLink(repository(ROUTE, { findRouteLink: vi.fn(async () => "s1") }));

    expect(
      (await screen.findByTestId("session-route-link")).getAttribute("href"),
    ).toBe("/app/compras/planejar/s1/preparo");
  });

  it("shows nothing without a session, on failure, or outside the provider", async () => {
    const failing = vi.fn(async () => {
      throw new Error("down");
    });
    const { unmount } = renderLink(
      repository(ROUTE, { findRouteLink: failing }),
    );
    await waitFor(() => expect(failing).toHaveBeenCalled());
    expect(screen.queryByTestId("session-route-link")).toBeNull();
    unmount();

    renderLink();
    expect(screen.queryByTestId("session-route-link")).toBeNull();
  });
});
