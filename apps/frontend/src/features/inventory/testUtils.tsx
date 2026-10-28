import type { ReactNode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { InventoryProvider } from "./InventoryProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import type { InventoryRepository } from "@/adapters/live/inventoryTypes";
import type { SessionAdapter, SessionState } from "@/app/session/types";

export function createMockInventoryRepo(
  overrides: Partial<InventoryRepository> = {},
): InventoryRepository {
  return {
    listLots: vi.fn(async () => ({ items: [], nextCursor: null })),
    getLot: vi.fn(),
    createLot: vi.fn(),
    updateLot: vi.fn(),
    adjustLot: vi.fn(),
    transitionLot: vi.fn(),
    deleteLot: vi.fn(),
    getHistory: vi.fn(async () => []),
    ...overrides,
  };
}

export function createSessionAdapter(
  state: SessionState = {
    status: "authenticated",
    internalUserId: "11111111-1111-1111-1111-111111111111",
    csrfToken: "csrf-test",
    displayName: "Ada",
    timeZone: "UTC",
  },
): SessionAdapter {
  return {
    getSession: vi.fn(async () => state),
    beginLogin: vi.fn(),
    logout: vi.fn(async () => undefined),
  };
}

export function renderInventoryTree({
  repository,
  sessionAdapter = createSessionAdapter(),
  initialPath = "/app/despensa",
  children,
}: {
  repository: InventoryRepository;
  sessionAdapter?: SessionAdapter;
  initialPath?: string;
  children: ReactNode;
}) {
  return (
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={sessionAdapter}>
        <InventoryProvider repository={repository}>
          <MemoryRouter initialEntries={[initialPath]}>
            <Routes>
              <Route path="/app/despensa" element={children} />
              <Route path="/app/despensa/novo" element={children} />
              <Route path="/app/despensa/:lotId" element={children} />
              <Route path="/app/despensa/:lotId/editar" element={children} />
            </Routes>
          </MemoryRouter>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>
  );
}

export const sampleLot = {
  lotId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  productId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  productName: "Rice",
  quantity: { kind: "measured" as const, value: 500, unit: "Gram" as const },
  storageLocation: "Pantry",
  customLocation: null,
  packageState: "Sealed",
  printedExpirationDate: "2026-12-31",
  notes: null,
  version: "v1",
  etag: '"v1"',
  createdAt: "2026-08-01T00:00:00Z",
  updatedAt: "2026-08-01T00:00:00Z",
  lifecycle: {
    state: "AsAcquired",
    openedAt: null,
    frozenAt: null,
    thawStartedAt: null,
    thawedAt: null,
    parentLotId: null,
  },
};
