import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { createUnavailablePlanningRepository } from "@/adapters/live/livePlanningRepository";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider, useSession } from "@/app/session/SessionProvider";
import type { SessionState } from "@/app/session/types";
import type { ReactNode } from "react";
import type {
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
import { PlanningProvider } from "../PlanningProvider";
import { ShoppingHubPage } from "../ShoppingHubPage";
import { ShoppingPage } from "../ShoppingPage";
import {
  createMemoryBackend,
  createOfflineListStore,
  type OfflineListStore,
} from "./offlineListStore";
import { OfflineListProvider } from "./OfflineListProvider";
import { isOfflinePath, OfflineGate, OfflineNotices } from "./OfflineNotices";

const OWNER = "11111111-1111-1111-1111-111111111111";
const ITEM = {
  itemKey: "frango|Gram",
  displayName: "Frango",
  unit: "Gram",
  category: "required",
  requiredQuantity: 500,
  availableQuantity: 0,
  availableIsEstimate: false,
  toBuyQuantity: 500,
  checked: false,
  storeSection: "butcher_fish",
  uses: [],
} as unknown as ShoppingItem;
const LIST: ShoppingList = {
  menuId: "m1",
  from: "2026-11-05",
  mealCount: 1,
  items: [ITEM],
};
const unavailable = () => new RecipeApiError("unavailable", "x", 503);

function repository(overrides: Partial<PlanningRepository> = {}) {
  return {
    ...createUnavailablePlanningRepository(),
    getShopping: vi.fn(async () => LIST),
    setChecked: vi.fn(async () => undefined),
    setLineMark: vi.fn(async () => undefined),
    getArrival: undefined,
    ...overrides,
  } as PlanningRepository;
}

const OFFLINE: SessionState = {
  status: "offline",
  internalUserId: OWNER,
  csrfToken: null,
};
const ONLINE: SessionState = {
  status: "authenticated",
  internalUserId: OWNER,
  csrfToken: "csrf",
};

/** As `RequireAuth` does in the application, pages wait for the session check. */
function SessionKnown({ children }: { children: ReactNode }) {
  const { session } = useSession();
  return session.status === "loading" ? null : <>{children}</>;
}

function renderList(
  repo: PlanningRepository,
  store: OfflineListStore,
  session: SessionState = OFFLINE,
  path = "/app/cardapio/m1/compras",
) {
  const adapter = createSessionAdapter(session);
  const view = render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <SessionProvider adapter={adapter}>
        <InventoryProvider repository={createMockInventoryRepo()}>
          <PlanningProvider repository={repo}>
            <OfflineListProvider store={store}>
              <MemoryRouter initialEntries={[path]}>
                <OfflineNotices />
                <SessionKnown>
                  <OfflineGate>
                    <Routes>
                      <Route
                        path="/app/cardapio/:menuId/compras"
                        element={<ShoppingPage />}
                      />
                      <Route
                        path="/app/compras"
                        element={<ShoppingHubPage />}
                      />
                      <Route path="/app/hoje" element={<p>hoje</p>} />
                    </Routes>
                  </OfflineGate>
                </SessionKnown>
              </MemoryRouter>
            </OfflineListProvider>
          </PlanningProvider>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
  return { adapter, view };
}

async function savedStore() {
  const store = createOfflineListStore(createMemoryBackend());
  await store.saveList(OWNER, LIST, "2026-11-05T10:00:00Z");
  return store;
}

describe("offline shopping list", () => {
  it("opens the saved list, allows only marks, and queues them", async () => {
    const store = await savedStore();
    const repo = repository({
      getShopping: vi.fn(async () => {
        throw unavailable();
      }),
    });
    renderList(repo, store);
    await screen.findByTestId("shopping-page");
    expect(screen.getByTestId("offline-banner")).toHaveTextContent(
      "Suas marcações serão enviadas depois",
    );
    expect(screen.getByTestId("shopping-saved-at")).toBeInTheDocument();
    expect(screen.getByTestId("shopping-item-buy")).toBeDisabled();

    const user = userEvent.setup();
    await user.click(screen.getByTestId("shopping-item-check"));
    expect(screen.getByTestId("shopping-item-check")).toBeChecked();
    await waitFor(async () =>
      expect(await store.outbox(OWNER)).toHaveLength(1),
    );
    expect(repo.setChecked).not.toHaveBeenCalled();

    await user.click(screen.getByTestId("shopping-not-found"));
    expect(screen.getByTestId("shopping-substitute")).toBeDisabled();
    expect(screen.queryByTestId("shopping-swap-dish")).toBeNull();
    await user.click(screen.getByTestId("shopping-later-save"));
    await screen.findByTestId("shopping-later");
    await waitFor(async () =>
      expect(await store.outbox(OWNER)).toHaveLength(2),
    );
    expect(repo.setLineMark).not.toHaveBeenCalled();
  });

  it("shows a queued mark on a reloaded list", async () => {
    const store = await savedStore();
    await store.enqueue(OWNER, {
      kind: "check",
      menuId: "m1",
      itemKey: ITEM.itemKey,
      checked: true,
      displayName: "Frango",
    });
    renderList(
      repository({
        getShopping: vi.fn(async () => {
          throw unavailable();
        }),
      }),
      store,
    );
    expect(await screen.findByTestId("shopping-item-check")).toBeChecked();
  });

  it("explains when this list was never opened on the device", async () => {
    const store = createOfflineListStore(createMemoryBackend());
    renderList(
      repository({
        listMenus: vi.fn(async () => {
          throw unavailable();
        }),
      }),
      store,
      OFFLINE,
      "/app/compras",
    );
    await screen.findByTestId("shopping-hub-offline");
  });

  it("opens the last list from the hub without a connection", async () => {
    const store = await savedStore();
    renderList(
      repository({
        listMenus: vi.fn(async () => {
          throw unavailable();
        }),
        getShopping: vi.fn(async () => {
          throw unavailable();
        }),
      }),
      store,
      OFFLINE,
      "/app/compras",
    );
    await screen.findByTestId("shopping-page");
  });

  it("limits the offline session to the list routes", async () => {
    const store = await savedStore();
    renderList(repository(), store, OFFLINE, "/app/hoje");
    await screen.findByTestId("offline-route");
    expect(screen.queryByText("hoje")).toBeNull();
    expect(isOfflinePath("/app/compras")).toBe(true);
    expect(isOfflinePath("/app/cardapio/m1/compras")).toBe(true);
    expect(isOfflinePath("/app/cardapio/m1")).toBe(false);
    expect(isOfflinePath("/app/despensa")).toBe(false);
  });

  it("queues a mark when the signal drops mid-session", async () => {
    const store = await savedStore();
    const repo = repository({
      setChecked: vi.fn(async () => {
        throw unavailable();
      }),
    });
    renderList(repo, store, ONLINE);
    const user = userEvent.setup();
    await user.click(await screen.findByTestId("shopping-item-check"));
    await waitFor(async () =>
      expect(await store.outbox(OWNER)).toHaveLength(1),
    );
    expect(screen.getByTestId("shopping-item-check")).toBeChecked();
  });

  it("delivers on the online event, reloads, and names what no longer applies", async () => {
    const store = await savedStore();
    await store.enqueue(OWNER, {
      kind: "check",
      menuId: "m1",
      itemKey: "gone|Gram",
      checked: true,
      displayName: "Coentro",
    });
    await store.enqueue(OWNER, {
      kind: "check",
      menuId: "m1",
      itemKey: ITEM.itemKey,
      checked: true,
      displayName: "Frango",
    });
    const repo = repository();
    const { adapter } = renderList(repo, store, ONLINE);
    await waitFor(() => expect(repo.setChecked).toHaveBeenCalledOnce());
    expect(vi.mocked(repo.setChecked).mock.calls[0][1]).toBe(ITEM.itemKey);
    const notice = await screen.findByTestId("offline-conflicts");
    expect(notice).toHaveTextContent("1 marcação não se aplica mais: Coentro.");
    await userEvent.click(screen.getByTestId("offline-conflicts-dismiss"));
    expect(screen.queryByTestId("offline-conflicts")).toBeNull();
    await waitFor(async () => expect(await store.conflicts(OWNER)).toEqual([]));

    await store.enqueue(OWNER, {
      kind: "check",
      menuId: "m1",
      itemKey: ITEM.itemKey,
      checked: false,
      displayName: "Frango",
    });
    const calls = vi.mocked(adapter.getSession).mock.calls.length;
    await act(async () => {
      window.dispatchEvent(new Event("online"));
    });
    await waitFor(() => expect(repo.setChecked).toHaveBeenCalledTimes(2));
    expect(vi.mocked(adapter.getSession).mock.calls.length).toBeGreaterThan(
      calls,
    );
    expect(await store.outbox(OWNER)).toEqual([]);
  });
});
