import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import type { InventoryRepository } from "@/adapters/live/inventoryTypes";
import { ProductNameCombobox, splitMatch } from "./ProductNameCombobox";

function renderField(repository: Partial<InventoryRepository>) {
  function Field() {
    const [value, setValue] = useState("");
    return (
      <>
        <ProductNameCombobox
          value={value}
          onChange={setValue}
          repository={repository as InventoryRepository}
          language="pt-BR"
        />
        <output data-testid="value">{value}</output>
      </>
    );
  }
  return render(
    <ProductionI18nProvider initialLocale="pt-BR">
      <Field />
    </ProductionI18nProvider>,
  );
}

describe("pantry name suggestions (PLAN-0056 Phase 4)", () => {
  it("highlights the match ignoring case and accents", () => {
    expect(splitMatch("Açúcar refinado", "acucar")).toEqual([
      "",
      "Açúcar",
      " refinado",
    ]);
    expect(splitMatch("Pão de queijo", "QUEIJO")).toEqual([
      "Pão de ",
      "queijo",
      "",
    ]);
    expect(splitMatch("Arroz", "feijão")).toBeNull();
  });

  it("suggests while typing and picks with the keyboard or the mouse", async () => {
    const user = userEvent.setup();
    const suggestNames = vi.fn(async () => [
      { name: "Requeijão da Fazenda", source: "pantry" as const },
      { name: "Requeijão", source: "catalog" as const },
    ]);
    renderField({ suggestNames });
    const input = screen.getByRole("combobox");

    await user.type(input, "requ");
    const list = await screen.findByRole("listbox");
    await waitFor(() => expect(list).toBeVisible());
    expect(input).toHaveAttribute("aria-expanded", "true");
    expect(suggestNames).toHaveBeenLastCalledWith(
      "requ",
      expect.objectContaining({ language: "pt-BR" }),
    );
    expect(screen.getAllByRole("option")[0]).toHaveTextContent("já usado");
    expect(
      screen.getAllByRole("option")[0].querySelector("mark"),
    ).toHaveTextContent("Requ");

    await user.keyboard("{ArrowDown}{ArrowDown}");
    expect(input).toHaveAttribute(
      "aria-activedescendant",
      screen.getAllByRole("option")[1].id,
    );
    await user.keyboard("{Enter}");
    expect(screen.getByTestId("value")).toHaveTextContent("Requeijão");
    expect(input).toHaveAttribute("aria-expanded", "false");

    await user.clear(input);
    await user.type(input, "requ");
    await user.click(await screen.findByText("da Fazenda", { exact: false }));
    expect(screen.getByTestId("value")).toHaveTextContent(
      "Requeijão da Fazenda",
    );
  });

  it("closes on Escape and works as a plain field without suggestions", async () => {
    const user = userEvent.setup();
    renderField({
      suggestNames: async () => [
        { name: "Cebola", source: "catalog" as const },
      ],
    });
    const input = screen.getByRole("combobox");
    await user.type(input, "ceb");
    await waitFor(() => expect(input).toHaveAttribute("aria-expanded", "true"));
    await user.keyboard("{Escape}");
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByTestId("value")).toHaveTextContent("ceb");
  });

  it("is a plain text field when the repository cannot suggest", async () => {
    const user = userEvent.setup();
    renderField({});
    await user.type(screen.getByRole("combobox"), "Arroz");
    expect(screen.getByTestId("value")).toHaveTextContent("Arroz");
    expect(screen.getByRole("combobox")).toHaveAttribute(
      "aria-expanded",
      "false",
    );
  });
});
