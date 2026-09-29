import { useEffect, useId, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useRecipeRepository } from "./RecipeProvider";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import {
  RecipeApiError,
  type RecipeCandidate,
  type RecipeGenerationSession,
} from "@/contracts/recipes";
import { InventoryApiError } from "@/adapters/live/inventoryTypes";
import { useSession } from "@/app/session/SessionProvider";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import { isRetryableRecipeError, recipeErrorMessageKey } from "./recipeErrors";

type Phase =
  | "checking-inventory"
  | "empty-inventory"
  | "confirm"
  | "requesting"
  | "candidates"
  | "selecting"
  | "error";

const SESSION_POLL_INTERVAL_MS = 750;
const SESSION_POLL_MAX_ATTEMPTS = 80;

function sleep(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException("Aborted", "AbortError"));
      return;
    }
    const timer = window.setTimeout(() => resolve(), ms);
    signal.addEventListener(
      "abort",
      () => {
        window.clearTimeout(timer);
        reject(new DOMException("Aborted", "AbortError"));
      },
      { once: true },
    );
  });
}

/**
 * Cook-now generation: confirm constraints → three candidates → select → saved recipe.
 * Polls GET session while status is AwaitingCandidates or Expanding; never invents recipes.
 */
export function RecipeGeneratePage() {
  const recipeRepo = useRecipeRepository();
  const inventoryRepo = useInventoryRepository();
  const { session } = useSession();
  const { t } = useProductionI18n();
  const navigate = useNavigate();
  const candidatesHeadingId = useId();

  const [phase, setPhase] = useState<Phase>("checking-inventory");
  const [sessionView, setSessionView] =
    useState<RecipeGenerationSession | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [requestIdempotencyKey, setRequestIdempotencyKey] = useState(() =>
    crypto.randomUUID(),
  );
  const selectIdempotencyRef = useRef<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    abortRef.current = controller;
    setPhase("checking-inventory");
    void (async () => {
      try {
        const page = await inventoryRepo.listLots({
          status: "active",
          pageSize: 1,
          signal: controller.signal,
        });
        if (controller.signal.aborted) return;
        setPhase(page.items.length === 0 ? "empty-inventory" : "confirm");
      } catch (err) {
        if (controller.signal.aborted) return;
        if (err instanceof InventoryApiError && err.code === "cancelled") {
          return;
        }
        // Fail open to confirm when inventory probe fails; generation still
        // depends on backend context assembly.
        setPhase("confirm");
      }
    })();
    return () => controller.abort();
  }, [inventoryRepo]);

  function cancelInFlight() {
    abortRef.current?.abort();
    abortRef.current = null;
    setPhase((current) =>
      current === "requesting" || current === "selecting" ? "confirm" : current,
    );
    setErrorKey("recipes.generate.cancelled");
  }

  async function pollGenerationSession(
    sessionId: string,
    signal: AbortSignal,
    isTerminal: (status: string) => boolean,
  ): Promise<RecipeGenerationSession> {
    let latest: RecipeGenerationSession | null = null;
    for (let attempt = 0; attempt < SESSION_POLL_MAX_ATTEMPTS; attempt += 1) {
      latest = await recipeRepo.getGenerationSession(sessionId, signal);
      if (isTerminal(latest.status)) {
        return latest;
      }
      await sleep(SESSION_POLL_INTERVAL_MS, signal);
    }
    throw new RecipeApiError(
      "unavailable",
      "Generation session polling timed out.",
      504,
    );
  }

  async function resolveCandidatesSession(
    initial: RecipeGenerationSession,
    signal: AbortSignal,
  ): Promise<RecipeGenerationSession> {
    if (
      initial.status === "CandidatesReady" &&
      initial.candidates?.length === 3
    ) {
      return initial;
    }
    if (initial.status === "Failed") {
      return initial;
    }
    if (
      initial.status === "AwaitingCandidates" ||
      initial.status === "Expanding"
    ) {
      return pollGenerationSession(
        initial.sessionId,
        signal,
        (status) =>
          status === "CandidatesReady" ||
          status === "Failed" ||
          status === "Selected",
      );
    }
    return initial;
  }

  async function requestCandidates() {
    if (!session.csrfToken) {
      setPhase("error");
      setErrorKey("recipes.error.session");
      setErrorCode("validation_failed");
      return;
    }
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setPhase("requesting");
    setErrorKey(null);
    setErrorCode(null);
    setSessionView(null);
    setSelectedId(null);
    selectIdempotencyRef.current = null;
    try {
      const initial = await recipeRepo.requestCandidates({
        csrfToken: session.csrfToken,
        idempotencyKey: requestIdempotencyKey,
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      const result = await resolveCandidatesSession(initial, controller.signal);
      if (controller.signal.aborted) return;
      if (result.status === "Failed") {
        setPhase("error");
        setErrorKey("recipes.error.invalidOutput");
        setErrorCode("ai_output_invalid");
        setRequestIdempotencyKey(crypto.randomUUID());
        return;
      }
      if (!result.candidates || result.candidates.length !== 3) {
        setPhase("error");
        setErrorKey("recipes.error.invalidOutput");
        setErrorCode("ai_output_invalid");
        setRequestIdempotencyKey(crypto.randomUUID());
        return;
      }
      setSessionView(result);
      setPhase("candidates");
    } catch (err) {
      if (controller.signal.aborted) return;
      if (err instanceof DOMException && err.name === "AbortError") {
        setPhase("confirm");
        setErrorKey("recipes.generate.cancelled");
        return;
      }
      if (err instanceof RecipeApiError && err.code === "cancelled") {
        setPhase("confirm");
        setErrorKey("recipes.generate.cancelled");
        return;
      }
      const code =
        err instanceof RecipeApiError ? err.code : ("unexpected" as const);
      setPhase("error");
      setErrorCode(code);
      setErrorKey(
        err instanceof RecipeApiError
          ? recipeErrorMessageKey(err.code)
          : "recipes.error.unexpected",
      );
      // New user intent after failure gets a fresh idempotency key.
      setRequestIdempotencyKey(crypto.randomUUID());
    }
  }

  async function selectCandidate(candidate: RecipeCandidate) {
    if (!session.csrfToken || !sessionView) {
      setPhase("error");
      setErrorKey("recipes.error.session");
      setErrorCode("validation_failed");
      return;
    }
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    setPhase("selecting");
    setSelectedId(candidate.candidateId);
    setErrorKey(null);
    if (!selectIdempotencyRef.current) {
      selectIdempotencyRef.current = crypto.randomUUID();
    }
    const selectKey = selectIdempotencyRef.current;
    try {
      const detail = await recipeRepo.selectCandidate(
        sessionView.sessionId,
        candidate.candidateId,
        {
          csrfToken: session.csrfToken,
          idempotencyKey: selectKey,
          signal: controller.signal,
        },
      );
      if (controller.signal.aborted) return;
      navigate(`/app/receitas/${detail.recipeId}`, { replace: false });
    } catch (err) {
      if (controller.signal.aborted) return;
      if (err instanceof DOMException && err.name === "AbortError") {
        setPhase("candidates");
        setErrorKey("recipes.generate.cancelled");
        return;
      }
      if (err instanceof RecipeApiError && err.code === "cancelled") {
        setPhase("candidates");
        setErrorKey("recipes.generate.cancelled");
        return;
      }
      // Concurrent same-key Expanding: poll until Selected/Failed, then replay select.
      if (
        err instanceof RecipeApiError &&
        err.code === "ai_operation_conflict"
      ) {
        try {
          const polled = await pollGenerationSession(
            sessionView.sessionId,
            controller.signal,
            (status) => status === "Selected" || status === "Failed",
          );
          if (controller.signal.aborted) return;
          if (polled.status === "Failed") {
            setPhase("error");
            setErrorCode("ai_output_invalid");
            setErrorKey("recipes.error.invalidOutput");
            selectIdempotencyRef.current = null;
            return;
          }
          const detail = await recipeRepo.selectCandidate(
            sessionView.sessionId,
            candidate.candidateId,
            {
              csrfToken: session.csrfToken,
              idempotencyKey: selectKey,
              signal: controller.signal,
            },
          );
          if (controller.signal.aborted) return;
          navigate(`/app/receitas/${detail.recipeId}`, { replace: false });
          return;
        } catch (pollErr) {
          if (controller.signal.aborted) return;
          if (
            pollErr instanceof DOMException &&
            pollErr.name === "AbortError"
          ) {
            setPhase("candidates");
            setErrorKey("recipes.generate.cancelled");
            return;
          }
          const code =
            pollErr instanceof RecipeApiError
              ? pollErr.code
              : ("unexpected" as const);
          setPhase("error");
          setErrorCode(code);
          setErrorKey(
            pollErr instanceof RecipeApiError
              ? recipeErrorMessageKey(pollErr.code)
              : "recipes.error.unexpected",
          );
          selectIdempotencyRef.current = null;
          return;
        }
      }
      const code =
        err instanceof RecipeApiError ? err.code : ("unexpected" as const);
      setPhase("error");
      setErrorCode(code);
      setErrorKey(
        err instanceof RecipeApiError
          ? recipeErrorMessageKey(err.code)
          : "recipes.error.unexpected",
      );
      selectIdempotencyRef.current = null;
    }
  }

  function retryAfterError() {
    setErrorKey(null);
    setErrorCode(null);
    setSessionView(null);
    setSelectedId(null);
    selectIdempotencyRef.current = null;
    setRequestIdempotencyKey(crypto.randomUUID());
    setPhase("confirm");
  }

  if (phase === "checking-inventory") {
    return (
      <div data-testid="recipes-generate-loading" role="status">
        <p>{t("recipes.loading")}</p>
      </div>
    );
  }

  if (phase === "empty-inventory") {
    return (
      <div data-testid="recipes-generate-empty-inventory" className="space-y-4">
        <FeatureUnavailable
          feature="recipes-empty-inventory"
          title={t("recipes.generate.emptyInventoryTitle")}
          detail={t("recipes.generate.emptyInventoryDetail")}
        />
        <div className="flex flex-wrap gap-2">
          <Button asChild data-testid="recipes-open-inventory">
            <Link to="/app/despensa">{t("recipes.actions.openInventory")}</Link>
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  if (phase === "error") {
    const retryable =
      !errorCode ||
      isRetryableRecipeError(
        errorCode as Parameters<typeof isRetryableRecipeError>[0],
      );
    return (
      <div data-testid="recipes-generate-error" className="space-y-4">
        <FeatureUnavailable
          feature="recipes-generate"
          title={t("feature.serviceUnavailable")}
          detail={t(errorKey ?? "recipes.error.unexpected")}
        />
        <div className="flex flex-wrap gap-2">
          {retryable ? (
            <Button
              type="button"
              data-testid="recipes-generate-retry"
              onClick={retryAfterError}
            >
              {t("recipes.actions.retry")}
            </Button>
          ) : null}
          <Button asChild variant="secondary">
            <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  if (phase === "requesting" || phase === "selecting") {
    return (
      <div
        data-testid="recipes-generate-busy"
        className="space-y-4"
        role="status"
      >
        <p>
          {phase === "requesting"
            ? t("recipes.generate.requesting")
            : t("recipes.generate.selecting")}
          <span className="sr-only">
            {t("recipes.generate.waitingSession")}
          </span>
        </p>
        <Button
          type="button"
          variant="secondary"
          data-testid="recipes-generate-cancel"
          onClick={cancelInFlight}
        >
          {t("recipes.actions.cancel")}
        </Button>
      </div>
    );
  }

  if (phase === "candidates" && sessionView?.candidates) {
    return (
      <div data-testid="recipes-generate-candidates" className="space-y-6">
        <div>
          <h1 id={candidatesHeadingId} className="font-display text-3xl">
            {t("recipes.generate.candidatesTitle")}
          </h1>
          <p className="mt-1 text-muted-foreground">
            {t("recipes.generate.candidatesHint")}
          </p>
        </div>
        {errorKey ? (
          <p role="status" className="text-sm text-warning-foreground">
            {t(errorKey)}
          </p>
        ) : null}
        <ul
          className="space-y-3"
          aria-labelledby={candidatesHeadingId}
          data-testid="recipes-candidate-list"
        >
          {sessionView.candidates.map((candidate) => (
            <li key={candidate.candidateId}>
              <article
                data-testid={`recipes-candidate-${candidate.candidateId}`}
                className="rounded-2xl border border-border bg-card p-4"
              >
                <h2 className="font-display text-xl">{candidate.name}</h2>
                <p className="mt-2 text-sm text-muted-foreground">
                  {candidate.summary}
                </p>
                <div className="mt-3 flex flex-wrap gap-3 text-xs text-muted-foreground">
                  <span>
                    {t("recipes.generate.minutes", {
                      count: candidate.totalMinutes,
                    })}
                  </span>
                  <span>
                    {t("recipes.generate.servings", {
                      count: candidate.servings,
                    })}
                  </span>
                  <span>
                    {t("recipes.generate.difficulty", {
                      value: candidate.difficulty,
                    })}
                  </span>
                  <span>{candidate.targetMealType}</span>
                </div>
                <Button
                  type="button"
                  className="mt-4"
                  data-testid={`recipes-select-${candidate.candidateId}`}
                  aria-label={`${t("recipes.actions.selectCandidate")}: ${candidate.name}`}
                  disabled={Boolean(selectedId)}
                  onClick={() => void selectCandidate(candidate)}
                >
                  {t("recipes.actions.selectCandidate")}
                </Button>
              </article>
            </li>
          ))}
        </ul>
        <Button asChild variant="secondary">
          <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  return (
    <div data-testid="recipes-generate-confirm" className="space-y-6">
      <div>
        <h1 className="font-display text-3xl">{t("recipes.generate.title")}</h1>
        <p className="mt-1 text-muted-foreground">
          {t("recipes.generate.subtitle")}
        </p>
      </div>
      <p className="text-sm text-muted-foreground">
        {t("recipes.generate.confirmDetail")}
      </p>
      {errorKey ? (
        <p role="status" className="text-sm text-warning-foreground">
          {t(errorKey)}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          data-testid="recipes-confirm-generate"
          disabled={!session.csrfToken}
          onClick={() => void requestCandidates()}
        >
          {t("recipes.actions.confirmGenerate")}
        </Button>
        <Button asChild variant="secondary">
          <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
        </Button>
      </div>
      {!session.csrfToken ? (
        <p role="status" className="text-sm text-muted-foreground">
          {t("recipes.error.session")}
        </p>
      ) : null}
    </div>
  );
}
