import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PreparedComponentAvailabilityBar } from "./PreparedComponentAvailabilityBar";
import {
  MOCK_PREPARED_COMPONENT_AVAILABLE,
  MOCK_PREPARED_COMPONENT_FULLY_RESERVED,
  MOCK_PREPARED_COMPONENT_SHORTFALL,
} from "@/adapters/mock/preparedComponentFixtures";
import type { PreparedComponentAvailability } from "@/contracts/preparation";

function tr(key: string): string {
  const map: Record<string, string> = {
    "pantry.reserved.label": "reservado",
    "pantry.reserved.exact": "totalmente reservado",
    "pantry.reserved.debt": "Revisar faltante",
    "pantry.reserved.free": "livre",
  };
  return map[key] ?? key;
}

describe("PreparedComponentAvailabilityBar", () => {
  it("renders partial reservation with free quantity text (not color-only)", () => {
    render(
      <PreparedComponentAvailabilityBar
        availability={MOCK_PREPARED_COMPONENT_AVAILABLE}
        tr={tr}
      />,
    );
    const el = screen.getByTestId("pantry-reserved-cp_broth");
    expect(el).toHaveAttribute("data-status", "balanced");
    expect(el.textContent).toMatch(/reservado/);
    expect(el.textContent).toMatch(/1000 ml|livre|1000/);
  });

  it("renders fully reserved state", () => {
    render(
      <PreparedComponentAvailabilityBar
        availability={MOCK_PREPARED_COMPONENT_FULLY_RESERVED}
        tr={tr}
      />,
    );
    expect(
      screen.getByTestId("pantry-reserved-cp_broth_exact"),
    ).toHaveAttribute("data-status", "exact");
    expect(screen.getByText(/totalmente reservado/)).toBeInTheDocument();
  });

  it("renders shortfall and invokes review action", async () => {
    const user = userEvent.setup();
    const onReview = vi.fn();
    render(
      <PreparedComponentAvailabilityBar
        availability={MOCK_PREPARED_COMPONENT_SHORTFALL}
        tr={tr}
        onReviewShortfall={onReview}
      />,
    );
    const btn = screen.getByTestId("pantry-reserved-debt-cp_broth_debt");
    expect(btn.textContent).toMatch(/Revisar faltante/);
    expect(btn.textContent).toMatch(/200/);
    await user.click(btn);
    expect(onReview).toHaveBeenCalledTimes(1);
  });

  it("renders absence of reservation by returning null from parent pattern", () => {
    const none: PreparedComponentAvailability | null = null;
    const { container } = render(
      none ? (
        <PreparedComponentAvailabilityBar availability={none} tr={tr} />
      ) : (
        <div data-testid="no-reservation" />
      ),
    );
    expect(
      container.querySelector("[data-testid^=pantry-reserved-]"),
    ).toBeNull();
    expect(screen.getByTestId("no-reservation")).toBeInTheDocument();
  });

  it("shows multiple reservations in accessible title text", () => {
    render(
      <PreparedComponentAvailabilityBar
        availability={MOCK_PREPARED_COMPONENT_AVAILABLE}
        tr={tr}
      />,
    );
    expect(screen.getByText(/2 receitas/)).toBeInTheDocument();
  });
});
