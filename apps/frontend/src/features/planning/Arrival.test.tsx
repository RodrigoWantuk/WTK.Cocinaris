import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import type {
  ArrivalPlan,
  PlanningRepository,
  ShoppingItem,
  ShoppingList,
} from "@/contracts/planning";
import { RecipeApiError } from "@/contracts/recipes";
import { InventoryProvider } from "@/features/inventory/InventoryProvider";
import {
  createMockInventoryRepo,
  createSessionAdapter,
} from "@/features/inventory/testUtils";
import { ArrivalPage } from "./ArrivalPage";
import { PlanningProvider } from "./PlanningProvider";
import { ShoppingPage } from "./ShoppingPage";
import { buildShoppingListText } from "./shoppingListText";

const PLAN: ArrivalPlan = {
  menuId: "m1",
  guidanceEnabled: true,
  containerCount: 3,
  lines: [
    {
      itemKey: "frango|Gram",
      displayName: "Frango",
      unit: "Gram",
      storeSection: "butcher_fish",
      checked: true,
      boughtQuantity: 900,
      needsRealQuantity: true,
      packageSize: null,
      requiredQuantity: 600,
      portions: [
        {
          location: "Refrigerator",
          quantity: 300,
          count: 1,
          dish: "Wrap",
          useFirst: false,
        },
        {
          location: "Freezer",
          quantity: 300,
          count: 2,
          dish: null,
          useFirst: false,
        },
      ],
    },
  ],
};

