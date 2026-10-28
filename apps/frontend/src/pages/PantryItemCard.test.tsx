import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route, useLocation } from "react-router-dom";
import { PantryItemCard } from "./Pantry";
import { StoreProvider } from "@/lib/store";
import { MOCK_PREPARED_COMPONENT_SHORTFALL } from "@/adapters/mock/preparedComponentFixtures";

function tr(key: string): string {
  const map: Record<string, string> = {
    "pantry.reserved.label": "reservado",
    "pantry.reserved.exact": "totalmente reservado",
    "pantry.reserved.debt": "Revisar faltante",
    "pantry.locations.freezer": "freezer",
  };
  return map[key] ?? key;
}

function LocationProbe() {
  const loc = useLocation();
  return (
    <div data-testid="nav-location">
      {loc.pathname}
      {loc.search}
    </div>
  );
}

const shortfallItem = {
  id: MOCK_PREPARED_COMPONENT_SHORTFALL.inventoryItemId,
  name: "Caldo",
  location: "freezer",
  mode: "measured",
  qty: 2000,
  unit: "ml",
  reservedFor: [
    { title: "Escondidinho rápido", qtyNum: 1200, unit: "ml", when: "Terça" },
    { title: "Sopa de feijão", qtyNum: 1000, unit: "ml", when: "Sexta" },
  ],
};

describe("PantryItemCard accessibility structure", () => {
  it("keeps detail link and shortfall action as siblings", async () => {
    const user = userEvent.setup();
    render(
      <StoreProvider enablePrototypeFixtures persistPrototypeAuth>
        <MemoryRouter initialEntries={["/app/despensa"]}>
          <PantryItemCard item={shortfallItem} tr={tr} />
          <Routes>
            <Route path="/app/despensa" element={<LocationProbe />} />
            <Route path="/app/despensa/:id" element={<LocationProbe />} />
            <Route path="/app/compras" element={<LocationProbe />} />
          </Routes>
        </MemoryRouter>
      </StoreProvider>,
    );

    const card = screen.getByTestId(`pantry-item-${shortfallItem.id}`);
    const link = screen.getByTestId(`pantry-item-link-${shortfallItem.id}`);
    const debt = screen.getByTestId(`pantry-reserved-debt-${shortfallItem.id}`);

    expect(card.contains(link)).toBe(true);
    expect(link.contains(debt)).toBe(false);

    await user.click(debt);
    expect(screen.getByTestId("nav-location").textContent).toBe(
      "/app/compras?review=shortfall",
    );
  });

  it("opens item detail from the main link without nested button activation", async () => {
    const user = userEvent.setup();
    render(
      <StoreProvider enablePrototypeFixtures persistPrototypeAuth>
        <MemoryRouter initialEntries={["/app/despensa"]}>
          <PantryItemCard item={shortfallItem} tr={tr} />
          <Routes>
            <Route path="/app/despensa" element={<LocationProbe />} />
            <Route path="/app/despensa/:id" element={<LocationProbe />} />
            <Route path="/app/compras" element={<LocationProbe />} />
          </Routes>
        </MemoryRouter>
      </StoreProvider>,
    );

    await user.click(
      screen.getByTestId(`pantry-item-link-${shortfallItem.id}`),
    );
    expect(screen.getByTestId("nav-location").textContent).toBe(
      `/app/despensa/${shortfallItem.id}`,
    );
  });
});
