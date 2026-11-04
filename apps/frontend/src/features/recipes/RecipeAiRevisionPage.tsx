import { useEffect, useId, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { Sparkles } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import {
  RecipeApiError,
  type RecipeDetail,
  type RecipeRevisionProposal,
} from "@/contracts/recipes";
import { useRecipeRepository } from "./RecipeProvider";
import { RecipeContent } from "./RecipeContent";
import {
  parseNormalizedRecipeJson,
  type ParsedNormalizedRecipe,
} from "./parseNormalizedRecipe";
import { recipeErrorMessageKey } from "./recipeErrors";

/** The API accepts at most this many characters in the request. */
export const MAX_REVISION_REQUEST = 300;

type Phase = "loading" | "missing" | "form" | "generating" | "proposal";

const ingredientNames = (recipe: ParsedNormalizedRecipe | null) =>
  (recipe?.ingredients ?? []).map((item) => item.displayName);

/**
 * "Revisar com IA" (PLAN-0060, owner request 6): the person says in a few words what to change, the
 * AI writes a complete new version, and the person reads it before anything happens. The recipe
 * changes only on "Salvar como nova versão", and the earlier version stays in its history.
 *
 * The request stays in the field after a failure or after "Pedir outro ajuste", so it is never lost.
 * It is rendered as plain text. One Idempotency-Key is kept per attempt until it ends, so a double
 * tap never makes a second AI call.
 */
export function RecipeAiRevisionPage() {
  const { recipeId = "" } = useParams();
  const repo = useRecipeRepository();
  const { session } = useSession();
  const { t, locale } = useProductionI18n();
  const navigate = useNavigate();
  const fieldId = useId();
  const [phase, setPhase] = useState<Phase>("loading");
  const [recipe, setRecipe] = useState<RecipeDetail | null>(null);
  const [instruction, setInstruction] = useState("");
  const [proposal, setProposal] = useState<RecipeRevisionProposal | null>(null);
  const [errorKey, setErrorKey] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const attemptKey = useRef<string | null>(null);
  const abort = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo
      .getRecipe(recipeId, controller.signal)
      .then((detail) => {
        setRecipe(detail);
        setPhase("form");
      })
      .catch(() => {
        if (!controller.signal.aborted) setPhase("missing");
      });
    return () => {
      controller.abort();
      abort.current?.abort();
    };
  }, [repo, recipeId]);

  const errorFor = (err: unknown) => {
    if (!(err instanceof RecipeApiError)) return "recipes.error.unexpected";
    if (err.code === "validation_failed") return "recipes.aiRevision.invalid";
    if (err.code === "precondition_failed") return "recipes.aiRevision.stale";
    return recipeErrorMessageKey(err.code);
  };

  async function generate() {
    if (!session.csrfToken || !recipe || !instruction.trim()) return;
    attemptKey.current ??= crypto.randomUUID();
    const controller = new AbortController();
    abort.current = controller;
    setPhase("generating");
    setErrorKey(null);
    try {
      const result = await repo.proposeRevision(recipe.recipeId, {
        csrfToken: session.csrfToken,
        idempotencyKey: attemptKey.current,
        instruction,
        expectedRevisionNumber: recipe.currentRevisionNumber,
        language: locale,
        signal: controller.signal,
      });
      if (controller.signal.aborted) return;
      attemptKey.current = null;
      setProposal(result);
      setPhase("proposal");
    } catch (err) {
      if (controller.signal.aborted) return;
      attemptKey.current = null;
      setErrorKey(errorFor(err));
      setPhase("form");
    }
  }

  function cancel() {
    abort.current?.abort();
    attemptKey.current = null;
    setPhase("form");
  }

  async function accept() {
    if (!session.csrfToken || !proposal) return;
    setSaving(true);
    setErrorKey(null);
    try {
      await repo.acceptRevisionProposal(recipeId, proposal.proposalId, {
        csrfToken: session.csrfToken,
      });
      navigate(`/app/receitas/${recipeId}`);
    } catch (err) {
      setErrorKey(errorFor(err));
      setSaving(false);
    }
  }

  // Leaving the proposal never depends on the discard succeeding: an undecided proposal changes nothing.
  async function discard(next: "again" | "leave") {
    if (session.csrfToken && proposal) {
      await repo
        .discardRevisionProposal(recipeId, proposal.proposalId, {
          csrfToken: session.csrfToken,
        })
        .catch(() => undefined);
    }

    if (next === "leave") {
      navigate(`/app/receitas/${recipeId}`);
      return;
    }

    setProposal(null);
    setErrorKey(null);
    setPhase("form");
  }

  if (phase === "loading") {
    return (
      <div role="status" data-testid="ai-revision-loading">
        <p>{t("recipes.loading")}</p>
      </div>
    );
  }

  if (phase === "missing" || !recipe) {
    return (
      <div data-testid="ai-revision-missing" className="space-y-4">
        <p role="alert">{t("recipes.detail.notFound")}</p>
        <Button asChild variant="secondary">
          <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  if (phase === "generating") {
    return (
      <div data-testid="ai-revision-busy" role="status" className="space-y-5">
        <p className="flex items-center gap-3 text-lg font-semibold">
          <Sparkles
            aria-hidden="true"
            className="h-6 w-6 text-primary motion-safe:animate-pulse"
          />
          {t("recipes.aiRevision.generating")}
        </p>
        <div aria-hidden="true" className="surface space-y-3 p-4">
          <div className="h-5 w-2/3 rounded bg-secondary motion-safe:animate-pulse" />
          <div className="h-4 w-full rounded bg-secondary motion-safe:animate-pulse" />
          <div className="h-4 w-5/6 rounded bg-secondary motion-safe:animate-pulse" />
        </div>
        <Button type="button" variant="secondary" onClick={cancel}>
          {t("recipes.actions.cancel")}
        </Button>
      </div>
    );
  }

  if (phase === "proposal" && proposal) {
    const proposed = proposal.normalizedRecipeJson
      ? parseNormalizedRecipeJson(proposal.normalizedRecipeJson)
      : null;
    const before = ingredientNames(
      parseNormalizedRecipeJson(recipe.normalizedRecipeJson),
    );
    const after = ingredientNames(proposed);
    const added = after.filter((name) => !before.includes(name));
    const removed = before.filter((name) => !after.includes(name));
    return (
      <div data-testid="ai-revision-proposal" className="space-y-6">
        <header>
          <h1 className="font-display text-3xl">
            {t("recipes.aiRevision.proposalTitle")}
          </h1>
          <p className="mt-1 text-muted-foreground">
            {t("recipes.aiRevision.proposalHint")}
          </p>
        </header>
        <section
          aria-label={t("recipes.aiRevision.changes")}
          className="rounded-2xl bg-accent p-4 text-accent-foreground"
        >
          <p className="text-sm font-semibold">
            {t("recipes.aiRevision.youAsked")}
          </p>
          <p className="mt-1 break-words">{proposal.instruction}</p>
          {added.length > 0 ? (
            <p className="mt-3 text-sm" data-testid="ai-revision-added">
              <span className="font-semibold">
                {t("recipes.aiRevision.added")}
              </span>{" "}
              {added.join(", ")}
            </p>
          ) : null}
          {removed.length > 0 ? (
            <p className="mt-1 text-sm" data-testid="ai-revision-removed">
              <span className="font-semibold">
                {t("recipes.aiRevision.removed")}
              </span>{" "}
              {removed.join(", ")}
            </p>
          ) : null}
        </section>
        <div>
          <h2 className="font-display text-2xl">{proposal.name}</h2>
          {proposal.servings ? (
            <p className="mt-1 text-sm text-muted-foreground">
              {t("recipes.generate.servings", { count: proposal.servings })}
            </p>
          ) : null}
        </div>
        {proposed ? <RecipeContent recipe={proposed} /> : null}
        {errorKey ? (
          <p role="alert" className="text-sm text-destructive">
            {t(errorKey)}
          </p>
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            data-testid="ai-revision-accept"
            disabled={saving || !session.csrfToken}
            onClick={() => void accept()}
          >
            {saving
              ? t("recipes.aiRevision.saving")
              : t("recipes.aiRevision.accept")}
          </Button>
          <Button
            type="button"
            variant="secondary"
            data-testid="ai-revision-again"
            disabled={saving}
            onClick={() => void discard("again")}
          >
            {t("recipes.aiRevision.again")}
          </Button>
          <Button
            type="button"
            variant="ghost"
            data-testid="ai-revision-discard"
            disabled={saving}
            onClick={() => void discard("leave")}
          >
            {t("recipes.aiRevision.discard")}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div data-testid="ai-revision-form" className="space-y-6">
      <header>
        <h1 className="font-display text-3xl">
          {t("recipes.aiRevision.title")}
        </h1>
        <p className="mt-1 break-words text-muted-foreground">{recipe.name}</p>
      </header>
      <form
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          void generate();
        }}
      >
        <div className="space-y-2">
          <label htmlFor={fieldId} className="font-medium">
            {t("recipes.aiRevision.label")}
          </label>
          <Textarea
            id={fieldId}
            rows={4}
            maxLength={MAX_REVISION_REQUEST}
            value={instruction}
            placeholder={t("recipes.aiRevision.placeholder")}
            aria-describedby={`${fieldId}-help`}
            data-testid="ai-revision-instruction"
            className="text-base"
            onChange={(event) => {
              setInstruction(event.target.value);
              attemptKey.current = null;
            }}
          />
          <p
            id={`${fieldId}-help`}
            className="flex justify-between gap-3 text-xs text-muted-foreground"
          >
            <span>{t("recipes.aiRevision.help")}</span>
            <span className="shrink-0 tabular-nums">
              {instruction.length}/{MAX_REVISION_REQUEST}
            </span>
          </p>
        </div>
        {errorKey ? (
          <p role="alert" className="text-sm text-destructive">
            {t(errorKey)}
          </p>
        ) : null}
        <div className="flex flex-wrap gap-2">
          <Button
            type="submit"
            data-testid="ai-revision-submit"
            disabled={!instruction.trim() || !session.csrfToken}
          >
            <Sparkles aria-hidden="true" />
            {t("recipes.aiRevision.submit")}
          </Button>
          <Button asChild variant="secondary">
            <Link to={`/app/receitas/${recipe.recipeId}`}>
              {t("recipes.actions.back")}
            </Link>
          </Button>
        </div>
      </form>
    </div>
  );
}
