import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ProductionI18nProvider } from "@/app/i18n/ProductionI18nProvider";
import type {
  HomeQuickChooserDefinition,
  HomeSourceResult,
} from "@/contracts/contextualHome";
import { homeCatalogText } from "@/contracts/contextualHome";
import { QuickChooser } from "./QuickChooser";

const twoQuestionDef: HomeQuickChooserDefinition = {
  capabilityStatus: "available",
  questions: [
    {
      id: "time",
      promptKey: "home.chooser.q.time",
      options: [
        { id: "under_20", labelKey: "home.chooser.a.under20" },
        { id: "under_45", labelKey: "home.chooser.a.about40" },
      ],
    },
    {
      id: "priority",
      promptKey: "home.chooser.q.shopping",
      options: [
        { id: "use_what_i_have", labelKey: "home.chooser.a.useWhatIHave" },
        { id: "ok_to_buy", labelKey: "home.chooser.a.okToBuy" },
      ],
    },
  ],
};

const oneQuestionDef: HomeQuickChooserDefinition = {
  capabilityStatus: "available",
  questions: [
    {
      id: "only",
      promptKey: "home.chooser.q.time",
      options: [
        { id: "under_20", labelKey: "home.chooser.a.under20" },
        { id: "about_40", labelKey: "home.chooser.a.about40" },
      ],
    },
  ],
};

function renderChooser(
  definition: HomeQuickChooserDefinition,
  overrides?: Partial<{
    onCancel: () => void;
    onRetry: () => void;
    onComplete: (result: HomeSourceResult) => void;
    onLoadSuggestions: (
      answers: Record<string, string>,
      signal: AbortSignal,
    ) => Promise<HomeSourceResult>;
  }>,
) {
  const onCancel = overrides?.onCancel ?? vi.fn();
  const onRetry = overrides?.onRetry ?? vi.fn();
  const onComplete = overrides?.onComplete ?? vi.fn();
  const onLoadSuggestions =
    overrides?.onLoadSuggestions ??
    (async () =>
      ({
        tier: "quickChooser",
        status: "empty",
        retryable: false,
        items: [],
      }) satisfies HomeSourceResult);
  const telemetry = { track: vi.fn() };
  const view = render(
    <ProductionI18nProvider initialLocale="en">
      <QuickChooser
        definition={definition}
        telemetry={telemetry}
        onCancel={onCancel}
        onRetry={onRetry}
        onComplete={onComplete}
        onLoadSuggestions={onLoadSuggestions}
      />
    </ProductionI18nProvider>,
  );
  return {
    ...view,
    onCancel,
    onRetry,
    onComplete,
    onLoadSuggestions,
    telemetry,
  };
}

describe("QuickChooser", () => {
  it("resets to the first question when definition changes while open", async () => {
    const user = userEvent.setup();
    const { rerender, telemetry } = renderChooser(twoQuestionDef);
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    expect(
      screen.getByTestId("chooser-option-use_what_i_have"),
    ).toBeInTheDocument();

    rerender(
      <ProductionI18nProvider initialLocale="en">
        <QuickChooser
          definition={oneQuestionDef}
          telemetry={telemetry}
          onCancel={vi.fn()}
          onRetry={vi.fn()}
          onComplete={vi.fn()}
          onLoadSuggestions={async () => ({
            tier: "quickChooser",
            status: "empty",
            retryable: false,
            items: [],
          })}
        />
      </ProductionI18nProvider>,
    );

    await waitFor(() => {
      expect(screen.getByTestId("chooser-option-under_20")).toBeInTheDocument();
    });
    expect(
      screen.queryByTestId("chooser-option-use_what_i_have"),
    ).not.toBeInTheDocument();
  });

  it("retries after a failed suggestion load", async () => {
    const user = userEvent.setup();
    let attempt = 0;
    const onComplete = vi.fn();
    const { telemetry } = renderChooser(oneQuestionDef, {
      onComplete,
      onLoadSuggestions: async () => {
        attempt += 1;
        if (attempt === 1) {
          throw new Error("transient");
        }
        return {
          tier: "quickChooser",
          status: "ready",
          retryable: false,
          items: [
            {
              id: "recovered",
              title: homeCatalogText("home.fixture.chooser.simpleSoup"),
              sourceTier: "quickChooser",
              sourceLabelKey: "home.source.label.quickChooser",
              reasonCodes: ["matches_request_answers"],
            },
          ],
        };
      },
    });
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(telemetry.track).not.toHaveBeenCalledWith(
      expect.objectContaining({ name: "quick_chooser_completed" }),
    );
    await user.click(screen.getByTestId("chooser-retry"));
    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1));
    expect(onComplete.mock.calls[0][0].items[0].id).toBe("recovered");
    expect(telemetry.track).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "quick_chooser_completed",
        codes: expect.objectContaining({ outcome: "ready" }),
      }),
    );
  });

  it("keeps answers and retries after a resolved failed+retryable result", async () => {
    const user = userEvent.setup();
    let attempt = 0;
    const onComplete = vi.fn();
    const { telemetry } = renderChooser(oneQuestionDef, {
      onComplete,
      onLoadSuggestions: async () => {
        attempt += 1;
        if (attempt === 1) {
          return {
            tier: "quickChooser",
            status: "failed",
            retryable: true,
            statusReasonKey: "home.chooser.loadFailed",
            items: [],
          };
        }
        return {
          tier: "quickChooser",
          status: "ready",
          retryable: false,
          items: [
            {
              id: "after-fail",
              title: homeCatalogText("home.fixture.chooser.simpleSoup"),
              sourceTier: "quickChooser",
              sourceLabelKey: "home.source.label.quickChooser",
              reasonCodes: ["matches_request_answers"],
            },
          ],
        };
      },
    });
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    expect(await screen.findByTestId("chooser-error")).toBeInTheDocument();
    expect(screen.getByTestId("chooser-option-under_20")).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    expect(telemetry.track).not.toHaveBeenCalledWith(
      expect.objectContaining({ name: "quick_chooser_completed" }),
    );
    await user.click(screen.getByTestId("chooser-retry"));
    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1));
    expect(attempt).toBe(2);
  });

  it("shows permanent unavailable suggestion without retry or completion telemetry", async () => {
    const user = userEvent.setup();
    const onComplete = vi.fn();
    const { telemetry } = renderChooser(oneQuestionDef, {
      onComplete,
      onLoadSuggestions: async () => ({
        tier: "quickChooser",
        status: "unavailable",
        retryable: false,
        statusReasonKey: "home.chooser.unavailable",
        items: [],
      }),
    });
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    expect(await screen.findByTestId("chooser-error")).toBeInTheDocument();
    expect(screen.queryByTestId("chooser-retry")).not.toBeInTheDocument();
    expect(onComplete).not.toHaveBeenCalled();
    expect(telemetry.track).not.toHaveBeenCalledWith(
      expect.objectContaining({ name: "quick_chooser_completed" }),
    );
  });

  it("emits completed with empty outcome for empty suggestion results", async () => {
    const user = userEvent.setup();
    const onComplete = vi.fn();
    const { telemetry } = renderChooser(oneQuestionDef, {
      onComplete,
      onLoadSuggestions: async () => ({
        tier: "quickChooser",
        status: "empty",
        retryable: false,
        statusReasonKey: "home.chooser.empty",
        items: [],
      }),
    });
    await user.click(screen.getByTestId("chooser-option-under_20"));
    await user.click(screen.getByTestId("chooser-next"));
    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1));
    expect(telemetry.track).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "quick_chooser_completed",
        codes: expect.objectContaining({ outcome: "empty" }),
      }),
    );
  });
});
