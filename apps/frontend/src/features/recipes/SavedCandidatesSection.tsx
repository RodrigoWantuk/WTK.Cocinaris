import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useRecipeRepository } from "./RecipeProvider";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import type { SavedCandidate } from "@/contracts/recipes";

/**
 * "Ideias salvas" on the recipes page (PLAN-0056 Phase 3, owner report 5): candidates kept for
 * later. "Cozinhar esta" turns one into a recipe against today's pantry and opens it; "Remover"
 * drops it. Renders nothing while loading, on a load failure, or when the list is empty, so the
 * recipes page is unchanged for people who never save.
 *
 * Each cook action keeps one Idempotency-Key per entry until it succeeds or fails, so a double
 * tap or a retry returns the same recipe instead of making two.
 */
export function SavedCandidatesSection() {
  const repo = useRecipeRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const navigate = useNavigate();
  const [items, setItems] = useState<SavedCandidate[]>([]);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const keys = useRef(new Map<string, string>());

  useEffect(() => {
    const controller = new AbortController();
    repo
      .listSavedCandidates(controller.signal)
      .then(setItems)
      .catch(() => undefined);
    return () => controller.abort();
  }, [repo]);

  if (items.length === 0) return null;

  async function cook(item: SavedCandidate) {
    if (!session.csrfToken) return;
    const key = keys.current.get(item.savedId) ?? crypto.randomUUID();
    keys.current.set(item.savedId, key);
    setBusyId(item.savedId);
    setErrorKey(null);
    try {
      const recipe = await repo.cookSavedCandidate(item.savedId, {
        csrfToken: session.csrfToken,
        idempotencyKey: key,
        language: locale,
      });
      navigate(`/app/receitas/${recipe.recipeId}`);
    } catch {
      // A failed attempt may be retried with a fresh key.
      keys.current.delete(item.savedId);
      setErrorKey("library.saved.error");
      setBusyId(null);
    }
  }

  async function remove(item: SavedCandidate) {
    if (!session.csrfToken) return;
    setBusyId(item.savedId);
    setErrorKey(null);
    try {
      await repo.deleteSavedCandidate(item.savedId, {
        csrfToken: session.csrfToken,
      });
      setItems((current) =>
        current.filter((entry) => entry.savedId !== item.savedId),
      );
    } catch {
      setErrorKey("library.saved.removeError");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <section
      aria-labelledby="saved-candidates-title"
      className="space-y-3"
      data-testid="saved-candidates"
    >
      <h2 id="saved-candidates-title" className="font-display text-xl">
        {t("library.saved.title")}
      </h2>
      {errorKey ? (
        <p role="alert" className="text-sm text-destructive">
          {t(errorKey)}
        </p>
      ) : null}
      <ul className="space-y-2">
        {items.map((item) => (
          <li
            key={item.savedId}
            className="rounded-2xl border border-border bg-card p-3"
            data-testid={`saved-candidate-${item.savedId}`}
          >
            <p className="font-medium">{item.candidate.name}</p>
            <p className="mt-1 text-sm text-muted-foreground">
              {item.candidate.summary}
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              {t("recipes.generate.minutes", {
                count: item.candidate.totalMinutes,
              })}
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button
                type="button"
                size="sm"
                disabled={busyId !== null}
                aria-label={`${t("library.saved.cook")}: ${item.candidate.name}`}
                data-testid={`saved-candidate-cook-${item.savedId}`}
                onClick={() => void cook(item)}
              >
                {busyId === item.savedId
                  ? t("library.saved.cooking")
                  : t("library.saved.cook")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                disabled={busyId !== null}
                aria-label={`${t("library.saved.remove")}: ${item.candidate.name}`}
                data-testid={`saved-candidate-remove-${item.savedId}`}
                onClick={() => void remove(item)}
              >
                {t("library.saved.remove")}
              </Button>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
