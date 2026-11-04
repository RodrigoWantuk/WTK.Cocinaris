import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import { SessionProvider } from "@/app/session/SessionProvider";
import { createSessionAdapter } from "@/features/inventory/testUtils";
import type { RecipeRepository, SuggestionGuidance } from "@/contracts/recipes";
import { RecipeApiError } from "@/contracts/recipes";
import { RecipeProvider } from "./RecipeProvider";
import { RecipeAiRevisionPage } from "./RecipeAiRevisionPage";
import { SuggestionHistoryPage } from "./SuggestionHistoryPage";
import {
  EMPTY_GUIDANCE,
  guidanceCount,
  SuggestionGuidancePanel,
} from "./SuggestionGuidancePanel";
import { executionStubs, libraryDetail } from "./testExecutionStubs";

function renderWith(repo: Partial<RecipeRepository>, path: string) {
  return render(
    <ProductionI18nProvider initialLocale="en">
      <SessionProvider adapter={createSessionAdapter()}>
        <RecipeProvider repository={{ ...executionStubs, ...repo } as never}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/sugestoes" element={<SuggestionHistoryPage />} />
              <Route
                path="/app/receitas/:recipeId/revisar"
                element={<RecipeAiRevisionPage />}
              />
              <Route
                path="/app/receitas/:recipeId"
                element={<p data-testid="recipe-page" />}
              />
            </Routes>
          </MemoryRouter>
        </RecipeProvider>
      </SessionProvider>
    </ProductionI18nProvider>,
  );
}

const candidate = (candidateId: string, name: string) => ({
  candidateId,
  candidateStrategy: "on_hand_first",
  name,
  targetMealType: "dinner",
  dishFormat: "bowl",
  primaryTechnique: "simmer",
  primaryIngredientRefs: ["rice"],
  summary: `${name} com aroma de alho.`,
  servings: 2,
  activeMinutes: 10,
  passiveMinutes: 10,
  totalMinutes: 20,
  difficulty: "easy",
  requiredEquipmentIds: [],
  requiredCapabilities: [],
});

const recipeJson = (names: string[]) =>
  JSON.stringify({
    name: "Arroz",
    ingredients: names.map((displayName, index) => ({
      ingredientId: `i${index}`,
      sourceType: "additional",
      displayName,
      requiredQuantity: 1,
      unit: "unit",
      optional: false,
    })),
    stages: [],
  });

const detail = libraryDetail({
  recipeId: "r1",
  revisionNumber: 1,
  name: "Arroz de forno",
  mealTypes: ["dinner"],
  servings: 2,
  normalizedRecipeJson: recipeJson(["Arroz", "Sal"]),
  thumbnailVisualJson: "{}",
  createdAt: "2026-11-01T10:00:00Z",
});

const proposal = {
  proposalId: "p1",
  recipeId: "r1",
  baseRevisionNumber: 1,
  instruction: "<b>sem sal</b>, com limão",
  status: "Ready",
  name: "Arroz de forno com limão",
  mealTypes: ["dinner"],
  servings: 2,
  normalizedRecipeJson: recipeJson(["Arroz", "Limão"]),
  acceptedRevisionNumber: null,
  createdAt: "2026-11-01T10:00:00Z",
};

describe("suggestion guidance panel", () => {
  function Harness({
    onValue,
  }: {
    onValue: (value: SuggestionGuidance) => void;
  }) {
    const [value, setValue] = useState(EMPTY_GUIDANCE);
    return (
      <ProductionI18nProvider initialLocale="en">
        <SuggestionGuidancePanel
          lots={[]}
          value={value}
          onChange={(next) => {
            setValue(next);
            onValue(next);
          }}
        />
      </ProductionI18nProvider>
    );
  }

  it("takes a wish of at most 300 characters and one ingredient approach", () => {
    const onValue = vi.fn();
    render(<Harness onValue={onValue} />);
    const wish = screen.getByTestId("recipes-guidance-wish");
    expect(wish).toHaveAttribute("maxlength", "300");
    fireEvent.change(wish, { target: { value: "algo com coentro" } });
    expect(onValue.mock.lastCall?.[0].hint).toBe("algo com coentro");
    expect(screen.getByText("16/300")).toBeInTheDocument();

    const faithful = screen.getByTestId(
      "recipes-guidance-adaptation-stay_faithful",
    );
    const adapted = screen.getByTestId(
      "recipes-guidance-adaptation-adapt_to_pantry",
    );
    fireEvent.click(faithful);
    expect(faithful).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(adapted);
    expect(onValue.mock.lastCall?.[0].adaptation).toBe("adapt_to_pantry");
    expect(faithful).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(adapted);
    expect(onValue.mock.lastCall?.[0].adaptation).toBeNull();
  });

  it("does not count a blank wish as a choice", () => {
    expect(guidanceCount({ ...EMPTY_GUIDANCE, hint: "   " })).toBe(0);
    expect(guidanceCount({ ...EMPTY_GUIDANCE, hint: "coentro" })).toBe(1);
    expect(
      guidanceCount({ ...EMPTY_GUIDANCE, adaptation: "stay_faithful" }),
    ).toBe(1);
  });
});

