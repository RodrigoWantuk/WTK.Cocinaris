import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { MenuSummary, ShoppingList } from "@/contracts/planning";
import { PlanningHubPage } from "./PlanningHubPage";
import {
  countShoppingLines,
  nextPlanningStep,
  pickCurrentMenu,
} from "./planningOverview";

const repos = vi.hoisted(() => ({
  planning: {} as Record<string, unknown>,
  plans: null as Record<string, unknown> | null,
  offline: null as { saveList: ReturnType<typeof vi.fn> } | null,
}));

vi.mock("./PlanningProvider", () => ({
  usePlanningRepository: () => repos.planning,
}));
vi.mock("@/features/shopping-plan/ShoppingPlanProvider", () => ({
  useOptionalShoppingPlanRepository: () => repos.plans,
}));
vi.mock("./offline/OfflineListProvider", () => ({
  useOfflineList: () => repos.offline,
}));
vi.mock("./ShoppingPage", () => ({ ShoppingPage: () => null }));
vi.mock("@/app/i18n/ProductionI18nProvider", () => ({
  useProductionI18n: () => ({
    locale: "pt-BR",
    t: (key: string, values?: Record<string, unknown>) =>
      values?.count !== undefined ? `${key}:${String(values.count)}` : key,
  }),
}));

const TODAY = new Date().toISOString().slice(0, 10);
const menu = (over: Partial<MenuSummary> = {}): MenuSummary => ({
  menuId: "m1",
  name: "",
  startDate: "2000-01-01",
  endDate: "2999-01-01",
  status: "accepted",
  entryCount: 6,
  updatedAt: "",
  ...over,
});
const line = (over: object) => ({ toBuyQuantity: 1, checked: false, ...over });
const list = (...items: object[]) =>
  ({
    menuId: "m1",
    from: TODAY,
    mealCount: 1,
    items,
  }) as unknown as ShoppingList;

function setUp({
  menus = [menu()],
  shopping = list(),
  current = null as { sessionId: string; status: string } | null,
  route = null as string | null,
}: {
  menus?: MenuSummary[];
  shopping?: ShoppingList | Error;
  current?: { sessionId: string; status: string } | null;
  route?: string | null;
} = {}) {
  repos.planning = {
    listMenus: vi.fn(async () => menus),
    getShopping: vi.fn(async () => {
      if (shopping instanceof Error) throw shopping;
      return shopping;
    }),
  };
  repos.plans = {
    getStart: vi.fn(async () => ({ current })),
    findRouteLink: vi.fn(async () => route),
  };
}

const show = () =>
  render(
    <MemoryRouter>
      <PlanningHubPage />
    </MemoryRouter>,
  );

describe("nextPlanningStep", () => {
  const base = { planInProgress: false, prepareSessionId: null };

  it("follows the real order of the work", () => {
    const m = menu();
    expect(nextPlanningStep({ ...base, menu: null, list: null })).toEqual({
      kind: "start",
    });
    expect(
      nextPlanningStep({
        ...base,
        menu: menu({ status: "draft" }),
        list: null,
      }),
    ).toEqual({ kind: "draft" });
    expect(
      nextPlanningStep({ ...base, menu: m, list: list(line({})) }),
    ).toEqual({ kind: "buy", count: 1 });
    expect(
      nextPlanningStep({
        ...base,
        menu: m,
        list: list(line({ checked: true })),
      }),
    ).toEqual({ kind: "arrival" });
    expect(
      nextPlanningStep({
        ...base,
        prepareSessionId: "s",
        menu: m,
        list: list(),
      }),
    ).toEqual({ kind: "prepare" });
    expect(nextPlanningStep({ ...base, menu: m, list: list() })).toEqual({
      kind: "view",
    });
  });

  it("keeps buying first while lines remain, even with some marked", () => {
    expect(
      nextPlanningStep({
        ...base,
        menu: menu(),
        list: list(line({ checked: true }), line({})),
      }),
    ).toEqual({ kind: "buy", count: 1 });
  });

  it("puts a plan in progress before everything else", () => {
    expect(
      nextPlanningStep({
        ...base,
        planInProgress: true,
        menu: menu(),
        list: list(line({})),
      }),
    ).toEqual({ kind: "resume" });
  });

  it("does not claim nothing is missing when the list could not be read", () => {
    expect(
      nextPlanningStep({
        ...base,
        prepareSessionId: "s",
        menu: menu(),
        list: null,
      }),
    ).toEqual({ kind: "view" });
  });
});

describe("countShoppingLines", () => {
  it("leaves out covered lines and those left for later", () => {
    expect(
      countShoppingLines(
        list(
          line({}),
          line({ checked: true }),
          line({ later: true }),
          line({ toBuyQuantity: 0 }),
        ),
      ),
    ).toEqual({ toBuy: 1, bought: 1 });
    expect(countShoppingLines(null)).toEqual({ toBuy: 0, bought: 0 });
  });
});

