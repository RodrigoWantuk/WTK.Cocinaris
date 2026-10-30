import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductionInventoryList } from "./ProductionInventoryList";
import {
  createMockInventoryRepo,
  renderInventoryTree,
  sampleLot,
} from "./testUtils";

describe("ProductionInventoryList", () => {
  it("shows loading then rows without requesting on each keystroke", async () => {
    const user = userEvent.setup();
    const listLots = vi.fn(async () => ({
      items: [sampleLot],
      nextCursor: null,
    }));
    const repo = createMockInventoryRepo({ listLots });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );

    expect(screen.getByTestId("inventory-loading")).toBeInTheDocument();
    expect(await screen.findByText("Rice")).toBeInTheDocument();
    expect(listLots).toHaveBeenCalledTimes(1);

    await user.type(screen.getByTestId("inventory-search"), "bean");
    expect(listLots).toHaveBeenCalledTimes(1);

    await user.click(screen.getByTestId("inventory-search-submit"));
    await waitFor(() => expect(listLots).toHaveBeenCalledTimes(2));
    expect(listLots).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: "bean" }),
    );
  });

  it("shows when a lot leaves the pantry automatically (PLAN-0056)", async () => {
    const repo = createMockInventoryRepo({
      listLots: async () => ({
        items: [{ ...sampleLot, autoRemoveOn: "2026-11-05" }],
        nextCursor: null,
      }),
    });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );

    expect(
      await screen.findByTestId(`inventory-auto-remove-${sampleLot.lotId}`),
    ).toHaveTextContent(/Leaves the pantry after .*2026/);
  });

  it("shows empty inventory and empty filtered states", async () => {
    const user = userEvent.setup();
    const listLots = vi.fn(async () => ({ items: [], nextCursor: null }));
    const repo = createMockInventoryRepo({ listLots });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );
    expect(await screen.findByTestId("inventory-empty")).toHaveTextContent(
      /Nothing here yet/,
    );

    await user.type(screen.getByTestId("inventory-search"), "zzz");
    await user.click(screen.getByTestId("inventory-search-submit"));
    expect(await screen.findByTestId("inventory-empty")).toHaveTextContent(
      /No lots match/,
    );
  });

  it("applies status/location filters, clears them, and loads more with cursor", async () => {
    const user = userEvent.setup();
    const listLots = vi
      .fn()
      // Initial load
      .mockResolvedValueOnce({
        items: [sampleLot],
        nextCursor: null,
      })
      // Filter submit — keep a cursor so load-more remains available
      .mockResolvedValueOnce({
        items: [sampleLot],
        nextCursor: "cursor-1",
      })
      // Load more
      .mockResolvedValueOnce({
        items: [
          {
            ...sampleLot,
            lotId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
            productName: "Beans",
          },
        ],
        nextCursor: null,
      })
      // Clear filters
      .mockResolvedValue({ items: [sampleLot], nextCursor: null });
    const repo = createMockInventoryRepo({ listLots });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );
    expect(await screen.findByText("Rice")).toBeInTheDocument();

    await user.selectOptions(
      screen.getByTestId("inventory-filter-status"),
      "active",
    );
    await user.selectOptions(
      screen.getByTestId("inventory-filter-location"),
      "Pantry",
    );
    await user.click(screen.getByTestId("inventory-search-submit"));
    await waitFor(() =>
      expect(listLots).toHaveBeenLastCalledWith(
        expect.objectContaining({
          status: "active",
          storageLocation: "Pantry",
        }),
      ),
    );

    await user.click(screen.getByTestId("inventory-load-more"));
    await waitFor(() =>
      expect(listLots).toHaveBeenCalledWith(
        expect.objectContaining({ cursor: "cursor-1" }),
      ),
    );
    expect(await screen.findByText("Beans")).toBeInTheDocument();

    await user.click(screen.getByTestId("inventory-clear-filters"));
    await waitFor(() =>
      expect(listLots).toHaveBeenLastCalledWith(
        expect.objectContaining({
          search: undefined,
          status: undefined,
          storageLocation: undefined,
        }),
      ),
    );
  });

  it("retries after failure", async () => {
    const user = userEvent.setup();
    const listLots = vi
      .fn()
      .mockRejectedValueOnce(new Error("network"))
      .mockResolvedValueOnce({ items: [sampleLot], nextCursor: null });
    const repo = createMockInventoryRepo({ listLots });
    render(
      renderInventoryTree({
        repository: repo,
        children: <ProductionInventoryList />,
      }),
    );
    expect(await screen.findByTestId("inventory-retry")).toBeInTheDocument();
    await user.click(screen.getByTestId("inventory-retry"));
    expect(await screen.findByText("Rice")).toBeInTheDocument();
  });
});