describe("suggestion history", () => {
  const entry = {
    sessionId: "s1",
    createdAt: "2026-11-01T10:00:00Z",
    hint: "<img src=x> algo cremoso",
    adaptation: "stay_faithful",
    candidates: [
      { candidate: candidate("c1", "Risoto"), recipeId: "r9" },
      { candidate: candidate("c2", "Canja"), recipeId: null },
    ],
  };

  it("links what became a recipe, asks for the rest once, and shows the hint as text", async () => {
    const reopen = vi.fn<RecipeRepository["reopenHistoryCandidate"]>(
      async () => detail,
    );
    renderWith(
      {
        listSuggestionHistory: async () => [entry],
        reopenHistoryCandidate: reopen,
      },
      "/sugestoes",
    );
    expect(await screen.findByText("Risoto")).toBeInTheDocument();
    expect(screen.getByText("Canja com aroma de alho.")).toBeInTheDocument();
    expect(screen.getByText("<img src=x> algo cremoso")).toBeInTheDocument();
    expect(document.querySelector("img[src='x']")).toBeNull();
    expect(screen.queryByTestId("suggestion-history-ask-c1")).toBeNull();

    fireEvent.click(screen.getByTestId("suggestion-history-ask-c2"));
    await screen.findByTestId("recipe-page");
    expect(reopen).toHaveBeenCalledTimes(1);
    expect(reopen.mock.calls[0]?.[0]).toBe("s1");
    expect(reopen.mock.calls[0]?.[1]).toBe("c2");
  });

  it("keeps the list and explains when asking fails, and removes an entry", async () => {
    renderWith(
      {
        listSuggestionHistory: async () => [entry],
        reopenHistoryCandidate: async () => {
          throw new RecipeApiError("ai_provider_unavailable", "x", 503);
        },
        removeFromSuggestionHistory: async () => undefined,
      },
      "/sugestoes",
    );
    fireEvent.click(await screen.findByTestId("suggestion-history-ask-c2"));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByText("Canja")).toBeInTheDocument();

    fireEvent.click(screen.getByTestId("suggestion-history-remove-s1"));
    expect(await screen.findByTestId("suggestion-history-empty")).toBeTruthy();
  });

  it("offers a retry when the list cannot load", async () => {
    const list = vi
      .fn()
      .mockRejectedValueOnce(new Error("down"))
      .mockResolvedValueOnce([]);
    renderWith({ listSuggestionHistory: list }, "/sugestoes");
    fireEvent.click(await screen.findByRole("button"));
    expect(await screen.findByTestId("suggestion-history-empty")).toBeTruthy();
  });
});

describe("AI revision", () => {
  const path = "/app/receitas/r1/revisar";

  it("shows the new version and changes the recipe only on acceptance", async () => {
    const propose = vi.fn<RecipeRepository["proposeRevision"]>(
      async () => proposal,
    );
    const accept = vi.fn(async () => detail);
    renderWith(
      {
        getRecipe: async () => detail,
        proposeRevision: propose,
        acceptRevisionProposal: accept,
      },
      path,
    );
    const field = await screen.findByTestId("ai-revision-instruction");
    expect(field).toHaveAttribute("maxlength", "300");
    expect(screen.getByTestId("ai-revision-submit")).toBeDisabled();
    fireEvent.change(field, { target: { value: "sem sal, com limão" } });
    fireEvent.click(screen.getByTestId("ai-revision-submit"));

    expect(await screen.findByTestId("ai-revision-proposal")).toBeTruthy();
    expect(propose.mock.lastCall?.[1]).toMatchObject({
      instruction: "sem sal, com limão",
      expectedRevisionNumber: 1,
    });
    expect(accept).not.toHaveBeenCalled();
    expect(screen.getByTestId("ai-revision-added")).toHaveTextContent("Limão");
    expect(screen.getByTestId("ai-revision-removed")).toHaveTextContent("Sal");
    expect(screen.getByText("<b>sem sal</b>, com limão")).toBeInTheDocument();

    fireEvent.click(screen.getByTestId("ai-revision-accept"));
    await screen.findByTestId("recipe-page");
    expect(accept).toHaveBeenCalledWith(
      "r1",
      "p1",
      expect.objectContaining({ csrfToken: expect.any(String) }),
    );
  });

  it("keeps the request after a failure, uses a new key, and can ask for another change", async () => {
    const propose = vi
      .fn()
      .mockRejectedValueOnce(new RecipeApiError("ai_output_invalid", "x", 422))
      .mockResolvedValueOnce(proposal);
    const discard = vi.fn(async () => undefined);
    renderWith(
      {
        getRecipe: async () => detail,
        proposeRevision: propose,
        discardRevisionProposal: discard,
      },
      path,
    );
    fireEvent.change(await screen.findByTestId("ai-revision-instruction"), {
      target: { value: "menos sal" },
    });
    fireEvent.click(screen.getByTestId("ai-revision-submit"));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByTestId("ai-revision-instruction")).toHaveValue(
      "menos sal",
    );

    fireEvent.click(screen.getByTestId("ai-revision-submit"));
    await screen.findByTestId("ai-revision-proposal");
    expect(propose.mock.calls[0]?.[1].idempotencyKey).not.toBe(
      propose.mock.calls[1]?.[1].idempotencyKey,
    );

    fireEvent.click(screen.getByTestId("ai-revision-again"));
    await waitFor(() => expect(discard).toHaveBeenCalledTimes(1));
    expect(await screen.findByTestId("ai-revision-instruction")).toHaveValue(
      "menos sal",
    );
  });

  it("says so when the recipe moved on since the proposal", async () => {
    renderWith(
      {
        getRecipe: async () => detail,
        proposeRevision: async () => proposal,
        acceptRevisionProposal: async () => {
          throw new RecipeApiError("precondition_failed", "x", 412);
        },
      },
      path,
    );
    fireEvent.change(await screen.findByTestId("ai-revision-instruction"), {
      target: { value: "menos sal" },
    });
    fireEvent.click(screen.getByTestId("ai-revision-submit"));
    fireEvent.click(await screen.findByTestId("ai-revision-accept"));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByTestId("ai-revision-proposal")).toBeTruthy();
  });
});