describe("pickCurrentMenu", () => {
  it("prefers the menu in use, then the next accepted one, then a live draft", () => {
    const inUse = menu({
      menuId: "use",
      startDate: "2026-11-01",
      endDate: "2026-11-10",
    });
    const next = menu({
      menuId: "next",
      startDate: "2026-11-20",
      endDate: "2026-11-25",
    });
    const later = menu({
      menuId: "later",
      startDate: "2026-12-20",
      endDate: "2026-12-25",
    });
    const draft = menu({
      menuId: "draft",
      status: "draft",
      startDate: "2026-11-01",
      endDate: "2026-11-30",
    });
    const old = menu({
      menuId: "old",
      status: "draft",
      startDate: "2026-01-01",
      endDate: "2026-01-05",
    });
    const day = "2026-11-05";
    expect(pickCurrentMenu([draft, later, next, inUse], day)?.menuId).toBe(
      "use",
    );
    expect(pickCurrentMenu([draft, later, next], day)?.menuId).toBe("next");
    expect(pickCurrentMenu([old, draft], day)?.menuId).toBe("draft");
    expect(pickCurrentMenu([old], day)).toBeNull();
  });
});

describe("PlanningHubPage", () => {
  beforeEach(() => {
    repos.plans = null;
    repos.offline = null;
  });

  it("keeps the list on the device without being opened, for the market", async () => {
    const shopping = list(line({}));
    setUp({ shopping });
    repos.offline = { saveList: vi.fn(async () => undefined) };
    show();
    await screen.findByTestId("planning-hub-buy");
    expect(repos.offline.saveList).toHaveBeenCalledWith(
      shopping,
      expect.any(String),
    );
  });

  it("is not affected when the device cannot store the list", async () => {
    setUp({ shopping: list(line({})) });
    repos.offline = {
      saveList: vi.fn(async () => {
        throw new Error("full");
      }),
    };
    show();
    expect(await screen.findByTestId("planning-hub-buy")).toBeInTheDocument();
  });

  it("saves nothing when the list could not be read", async () => {
    setUp({ shopping: new Error("down") });
    repos.offline = { saveList: vi.fn(async () => undefined) };
    show();
    await screen.findByTestId("planning-hub-view");
    expect(repos.offline.saveList).not.toHaveBeenCalled();
  });

  it("invites a first plan, and keeps the manual way second", async () => {
    setUp({ menus: [] });
    show();
    expect(await screen.findByTestId("planning-hub-start")).toHaveAttribute(
      "href",
      "/app/compras/planejar",
    );
    expect(screen.getByTestId("planning-hub-by-hand")).toHaveAttribute(
      "href",
      "/app/cardapio",
    );
    expect(screen.queryByTestId("planning-hub-menu")).toBeNull();
  });

  it("leads with what to buy, showing the count, and does not open the list by itself", async () => {
    setUp({ shopping: list(line({}), line({}), line({ checked: true })) });
    show();
    const main = await screen.findByTestId("planning-hub-buy");
    expect(main).toHaveTextContent("planningHub.buy:2");
    expect(main).toHaveAttribute("href", "/app/cardapio/m1/compras");
    expect(screen.getByTestId("planning-hub-arrival-link")).toBeInTheDocument();
    expect(screen.queryByTestId("planning-hub-list")).toBeNull();
    expect(screen.getByTestId("planning-hub-new")).toBeInTheDocument();
  });

  it("moves to the market arrival once everything is marked", async () => {
    setUp({ shopping: list(line({ checked: true })) });
    show();
    expect(await screen.findByTestId("planning-hub-arrival")).toHaveAttribute(
      "href",
      "/app/cardapio/m1/chegada",
    );
    expect(screen.queryByTestId("planning-hub-arrival-link")).toBeNull();
  });

  it("offers the preparation session when nothing is left to buy", async () => {
    setUp({ route: "s9" });
    show();
    expect(await screen.findByTestId("planning-hub-prepare")).toHaveAttribute(
      "href",
      "/app/compras/planejar/s9/preparo",
    );
  });

  it("continues a plan in progress where it stopped, and offers no second one", async () => {
    setUp({ current: { sessionId: "s1", status: "reviewing" } });
    show();
    expect(await screen.findByTestId("planning-hub-resume")).toHaveAttribute(
      "href",
      "/app/compras/planejar/s1",
    );
    expect(screen.queryByTestId("planning-hub-new")).toBeNull();
  });

  it("still works when the list or the other reads fail", async () => {
    setUp({ shopping: new Error("down") });
    repos.plans = {
      getStart: vi.fn(async () => {
        throw new Error("down");
      }),
      findRouteLink: vi.fn(async () => {
        throw new Error("down");
      }),
    };
    show();
    expect(await screen.findByTestId("planning-hub-view")).toHaveAttribute(
      "href",
      "/app/cardapio/m1",
    );
  });

  it("works without the plans provider", async () => {
    setUp({ menus: [] });
    repos.plans = null;
    show();
    expect(await screen.findByTestId("planning-hub-start")).toBeInTheDocument();
  });

  it("reports a failure to list the menus", async () => {
    setUp();
    repos.planning.listMenus = vi.fn(async () => {
      throw new Error("server");
    });
    show();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "planning.error.load",
    );
  });
});
