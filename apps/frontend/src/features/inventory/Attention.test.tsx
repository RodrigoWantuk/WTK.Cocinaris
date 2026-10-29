import { render, screen, within } from "@testing-library/react";
import type { InventoryLotView } from "@/adapters/live/inventoryTypes";
import { ProductionInventoryDetail } from "./ProductionInventoryDetail";
import { ProductionInventoryList } from "./ProductionInventoryList";
import {
  createMockInventoryRepo,
  renderInventoryTree,
  sampleLot,
} from "./testUtils";

const urgent: InventoryLotView = {
  ...sampleLot,
  lotId: "11111111-2222-3333-4444-555555555555",
  productName: "Yogurt",
  attention: {
    state: "UsePriority",
    estimatedUseBy: "2026-10-30",
    evidence: "PrintedDate",
    reasons: ["printed_date", "date_today_or_tomorrow", "some_future_code"],
  },
};

describe("inventory attention (PLAN-0037 Phase 2)", () => {
  it("lists lots needing attention above the pantry, with their state", async () => {
    const repo = createMockInventoryRepo({
      listLots: vi.fn(async () => ({
        items: [sampleLot, urgent],
        nextCursor: null,
      })),
      listAttention: vi.fn(async () => [urgent]),
    });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );

    const panel = await screen.findByTestId("inventory-attention-panel");
    expect(within(panel).getByText("Needs attention")).toBeInTheDocument();
    expect(
      within(panel).getByTestId(`inventory-attention-link-${urgent.lotId}`),
    ).toHaveTextContent("Use today or tomorrow");
    expect(panel).toHaveTextContent("Check the food before using it.");
  });

  it("hides the panel when nothing needs attention or the request fails", async () => {
    const repo = createMockInventoryRepo({
      listAttention: vi.fn(async () => {
        throw new Error("offline");
      }),
    });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );
    await screen.findByTestId("production-inventory-list");
    expect(screen.queryByTestId("inventory-attention-panel")).toBeNull();
  });

  it("explains the attention on the lot detail and skips unknown reason codes", async () => {
    const repo = createMockInventoryRepo({ getLot: vi.fn(async () => urgent) });
    render(
      renderInventoryTree({
        repository: repo,
        initialPath: `/app/despensa/${urgent.lotId}`,
        children: <ProductionInventoryDetail />,
      }),
    );

    const section = await screen.findByTestId("inventory-detail-attention");
    const reasons = within(section).getByTestId("inventory-attention-reasons");
    expect(within(reasons).getAllByRole("listitem")).toHaveLength(2);
    expect(reasons).toHaveTextContent("Based on the printed date.");
    expect(reasons).not.toHaveTextContent("some_future_code");
  });

  it("shows no attention for a normal lot", async () => {
    const repo = createMockInventoryRepo({
      getLot: vi.fn(async () => ({
        ...sampleLot,
        attention: {
          state: "Normal",
          estimatedUseBy: null,
          evidence: "Unknown",
          reasons: ["no_date"],
        },
      })),
    });
    render(
      renderInventoryTree({
        repository: repo,
        initialPath: `/app/despensa/${sampleLot.lotId}`,
        children: <ProductionInventoryDetail />,
      }),
    );
    await screen.findByTestId("production-inventory-detail");
    expect(screen.queryByTestId("inventory-detail-attention")).toBeNull();
  });
});
