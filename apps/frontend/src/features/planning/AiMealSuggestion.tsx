import { useRef, useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RecipeApiError, type RecipeCandidate } from "@/contracts/recipes";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { recipeErrorMessageKey } from "@/features/recipes/recipeErrors";
import { MEAL_TYPES, mealTypeLabel } from "@/features/recipes/recipeLabels";

/**
 * AI suggestions for one menu slot (PLAN-0029 Phase 2). The backend resolves the menu, gives the
 * model only stock no other meal counts on, and asks for dishes unlike those already planned. The
 * chosen candidate is expanded and saved as a recipe like any cook-now selection, then handed to
 * `onChosen` so the menu adds it to the slot. Nothing is added before the cook chooses.
 */
export function AiMealSuggestion({
  menuId,
  plannedDate,
  initialServings,
  onChosen,
  onCancel,
}: {
  menuId: string;
  plannedDate: string | null;
  initialServings: number;
  onChosen: (input: {
    recipeId: string;
    mealType: string | null;
    servings: number;
  }) => Promise<boolean>;
  onCancel: () => void;
}) {
  const recipes = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const [meal, setMeal] = useState("");
  const [servings, setServings] = useState(String(initialServings));
  const [phase, setPhase] = useState<"form" | "asking" | "choosing" | "saving">(
    "form",
  );
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [candidates, setCandidates] = useState<RecipeCandidate[]>([]);
  const [error, setError] = useState<string | null>(null);
  // One key per request and per choice, so a retried request never creates a second session or recipe.
  const askKey = useRef(crypto.randomUUID());
  const chooseKey = useRef(crypto.randomUUID());

  const fail = (err: unknown) => {
    setError(
      err instanceof RecipeApiError
        ? recipeErrorMessageKey(err.code)
        : "recipes.error.unexpected",
    );
  };

  const ask = async () => {
    const portions = Number(servings);
    if (
      !session.csrfToken ||
      !Number.isInteger(portions) ||
      portions < 1 ||
      portions > 24
    )
      return;
    setPhase("asking");
    setError(null);
    try {
      const result = await recipes.requestCandidates({
        csrfToken: session.csrfToken,
        idempotencyKey: askKey.current,
        language: locale,
        menuPlanning: {
          menuId,
          plannedDate,
          mealType: meal || null,
          servings: portions,
        },
      });
      setSessionId(result.sessionId);
      setCandidates(result.candidates ?? []);
      setPhase("choosing");
    } catch (err) {
      fail(err);
      askKey.current = crypto.randomUUID();
      setPhase("form");
    }
  };

  const choose = async (candidate: RecipeCandidate) => {
    if (!session.csrfToken || !sessionId) return;
    setPhase("saving");
    setError(null);
    try {
      const recipe = await recipes.selectCandidate(
        sessionId,
        candidate.candidateId,
        {
          csrfToken: session.csrfToken,
          idempotencyKey: chooseKey.current,
          language: locale,
        },
      );
      const added = await onChosen({
        recipeId: recipe.recipeId,
        mealType: meal || null,
        servings: Number(servings),
      });
      if (!added) {
        // The recipe is saved; choosing again replays the same selection and retries the add.
        setError("planning.error.save");
        setPhase("choosing");
      }
    } catch (err) {
      fail(err);
      chooseKey.current = crypto.randomUUID();
      setPhase("choosing");
    }
  };

  return (
    <div
      data-testid="menu-ai"
      className="space-y-3 rounded-xl border border-dashed border-border p-3"
    >
      <p className="text-xs text-muted-foreground">{t("planning.ai.hint")}</p>
      {phase === "form" || phase === "asking" ? (
        <div className="grid gap-2 sm:grid-cols-3 sm:items-end">
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("planning.field.meal")}
            <select
              data-testid="menu-ai-meal"
              className="h-10 w-full rounded-md border border-input bg-background px-2 text-sm text-foreground"
              value={meal}
              onChange={(event) => setMeal(event.target.value)}
            >
              <option value="">{t("planning.field.anyMeal")}</option>
              {MEAL_TYPES.map((mealType) => (
                <option key={mealType} value={mealType}>
                  {mealTypeLabel(t, mealType)}
                </option>
              ))}
            </select>
          </label>
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("editor.servings")}
            <Input
              type="number"
              min={1}
              max={24}
              data-testid="menu-ai-servings"
              value={servings}
              onChange={(event) => setServings(event.target.value)}
            />
          </label>
          <div className="flex gap-2">
            <Button
              type="button"
              size="sm"
              data-testid="menu-ai-ask"
              disabled={phase === "asking" || !session.csrfToken}
              onClick={() => void ask()}
            >
              {phase === "asking"
                ? t("planning.ai.asking")
                : t("planning.ai.ask")}
            </Button>
            <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
              {t("editor.cancel")}
            </Button>
          </div>
        </div>
      ) : (
        <ul className="space-y-2">
          {candidates.map((candidate) => (
            <li
              key={candidate.candidateId}
              data-testid="menu-ai-candidate"
              className="space-y-1 rounded-lg bg-secondary/40 p-3"
            >
              <p className="font-medium">{candidate.name}</p>
              <p className="text-sm text-muted-foreground">
                {candidate.summary}
              </p>
              <p className="text-xs text-muted-foreground">
                {t("recipes.generate.minutes", {
                  count: candidate.totalMinutes,
                })}
              </p>
              <Button
                type="button"
                size="sm"
                data-testid="menu-ai-choose"
                disabled={phase === "saving"}
                aria-label={`${t("planning.ai.choose")}: ${candidate.name}`}
                onClick={() => void choose(candidate)}
              >
                {phase === "saving"
                  ? t("planning.ai.saving")
                  : t("planning.ai.choose")}
              </Button>
            </li>
          ))}
          <li>
            <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
              {t("editor.cancel")}
            </Button>
          </li>
        </ul>
      )}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {t(error)}
        </p>
      ) : null}
    </div>
  );
}
