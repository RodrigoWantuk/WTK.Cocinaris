import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductionInventoryDetail } from "./ProductionInventoryDetail";
import { InventoryApiError } from "@/adapters/live/inventoryTypes";
import {
  createMockInventoryRepo,
  createSessionAdapter,
  sampleLot,
} from "./testUtils";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { InventoryProvider } from "./InventoryProvider";
import { MemoryRouter, Route, Routes } from "react-router-dom";

function renderDetail(
  repo = createMockInventoryRepo({
    getLot: vi.fn(async () => sampleLot),
    getHistory: vi.fn(async () => [
      {
        entryId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        kind: "LifecycleTransaction",
        type: "Consume",
        reasonCode: "ui_consume",
        changedFields: null,
        occurredAt: "2026-08-01T12:00:00Z",
        transition: null,
      },
    ]),
    adjustLot: vi.fn(async () => ({
      ...sampleLot,
      quantity: {
        kind: "measured" as const,
        value: 400,
        unit: "Gram" as const,
      },
      etag: '"v2"',
    })),
  }),
  session = createSessionAdapter(),
) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={session}>
        <InventoryProvider repository={repo}>
          <MemoryRouter initialEntries={[`/app/despensa/${sampleLot.lotId}`]}>
            <Routes>
              <Route
                path="/app/despensa/:lotId"
                element={<ProductionInventoryDetail />}
              />
              <Route path="/app/despensa" element={<div>list</div>} />
            </Routes>
          </MemoryRouter>
        </InventoryProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

