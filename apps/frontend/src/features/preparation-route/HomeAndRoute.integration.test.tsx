import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";
import { PreparationRouteProvider } from "./PreparationRouteProvider";
import { HomeRouteCarousel } from "./HomeRouteCarousel";
import { RouteChainView } from "./RouteChainView";
import {
  MockPreparationRouteRepository,
  MOCK_PREPARATION_ROUTE_TASKS,
} from "../../adapters/mock/preparationRouteRepository";
import { StoreProvider } from "../../lib/store";
import type { PreparationRouteTask } from "../../contracts/preparation";
import { isCookTargetReady } from "./cookTargetReady";

function tr(key: string): string {
  return key;
}

function LocationProbe() {
  const loc = useLocation();
  return (
    <div data-testid="location-probe">
      {loc.pathname}?{loc.search.replace(/^\?/, "")}
    </div>
  );
}

function renderHomeAndChain(repo: MockPreparationRouteRepository) {
  return render(
    <StoreProvider enablePrototypeFixtures persistPrototypeAuth>
      <MemoryRouter initialEntries={["/app/hoje"]}>
        <PreparationRouteProvider repository={repo}>
          <HomeRouteCarousel tr={tr} />
          <RouteChainView />
          <Routes>
            <Route path="/app/hoje" element={<LocationProbe />} />
            <Route path="/app/cozinhar/:id" element={<LocationProbe />} />
            <Route path="*" element={<LocationProbe />} />
          </Routes>
        </PreparationRouteProvider>
      </MemoryRouter>
    </StoreProvider>,
  );
}