function renderArrival(repo: Partial<PlanningRepository>) {
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <PlanningProvider
            repository={{ ...createUnavailablePlanningRepository(), ...repo }}
          >
            <MemoryRouter initialEntries={["/app/cardapio/m1/chegada"]}>
              <Routes>
                <Route
                  path="/app/cardapio/:menuId/chegada"
                  element={<ArrivalPage />}
                />
                <Route
                  path="/app/cardapio/:menuId/compras"
                  element={<ShoppingPage />}
                />
              </Routes>
            </MemoryRouter>
          </PlanningProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("arrival (PLAN-0062)", () => {
  it("groups portions by place and records them with the place the person chose", async () => {
    const user = userEvent.setup();
    const recordArrival = vi.fn(async () => ({ recorded: 3, failed: [] }));
    renderArrival({
      getArrival: vi.fn(async () => PLAN),
      recordArrival,
      setPackageSize: vi.fn(async () => undefined),
    });

    const freezer = await screen.findByTestId("arrival-Freezer");
    expect(within(freezer).getByText(/2 × 300/)).toBeInTheDocument();
    expect(within(freezer).getByTestId("arrival-label")).toHaveTextContent(
      "Frango",
    );
    expect(
      within(screen.getByTestId("arrival-Refrigerator")).getByText(
        "Para: Wrap",
      ),
    ).toBeInTheDocument();
    expect(screen.getByTestId("arrival-containers")).toHaveTextContent("3");

    await user.selectOptions(
      within(freezer).getByTestId("arrival-place"),
      "Refrigerator",
    );
    expect(screen.queryByTestId("arrival-Freezer")).toBeNull();
    await user.click(screen.getByTestId("arrival-save"));

    expect(await screen.findByTestId("arrival-done")).toHaveTextContent(
      "3 itens",
    );
    const [, lines] = recordArrival.mock.calls[0] as unknown as [
      string,
      ArrivalPlan["lines"],
    ];
    expect(lines[0].portions.map((portion) => portion.location)).toEqual([
      "Refrigerator",
      "Refrigerator",
    ]);
  });

  it("recomputes for the amount that came and can remember the package", async () => {
    const user = userEvent.setup();
    const previewArrival = vi.fn(async () => PLAN);
    const setPackageSize = vi.fn(async () => undefined);
    renderArrival({
      getArrival: vi.fn(async () => PLAN),
      previewArrival,
      setPackageSize,
      recordArrival: vi.fn(async () => ({ recorded: 1, failed: [] })),
    });

    const input = await screen.findByTestId("arrival-quantity");
    await user.clear(input);
    await user.type(input, "1000");
    await user.tab();
    await waitFor(() => expect(previewArrival).toHaveBeenCalled());
    expect((previewArrival.mock.calls[0] as unknown[])[2]).toEqual([
      { itemKey: "frango|Gram", boughtQuantity: 1000 },
    ]);

    await user.click(screen.getByTestId("arrival-remember-package"));
    await user.click(screen.getByTestId("arrival-save"));
    await waitFor(() =>
      expect(setPackageSize).toHaveBeenCalledWith(
        "frango|Gram",
        900,
        expect.anything(),
      ),
    );
  });

  it("keeps the same key and reports a partial result", async () => {
    const user = userEvent.setup();
    const recordArrival = vi.fn(async () => ({
      recorded: 1,
      failed: [{ itemKey: "frango|Gram", errorCode: "x" }],
    }));
    renderArrival({ getArrival: vi.fn(async () => PLAN), recordArrival });

    await user.click(await screen.findByTestId("arrival-save"));
    expect(await screen.findByTestId("arrival-partial")).toHaveTextContent(
      "1 itens guardados, 1 não",
    );
    await user.click(screen.getByTestId("arrival-save"));

    await waitFor(() => expect(recordArrival).toHaveBeenCalledTimes(2));
    const key = (call: unknown[]) =>
      (call[2] as { idempotencyKey: string }).idempotencyKey;
    expect(key(recordArrival.mock.calls[0])).toBe(
      key(recordArrival.mock.calls[1]),
    );
  });

  it("does not mention guidance when it is off, and explains an empty purchase", async () => {
    const off = {
      ...PLAN,
      guidanceEnabled: false,
      containerCount: 0,
      lines: [
        {
          ...PLAN.lines[0],
          portions: [
            {
              location: "Refrigerator",
              quantity: 900,
              count: 1,
              dish: null,
              useFirst: false,
            },
          ],
        },
      ],
    };
    const view = renderArrival({ getArrival: vi.fn(async () => off) });
    await screen.findByTestId("arrival-page");
    expect(screen.queryByText(/não uma garantia/)).toBeNull();
    expect(screen.queryByTestId("arrival-ahead")).toBeNull();
    view.unmount();

    renderArrival({ getArrival: vi.fn(async () => ({ ...PLAN, lines: [] })) });
    expect(await screen.findByTestId("arrival-empty")).toBeInTheDocument();
  });
});

const ITEM: ShoppingItem = {
  itemKey: "coentro|Gram",
  markKey: "coentro|Gram",
  displayName: "Coentro",
  unit: "Gram",
  category: "required",
  requiredQuantity: 50,
  availableQuantity: 0,
  toBuyQuantity: 50,
  checked: true,
  storeSection: "produce",
  place: "street_market",
  uses: [
    {
      entryId: "e1",
      recipeName: "Moqueca",
      plannedDate: null,
      quantity: 50,
      optional: false,
    },
  ],
};
const LIST = (items: ShoppingItem[]): ShoppingList => ({
  menuId: "m1",
  from: "2026-11-04",
  mealCount: 1,
  items,
});

function renderList(repo: Partial<PlanningRepository>) {
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={createSessionAdapter()}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <PlanningProvider
            repository={{ ...createUnavailablePlanningRepository(), ...repo }}
          >
            <MemoryRouter initialEntries={["/app/cardapio/m1/compras"]}>
              <Routes>
                <Route
                  path="/app/cardapio/:menuId/compras"
                  element={<ShoppingPage />}
                />
              </Routes>
            </MemoryRouter>
          </PlanningProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("shopping marks (PLAN-0062)", () => {
  it("names the affected dishes and saves a substitute", async () => {
    const user = userEvent.setup();
    const setLineMark = vi.fn(async () => undefined);
    renderList({ getShopping: vi.fn(async () => LIST([ITEM])), setLineMark });

    await user.click(await screen.findByTestId("shopping-item-more"));
    await user.click(screen.getByTestId("shopping-not-found"));
    expect(screen.getByTestId("shopping-not-found-sheet")).toHaveTextContent(
      "Moqueca",
    );
    await user.type(screen.getByTestId("shopping-substitute"), "Salsa");
    await user.click(screen.getByTestId("shopping-substitute-save"));

    await waitFor(() => expect(setLineMark).toHaveBeenCalled());
    expect((setLineMark.mock.calls[0] as unknown[])[1]).toEqual({
      itemKey: "coentro|Gram",
      state: null,
      substituteName: "Salsa",
      place: "street_market",
    });
  });

  it("says why a substitute is refused", async () => {
    const user = userEvent.setup();
    renderList({
      getShopping: vi.fn(async () => LIST([ITEM])),
      setLineMark: vi.fn(async () => {
        throw new RecipeApiError("restriction_blocked", "x", 422);
      }),
    });

    await user.click(await screen.findByTestId("shopping-item-more"));
    await user.click(screen.getByTestId("shopping-not-found"));
    await user.type(screen.getByTestId("shopping-substitute"), "Frango");
    await user.click(screen.getByTestId("shopping-substitute-save"));

    expect(await screen.findByRole("alert")).toHaveTextContent("restrição");
  });

  it("moves a later line to its own block and brings it back", async () => {
    const user = userEvent.setup();
    const setLineMark = vi.fn(async () => undefined);
    renderList({
      getShopping: vi.fn(async () => LIST([{ ...ITEM, later: true }])),
      setLineMark,
    });

    await user.click(await screen.findByTestId("shopping-later-back"));
    await waitFor(() => expect(setLineMark).toHaveBeenCalled());
    expect((setLineMark.mock.calls[0] as unknown[])[1]).toMatchObject({
      state: null,
    });
    expect(screen.queryByTestId("shopping-section-produce")).toBeNull();
  });

  it("groups by place only when asked, and offers the arrival once something is checked", async () => {
    const user = userEvent.setup();
    renderList({
      getShopping: vi.fn(async () => LIST([ITEM])),
      setLineMark: vi.fn(async () => undefined),
      getArrival: vi.fn(async () => PLAN),
    });

    expect(
      await screen.findByTestId("shopping-section-produce"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("shopping-arrival")).toBeInTheDocument();
    await user.click(screen.getByTestId("shopping-split-place"));
    expect(
      screen.getByTestId("shopping-place-street_market"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("shopping-place")).toHaveValue("street_market");
  });

  it("shows the portions hint from the advisor", async () => {
    const hinted = {
      ...PLAN,
      lines: [{ ...PLAN.lines[0], itemKey: ITEM.itemKey }],
    };
    renderList({
      getShopping: vi.fn(async () => LIST([ITEM])),
      getArrival: vi.fn(async () => hinted),
    });

    expect(
      await screen.findByTestId("shopping-item-portions"),
    ).toHaveTextContent("3 porções");
  });

  it("shares by place and leaves later lines out", () => {
    const text = buildShoppingListText(
      [
        { ...ITEM, checked: false },
        { ...ITEM, displayName: "Depois", checked: false, later: true },
      ],
      (key) => key,
      String,
      true,
    );

    expect(text).toContain("shopping.place.street_market");
    expect(text).toContain("Coentro");
    expect(text).not.toContain("Depois");
  });
});
