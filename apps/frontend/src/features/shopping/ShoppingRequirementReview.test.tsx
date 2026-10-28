import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ShoppingRequirementReview } from "./ShoppingRequirementReview";
import {
  MOCK_SHOPPING_REQUIREMENTS,
  selectShoppingShortfalls,
} from "@/adapters/mock/shoppingRequirementFixtures";
import type { ShoppingRequirementProjection } from "@/contracts/preparation";

function tr(key: string): string {
  return key;
}

describe("ShoppingRequirementReview", () => {
  it("does not send covered rows and only sends shortfallQuantity", async () => {
    const user = userEvent.setup();
    const onSend = vi.fn();
    render(
      <ShoppingRequirementReview
        tr={tr}
        projections={MOCK_SHOPPING_REQUIREMENTS}
        onSendShortfalls={onSend}
      />,
    );

    expect(
      screen.getByTestId("shopping-req-req-beans-covered"),
    ).toHaveAttribute("data-shortfall", "false");
    expect(
      screen.queryByTestId("shopping-req-check-req-beans-covered"),
    ).not.toBeInTheDocument();

    await user.click(screen.getByTestId("shopping-req-send"));
    expect(onSend).toHaveBeenCalledTimes(1);
    const sent = onSend.mock.calls[0][0] as ShoppingRequirementProjection[];
    expect(sent.every((s) => s.shortfallQuantity.value > 0)).toBe(true);
    expect(
      sent.find((s) => s.requirementId === "req-beans-covered"),
    ).toBeUndefined();
    expect(
      sent.find((s) => s.requirementId === "req-broth-shortfall")
        ?.shortfallQuantity.value,
    ).toBe(200);
  });

  it("selectShoppingShortfalls ignores zero shortfall", () => {
    const covered: ShoppingRequirementProjection = {
      ...MOCK_SHOPPING_REQUIREMENTS[2],
      shortfallQuantity: { value: 0, unit: "g" },
    };
    expect(selectShoppingShortfalls([covered])).toEqual([]);
  });

  it("shows controlled unavailable when empty and flagged", () => {
    render(
      <ShoppingRequirementReview
        tr={tr}
        projections={[]}
        showUnavailableWhenEmpty
      />,
    );
    expect(
      screen.getByTestId("feature-unavailable-shopping-requirements"),
    ).toBeInTheDocument();
  });
});
