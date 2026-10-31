import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { QuickActionMenu } from "./QuickActionMenu";

describe("main button half-moon menu (PLAN-0059)", () => {
  it("opens add-to-pantry and cook, focuses the first, and closes on Escape", async () => {
    const user = userEvent.setup();
    render(
      <ProductionI18nProvider initialLocale="en">
        <MemoryRouter>
          <QuickActionMenu active={false} />
        </MemoryRouter>
      </ProductionI18nProvider>,
    );
    const trigger = screen.getByTestId("production-nav-add");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByTestId("quick-action-cook")).toBeNull();

    await user.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByTestId("quick-action-pantry")).toHaveAttribute(
      "href",
      "/app/despensa/novo",
    );
    expect(screen.getByTestId("quick-action-cook")).toHaveAttribute(
      "href",
      "/app/cozinhar",
    );
    expect(screen.getByTestId("quick-action-pantry")).toHaveFocus();

    await user.keyboard("{Escape}");
    expect(screen.queryByTestId("quick-action-cook")).toBeNull();
    expect(trigger).toHaveFocus();

    // A tap outside closes it too.
    await user.click(trigger);
    await user.click(screen.getByTestId("quick-action-backdrop"));
    expect(screen.queryByTestId("quick-action-pantry")).toBeNull();
  });
});