describe("Home + RouteChain integrated preparation route", () => {
  it("completing on Home updates the full route and unlocks the next dependency", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    renderHomeAndChain(repo);

    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-state",
      "blocked",
    );
    expect(screen.getByTestId("chain-item-n3")).toHaveAttribute(
      "data-state",
      "blocked",
    );

    await user.click(screen.getByTestId("home-route-start-n2"));
    expect(repo.getInProgressIds().has("n2")).toBe(true);
    expect(screen.getByTestId("home-route-card-n2")).toHaveAttribute(
      "data-state",
      "inProgress",
    );

    await user.click(screen.getByTestId("home-route-done-n2"));
    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-state",
      "canStart",
    );
    expect(screen.getByTestId("chain-item-n3")).toHaveAttribute(
      "data-state",
      "canStart",
    );
  });

  it("completing on the full route updates Home", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    renderHomeAndChain(repo);

    await user.click(screen.getByTestId("chain-toggle-n2"));
    expect(repo.getCompletedIds().has("n2")).toBe(true);
    expect(screen.getByTestId("home-route-card-n2")).toHaveAttribute(
      "data-state",
      "done",
    );
    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-state",
      "canStart",
    );
  });

  it("blocked tasks cannot be completed via the chain toggle", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    renderHomeAndChain(repo);

    await user.click(screen.getByTestId("chain-toggle-n3"));
    expect(repo.getCompletedIds().has("n3")).toBe(false);
    expect(screen.getByTestId("chain-item-n3")).toHaveAttribute(
      "data-state",
      "blocked",
    );
  });

  it("releases Cook CTA for the correct target and does not duplicate cook buttons on Home", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    renderHomeAndChain(repo);

    await user.click(screen.getByTestId("chain-toggle-n2"));
    await user.click(screen.getByTestId("chain-toggle-n3"));

    expect(screen.getByTestId("home-route-cook-ready-r3")).toBeInTheDocument();
    expect(screen.getByTestId("home-route-cook-now-r3")).toBeInTheDocument();
    expect(
      screen.queryByTestId("home-route-card-cook-r3"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("chain-cook-start-r3")).toBeInTheDocument();
    expect(screen.queryByTestId("chain-item-cook-r3")).not.toBeInTheDocument();
    expect(screen.getByTestId("chain-item-unlocked-r3")).toBeInTheDocument();
  });

  it("Later dismisses CTA but keeps completion", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    repo.replaceCompletedForTests(["n1", "n2", "n3"]);
    renderHomeAndChain(repo);

    expect(screen.getByTestId("home-route-cook-now-r3")).toBeInTheDocument();
    await user.click(screen.getByTestId("home-route-cook-later-r3"));
    expect(
      screen.queryByTestId("home-route-cook-now-r3"),
    ).not.toBeInTheDocument();
    expect(repo.getCompletedIds().has("n3")).toBe(true);
    expect(repo.getDismissedTargetIds().has("r3")).toBe(true);
  });

  it("navigates to cook with sourcePreparationRouteId and relatedPlanEntryId", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    repo.replaceCompletedForTests(["n1", "n2", "n3"]);
    renderHomeAndChain(repo);

    await user.click(screen.getByTestId("home-route-cook-now-r3"));
    const probe = screen.getByTestId("location-probe");
    expect(probe.textContent).toContain("/app/cozinhar/r3");
    expect(probe.textContent).toContain(
      `sourcePreparationRouteId=${repo.getRouteId()}`,
    );
    expect(probe.textContent).toContain("relatedPlanEntryId=pl-route-r3");
  });

  it("optional tasks do not block cook readiness", () => {
    const tasks = MOCK_PREPARATION_ROUTE_TASKS.map((t) => ({
      ...t,
      state:
        t.id === "n1" || t.id === "n2" || t.id === "n3"
          ? "done"
          : t.baselineState,
    }));
    expect(isCookTargetReady(tasks, "r3")).toBe(true);
    // n4/n5 optional remain incomplete
    expect(tasks.find((t) => t.id === "n4")?.requiredForTarget).toBe(false);
  });

  it("supports independent cook targets without waiting on another recipe", () => {
    const tasks: PreparationRouteTask[] = [
      {
        id: "a1",
        groupKey: "g",
        time: "09:00",
        task: "Prep A",
        forTitle: "Recipe A",
        targetRecipeId: "ra",
        relatedPlanEntryId: "pl-a",
        produces: null,
        activeMin: 5,
        passiveMin: 0,
        dependsOn: null,
        baselineState: "done",
        requiredForTarget: true,
      },
      {
        id: "b1",
        groupKey: "g",
        time: "10:00",
        task: "Prep B",
        forTitle: "Recipe B",
        targetRecipeId: "rb",
        relatedPlanEntryId: "pl-b",
        produces: null,
        activeMin: 5,
        passiveMin: 0,
        dependsOn: null,
        baselineState: "canStart",
        requiredForTarget: true,
      },
    ];
    const repo = new MockPreparationRouteRepository(tasks, "multi-route");
    repo.replaceCompletedForTests(["a1"]);
    const snap = repo.getProjectionSnapshot();
    expect(isCookTargetReady(snap.tasks, "ra")).toBe(true);
    expect(isCookTargetReady(snap.tasks, "rb")).toBe(false);
    expect(
      snap.readyTargets.some((t) => t.targetRecipeId === "ra" && !t.dismissed),
    ).toBe(true);
    expect(snap.readyTargets.some((t) => t.targetRecipeId === "rb")).toBe(
      false,
    );
  });
});

describe("cook handoff with real MemoryRouter", () => {
  it("Cook now navigates; Later does not", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    repo.replaceCompletedForTests(["n1", "n2", "n3"]);
    render(
      <StoreProvider enablePrototypeFixtures persistPrototypeAuth>
        <MemoryRouter initialEntries={["/app/hoje"]}>
          <PreparationRouteProvider repository={repo}>
            <HomeRouteCarousel tr={tr} />
            <Routes>
              <Route path="/app/hoje" element={<LocationProbe />} />
              <Route path="/app/cozinhar/:id" element={<LocationProbe />} />
            </Routes>
          </PreparationRouteProvider>
        </MemoryRouter>
      </StoreProvider>,
    );

    await user.click(screen.getByTestId("home-route-cook-later-r3"));
    expect(screen.getByTestId("location-probe").textContent).toContain(
      "/app/hoje",
    );
    expect(repo.getCompletedIds().has("n3")).toBe(true);
    expect(
      screen.queryByTestId("home-route-cook-now-r3"),
    ).not.toBeInTheDocument();
  });
});
