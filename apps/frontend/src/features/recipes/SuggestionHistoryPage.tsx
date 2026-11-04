import { useEffect, useRef, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Clock, History, Quote, Sparkles, Users } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { EmptyState } from "@/components/feedback/EmptyState";
import { Button } from "@/components/ui/button";
import {
  RecipeApiError,
  type SuggestionHistoryCandidate,
  type SuggestionHistoryEntry,
} from "@/contracts/recipes";
import { useRecipeRepository } from "./RecipeProvider";
import { recipeErrorMessageKey } from "./recipeErrors";

type Loaded =
  | { status: "loading" }
  | { status: "failed" }
  | { status: "ready"; entries: SuggestionHistoryEntry[] };

/**
 * "Sugestões anteriores" (PLAN-0060, owner request 4): every past suggestion request with its
 * candidates, so the person can change their mind later. A candidate that already became a recipe
 * links to it; any other can still be asked for, however old the request is, and is written against
 * today's pantry. "Remover" drops the whole request from the list and forgets its hint.
 *
 * Each ask keeps one Idempotency-Key per candidate until it succeeds or fails, so a double tap or a
 * retry returns the same recipe instead of making two.
 */
export function SuggestionHistoryPage() {
  const repo = useRecipeRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const navigate = useNavigate();
  const [state, setState] = useState<Loaded>({ status: "loading" });
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const keys = useRef(new Map<string, string>());

  useEffect(() => {
    const controller = new AbortController();
    setState({ status: "loading" });
    repo
      .listSuggestionHistory(controller.signal)
      .then((entries) => setState({ status: "ready", entries }))
      .catch(() => {
        if (!controller.signal.aborted) setState({ status: "failed" });
      });
    return () => controller.abort();
  }, [repo, reload]);

  async function ask(
    entry: SuggestionHistoryEntry,
    item: SuggestionHistoryCandidate,
  ) {
    if (!session.csrfToken) return;
    const id = `${entry.sessionId}:${item.candidate.candidateId}`;
    const key = keys.current.get(id) ?? crypto.randomUUID();
    keys.current.set(id, key);
    setBusy(id);
    setErrorKey(null);
    try {
      const recipe = await repo.reopenHistoryCandidate(
        entry.sessionId,
        item.candidate.candidateId,
        {
          csrfToken: session.csrfToken,
          idempotencyKey: key,
          language: locale,
        },
      );
      navigate(`/app/receitas/${recipe.recipeId}`);
    } catch (err) {
      // A failed attempt may be retried with a fresh key.
      keys.current.delete(id);
      setErrorKey(
        err instanceof RecipeApiError
          ? recipeErrorMessageKey(err.code)
          : "recipes.error.unexpected",
      );
      setBusy(null);
    }
  }

  async function remove(entry: SuggestionHistoryEntry) {
    if (!session.csrfToken) return;
    setBusy(entry.sessionId);
    setErrorKey(null);
    try {
      await repo.removeFromSuggestionHistory(entry.sessionId, {
        csrfToken: session.csrfToken,
      });
      setState((current) =>
        current.status === "ready"
          ? {
              status: "ready",
              entries: current.entries.filter(
                (item) => item.sessionId !== entry.sessionId,
              ),
            }
          : current,
      );
    } catch {
      setErrorKey("recipes.suggestionHistory.removeError");
    } finally {
      setBusy(null);
    }
  }

  const when = (iso: string) =>
    new Date(iso).toLocaleDateString(locale, {
      day: "numeric",
      month: "long",
      hour: "2-digit",
      minute: "2-digit",
    });

  return (
    <div data-testid="suggestion-history" className="space-y-6">
      <header>
        <h1 className="font-display text-3xl">
          {t("recipes.suggestionHistory.title")}
        </h1>
        <p className="mt-1 text-muted-foreground">
          {t("recipes.suggestionHistory.subtitle")}
        </p>
      </header>

      {errorKey ? (
        <p role="alert" className="text-sm text-destructive">
          {t(errorKey)}
        </p>
      ) : null}

      {state.status === "loading" ? (
        <ul aria-hidden="true" className="space-y-3">
          {[0, 1].map((index) => (
            <li key={index} className="surface space-y-3 p-4">
              <div className="h-4 w-1/3 rounded bg-secondary motion-safe:animate-pulse" />
              <div className="h-4 w-3/4 rounded bg-secondary motion-safe:animate-pulse" />
              <div className="h-4 w-2/3 rounded bg-secondary motion-safe:animate-pulse" />
            </li>
          ))}
        </ul>
      ) : state.status === "failed" ? (
        <div className="space-y-3">
          <p role="alert" className="text-sm text-muted-foreground">
            {t("recipes.suggestionHistory.loadError")}
          </p>
          <Button
            type="button"
            variant="secondary"
            onClick={() => setReload((value) => value + 1)}
          >
            {t("recipes.actions.retry")}
          </Button>
        </div>
      ) : state.entries.length === 0 ? (
        <>
          <EmptyState
            testId="suggestion-history-empty"
            icon={History}
            title={t("recipes.suggestionHistory.empty")}
            detail={t("recipes.suggestionHistory.emptyDetail")}
          />
          <Button asChild>
            <Link to="/app/receitas/gerar">
              <Sparkles aria-hidden="true" />
              {t("recipes.suggestionHistory.suggest")}
            </Link>
          </Button>
        </>
      ) : (
        <ul className="space-y-6">
          {state.entries.map((entry) => (
            <li
              key={entry.sessionId}
              data-testid={`suggestion-history-entry-${entry.sessionId}`}
              className="space-y-3"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="text-sm font-semibold text-muted-foreground">
                  {when(entry.createdAt)}
                </h2>
                <Button
                  type="button"
                  size="sm"
                  variant="ghost"
                  disabled={busy !== null}
                  aria-label={`${t("recipes.suggestionHistory.remove")}: ${when(entry.createdAt)}`}
                  data-testid={`suggestion-history-remove-${entry.sessionId}`}
                  onClick={() => void remove(entry)}
                >
                  {t("recipes.suggestionHistory.remove")}
                </Button>
              </div>
              {entry.hint || entry.adaptation ? (
                <div className="space-y-1 text-sm text-muted-foreground">
                  {entry.hint ? (
                    <p className="flex gap-2">
                      <Quote
                        aria-hidden="true"
                        className="mt-0.5 h-4 w-4 shrink-0"
                      />
                      {/* The person's own text; rendered as text, never as markup. */}
                      <span className="min-w-0 break-words">{entry.hint}</span>
                    </p>
                  ) : null}
                  {entry.adaptation ? (
                    <p>
                      {t(`recipes.guidance.adaptation.${entry.adaptation}`)}
                    </p>
                  ) : null}
                </div>
              ) : null}
              <ul className="surface divide-y divide-border overflow-hidden">
                {entry.candidates.map((item) => {
                  const id = `${entry.sessionId}:${item.candidate.candidateId}`;
                  return (
                    <li
                      key={item.candidate.candidateId}
                      data-testid={`suggestion-history-candidate-${item.candidate.candidateId}`}
                      className="space-y-2 p-4"
                    >
                      <h3 className="font-semibold leading-snug">
                        {item.candidate.name}
                      </h3>
                      <p className="text-sm leading-relaxed text-muted-foreground">
                        {item.candidate.summary}
                      </p>
                      <ul className="flex flex-wrap gap-2 text-xs font-medium text-muted-foreground">
                        <li className="inline-flex items-center gap-1 rounded-full bg-secondary px-2.5 py-1">
                          <Clock aria-hidden="true" className="h-3.5 w-3.5" />
                          {t("recipes.generate.minutes", {
                            count: item.candidate.totalMinutes,
                          })}
                        </li>
                        <li className="inline-flex items-center gap-1 rounded-full bg-secondary px-2.5 py-1">
                          <Users aria-hidden="true" className="h-3.5 w-3.5" />
                          {t("recipes.generate.servings", {
                            count: item.candidate.servings,
                          })}
                        </li>
                      </ul>
                      {item.recipeId ? (
                        <Button asChild size="sm" variant="secondary">
                          <Link to={`/app/receitas/${item.recipeId}`}>
                            {t("recipes.suggestionHistory.open")}
                          </Link>
                        </Button>
                      ) : (
                        <Button
                          type="button"
                          size="sm"
                          disabled={busy !== null || !session.csrfToken}
                          aria-label={`${t("recipes.suggestionHistory.ask")}: ${item.candidate.name}`}
                          data-testid={`suggestion-history-ask-${item.candidate.candidateId}`}
                          onClick={() => void ask(entry, item)}
                        >
                          {busy === id
                            ? t("recipes.suggestionHistory.asking")
                            : t("recipes.suggestionHistory.ask")}
                        </Button>
                      )}
                    </li>
                  );
                })}
              </ul>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
