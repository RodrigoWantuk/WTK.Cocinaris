import { render, screen, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  PreparationRouteProvider,
  usePreparationRoute,
} from "./PreparationRouteProvider";
import { MockPreparationRouteRepository } from "../../adapters/mock/preparationRouteRepository";
import { HomeRouteCarousel } from "./HomeRouteCarousel";
import { StoreProvider } from "../../lib/store";

vi.mock("react-router-dom", () => ({
  MemoryRouter: ({ children }: { children: React.ReactNode }) => children,
  useNavigate: () => vi.fn(),
  Link: ({ children }: { children: React.ReactNode }) => children,
  NavLink: ({ children }: { children: React.ReactNode }) => children,
  useLocation: () => ({ pathname: "/app/hoje" }),
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
}));

function SnapshotProbe({ label }: { label: string }) {
  const { projection } = usePreparationRoute();
  return (
    <div data-testid={`probe-${label}`}>
      {projection.highlightedTaskId ?? "none"}|
      {projection.tasks.map((t) => t.state).join(",")}
    </div>
  );
}

function tr(key: string): string {
  return key;
}

function wrap(ui: React.ReactElement, repo: MockPreparationRouteRepository) {
  return (
    <StoreProvider enablePrototypeFixtures persistPrototypeAuth>
      <PreparationRouteProvider repository={repo}>
        {ui}
      </PreparationRouteProvider>
    </StoreProvider>
  );
}

describe("PreparationRouteProvider external store", () => {
  it("mounts without getSnapshot cache warning and shares one snapshot", () => {
    const repo = new MockPreparationRouteRepository();
    const errors: string[] = [];
    const spy = vi
      .spyOn(console, "error")
      .mockImplementation((...args: unknown[]) => {
        errors.push(String(args[0]));
      });

    render(
      wrap(
        <>
          <SnapshotProbe label="home" />
          <SnapshotProbe label="plan" />
        </>,
        repo,
      ),
    );

    expect(screen.getByTestId("probe-home").textContent).toBe(
      screen.getByTestId("probe-plan").textContent,
    );
    expect(
      errors.some((e) => e.includes("getSnapshot") || e.includes("cached")),
    ).toBe(false);
    spy.mockRestore();
  });

  it("keeps getProjectionSnapshot referentially stable until mutation", () => {
    const repo = new MockPreparationRouteRepository();
    const a = repo.getProjectionSnapshot();
    const b = repo.getProjectionSnapshot();
    expect(a).toBe(b);
    repo.markInProgress("n2");
    const c = repo.getProjectionSnapshot();
    expect(c).not.toBe(a);
  });

  it("completing a task on Home unlocks the next dependency", async () => {
    const user = userEvent.setup();
    const repo = new MockPreparationRouteRepository();
    render(wrap(<HomeRouteCarousel tr={tr} />, repo));

    expect(screen.getByTestId("home-route-block")).toBeInTheDocument();
    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-state",
      "blocked",
    );
    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-focus",
      "false",
    );

    await user.click(screen.getByTestId("home-route-start-n2"));
    expect(repo.getInProgressIds().has("n2")).toBe(true);

    await user.click(screen.getByTestId("home-route-done-n2"));
    expect(repo.getCompletedIds().has("n2")).toBe(true);
    expect(screen.getByTestId("home-route-card-n3")).toHaveAttribute(
      "data-state",
      "canStart",
    );
  });

  it("blocked tasks cannot be completed and cook CTA appears only after required deps", () => {
    const repo = new MockPreparationRouteRepository();
    repo.markDone("n3");
    expect(repo.getCompletedIds().has("n3")).toBe(false);

    act(() => {
      repo.replaceCompletedForTests(["n1", "n2", "n3"]);
    });
    const snap = repo.getProjectionSnapshot();
    const ready = snap.readyTargets.find((t) => t.targetRecipeId === "r3");
    expect(ready).toBeDefined();
    expect(ready?.sourcePreparationRouteId).toBe(repo.getRouteId());
    expect(ready?.relatedPlanEntryId).toBe("pl-route-r3");
  });

  it("hides the home widget when the route has no tasks", () => {
    const repo = new MockPreparationRouteRepository([], "empty-route");
    render(wrap(<HomeRouteCarousel tr={tr} />, repo));
    expect(screen.queryByTestId("home-route-block")).not.toBeInTheDocument();
  });
});