describe("ProductionInventoryDetail", () => {
  it("loads lot projection and localized history", async () => {
    renderDetail();
    expect(await screen.findByText("Rice")).toBeInTheDocument();
    expect(screen.getByTestId("inventory-history")).toHaveTextContent(
      /Lifecycle transaction|Consumed from UI|Consume/,
    );
    expect(screen.getByTestId("inventory-printed-date")).toHaveTextContent(
      /31/,
    );
  });

  it("consumes with ETag and new idempotency key, then refreshes history", async () => {
    const user = userEvent.setup();
    const adjustLot = vi.fn(async () => ({
      ...sampleLot,
      quantity: {
        kind: "measured" as const,
        value: 400,
        unit: "Gram" as const,
      },
      etag: '"v2"',
    }));
    const getHistory = vi
      .fn()
      .mockResolvedValueOnce([])
      .mockResolvedValueOnce([
        {
          entryId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
          kind: "LifecycleTransaction",
          type: "Consume",
          reasonCode: "ui_consume",
          changedFields: null,
          occurredAt: "2026-08-01T13:00:00Z",
          transition: null,
        },
      ]);
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      getHistory,
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.type(screen.getByTestId("inventory-adjust-value"), "100");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    await waitFor(() => expect(adjustLot).toHaveBeenCalled());
    expect(adjustLot).toHaveBeenCalledWith(
      sampleLot.lotId,
      expect.objectContaining({ type: "Consume", value: 100 }),
      expect.objectContaining({
        etag: '"v1"',
        idempotencyKey: expect.stringMatching(/^[0-9a-f-]{36}$/i),
      }),
    );
    expect(getHistory).toHaveBeenCalledTimes(2);
  });

  it("supports discard and correct actions", async () => {
    const user = userEvent.setup();
    const adjustLot = vi.fn(async (_id, input) => ({
      ...sampleLot,
      etag: '"v3"',
      quantity: {
        kind: "measured" as const,
        value: input.type === "Correct" ? input.value! : 1,
        unit: "Gram" as const,
      },
    }));
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      getHistory: vi.fn(async () => []),
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.selectOptions(
      screen.getByTestId("inventory-adjust-type"),
      "Discard",
    );
    await user.type(screen.getByTestId("inventory-adjust-value"), "10");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    await waitFor(() =>
      expect(adjustLot).toHaveBeenCalledWith(
        sampleLot.lotId,
        expect.objectContaining({ type: "Discard" }),
        expect.any(Object),
      ),
    );
  });

  it("changes qualitative availability", async () => {
    const user = userEvent.setup();
    const qualitative = {
      ...sampleLot,
      quantity: {
        kind: "qualitative" as const,
        availability: "Low" as const,
      },
    };
    const adjustLot = vi.fn(async () => ({
      ...qualitative,
      quantity: {
        kind: "qualitative" as const,
        availability: "Available" as const,
      },
      etag: '"v9"',
    }));
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => qualitative),
      getHistory: vi.fn(async () => []),
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.selectOptions(
      screen.getByTestId("inventory-availability"),
      "Available",
    );
    await user.click(screen.getByTestId("inventory-availability-submit"));
    await waitFor(() =>
      expect(adjustLot).toHaveBeenCalledWith(
        sampleLot.lotId,
        expect.objectContaining({
          type: "AvailabilityChanged",
          availabilityState: "Available",
        }),
        expect.any(Object),
      ),
    );
  });

  it("surfaces 412 and 428 without retry", async () => {
    const user = userEvent.setup();
    const adjustLot = vi
      .fn()
      .mockRejectedValueOnce(
        new InventoryApiError("precondition_failed", "stale", 412),
      );
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      getHistory: vi.fn(async () => []),
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.type(screen.getByTestId("inventory-adjust-value"), "1");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    expect(
      await screen.findByTestId("inventory-stale-conflict"),
    ).toBeInTheDocument();
    expect(adjustLot).toHaveBeenCalledTimes(1);

    adjustLot.mockRejectedValueOnce(
      new InventoryApiError("precondition_required", "need if-match", 428),
    );
    await user.click(screen.getByTestId("inventory-reload-conflict"));
    await screen.findByText("Rice");
    await user.type(screen.getByTestId("inventory-adjust-value"), "2");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    expect(
      await screen.findByTestId("inventory-missing-precondition"),
    ).toBeInTheDocument();
  });

  it("disables mutations when CSRF is missing", async () => {
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      getHistory: vi.fn(async () => []),
    });
    renderDetail(
      repo,
      createSessionAdapter({
        status: "authenticated",
        internalUserId: "11111111-1111-1111-1111-111111111111",
        csrfToken: null,
      }),
    );
    await screen.findByText("Rice");
    expect(screen.getByTestId("inventory-adjust-submit")).toBeDisabled();
    expect(screen.getByTestId("inventory-missing-csrf")).toBeInTheDocument();
  });

  it("does not treat history refresh failure as adjust failure", async () => {
    const user = userEvent.setup();
    const adjustLot = vi.fn(async () => ({
      ...sampleLot,
      quantity: {
        kind: "measured" as const,
        value: 400,
        unit: "Gram" as const,
      },
      etag: '"v2"',
    }));
    const getHistory = vi
      .fn()
      .mockResolvedValueOnce([
        {
          entryId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
          kind: "LifecycleTransaction",
          type: "Consume",
          reasonCode: "ui_consume",
          changedFields: null,
          occurredAt: "2026-08-01T12:00:00Z",
          transition: null,
        },
      ])
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => sampleLot),
      getHistory,
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.type(screen.getByTestId("inventory-adjust-value"), "100");
    await user.click(screen.getByTestId("inventory-adjust-submit"));

    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("production-inventory-detail")).toHaveTextContent(
      /400/,
    );
    expect(
      screen.queryByText(/Could not adjust quantity/i),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("inventory-reload-history")).toBeInTheDocument();
    expect(
      screen.getByTestId("inventory-reload-after-history"),
    ).toBeInTheDocument();
    expect(adjustLot).toHaveBeenCalledTimes(1);
    expect(adjustLot).toHaveBeenCalledWith(
      sampleLot.lotId,
      expect.any(Object),
      expect.objectContaining({ etag: '"v1"' }),
    );

    getHistory.mockResolvedValueOnce([]);
    await user.type(screen.getByTestId("inventory-adjust-value"), "1");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    await waitFor(() => expect(adjustLot).toHaveBeenCalledTimes(2));
    expect(adjustLot).toHaveBeenLastCalledWith(
      sampleLot.lotId,
      expect.any(Object),
      expect.objectContaining({ etag: '"v2"' }),
    );
  });

  it("keeps availability mutation success when history refresh fails", async () => {
    const user = userEvent.setup();
    const qualitative = {
      ...sampleLot,
      quantity: {
        kind: "qualitative" as const,
        availability: "Low" as const,
      },
    };
    const adjustLot = vi.fn(async () => ({
      ...qualitative,
      quantity: {
        kind: "qualitative" as const,
        availability: "Available" as const,
      },
      etag: '"v9"',
    }));
    const getHistory = vi
      .fn()
      .mockResolvedValueOnce([])
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => qualitative),
      getHistory,
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.selectOptions(
      screen.getByTestId("inventory-availability"),
      "Available",
    );
    await user.click(screen.getByTestId("inventory-availability-submit"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/Could not adjust quantity/i),
    ).not.toBeInTheDocument();
    expect(adjustLot).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("production-inventory-detail")).toHaveTextContent(
      /Available/i,
    );
  });

  it("keeps history refresh warning visible after repeated reload failures", async () => {
    const user = userEvent.setup();
    const initialHistory = [
      {
        entryId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        kind: "LifecycleTransaction" as const,
        type: "Consume" as const,
        reasonCode: "ui_consume",
        changedFields: null,
        occurredAt: "2026-08-01T12:00:00Z",
        transition: null,
      },
    ];
    const adjustLot = vi.fn(async () => ({
      ...sampleLot,
      quantity: {
        kind: "measured" as const,
        value: 400,
        unit: "Gram" as const,
      },
      etag: '"v2"',
    }));
    const getHistory = vi
      .fn()
      .mockResolvedValueOnce(initialHistory)
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503));
    const getLot = vi.fn(async () => sampleLot);
    const repo = createMockInventoryRepo({
      getLot,
      getHistory,
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    expect(screen.getByTestId("inventory-history")).toHaveTextContent(
      /Consume/,
    );

    await user.type(screen.getByTestId("inventory-adjust-value"), "100");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("inventory-history")).toHaveAttribute(
      "data-history-stale",
      "true",
    );
    expect(screen.getByTestId("inventory-history")).toHaveTextContent(
      /Consume/,
    );
    expect(adjustLot).toHaveBeenCalledTimes(1);

    await user.click(screen.getByTestId("inventory-reload-after-history"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();
    expect(screen.getByTestId("inventory-history")).toHaveAttribute(
      "data-history-stale",
      "true",
    );
    expect(screen.getByTestId("inventory-history")).toHaveTextContent(
      /Consume/,
    );
    expect(adjustLot).toHaveBeenCalledTimes(1);

    await user.click(screen.getByTestId("inventory-reload-history"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();
    expect(adjustLot).toHaveBeenCalledTimes(1);
  });

  it("clears history refresh warning only after a successful history reload", async () => {
    const user = userEvent.setup();
    const initialHistory = [
      {
        entryId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        kind: "LifecycleTransaction" as const,
        type: "Consume" as const,
        reasonCode: "ui_consume",
        changedFields: null,
        occurredAt: "2026-08-01T12:00:00Z",
        transition: null,
      },
    ];
    const refreshedHistory = [
      {
        entryId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        kind: "LifecycleTransaction" as const,
        type: "Discard" as const,
        reasonCode: "ui_discard",
        changedFields: null,
        occurredAt: "2026-08-01T13:00:00Z",
        transition: null,
      },
    ];
    const adjustLot = vi.fn(async () => ({
      ...sampleLot,
      quantity: {
        kind: "measured" as const,
        value: 400,
        unit: "Gram" as const,
      },
      etag: '"v2"',
    }));
    const getHistory = vi
      .fn()
      .mockResolvedValueOnce(initialHistory)
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockRejectedValueOnce(new InventoryApiError("unavailable", "down", 503))
      .mockResolvedValueOnce(refreshedHistory);
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => ({
        ...sampleLot,
        quantity: {
          kind: "measured" as const,
          value: 400,
          unit: "Gram" as const,
        },
        etag: '"v2"',
      })),
      getHistory,
      adjustLot,
    });
    renderDetail(repo);
    await screen.findByText("Rice");
    await user.type(screen.getByTestId("inventory-adjust-value"), "100");
    await user.click(screen.getByTestId("inventory-adjust-submit"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();

    await user.click(screen.getByTestId("inventory-reload-history"));
    expect(
      await screen.findByTestId("inventory-history-refresh-error"),
    ).toBeInTheDocument();

    await user.click(screen.getByTestId("inventory-reload-history"));
    await waitFor(() =>
      expect(
        screen.queryByTestId("inventory-history-refresh-error"),
      ).not.toBeInTheDocument(),
    );
    expect(screen.getByTestId("inventory-history")).toHaveAttribute(
      "data-history-stale",
      "false",
    );
    expect(screen.getByTestId("inventory-history")).toHaveTextContent(
      /Discard/,
    );
    expect(adjustLot).toHaveBeenCalledTimes(1);
  });
});
