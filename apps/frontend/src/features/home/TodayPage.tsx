import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { InventoryLotView } from "@/adapters/live/inventoryTypes";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import type {
  RecipeExecutionSummary,
  RecipeSummary,
} from "@/contracts/recipes";
import { AttentionBadge } from "@/features/inventory/AttentionBadge";
import { useInventoryRepository } from "@/features/inventory/InventoryProvider";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { buildHomeGreeting, readBrowserTimeZone } from "./dayPart";

type Source<T> =
  | { status: "loading" }
  | { status: "ready"; items: T[] }
  | { status: "failed" };

/** Loads one home source; a failure affects only its own section. */
function useSource<T>(load: (signal: AbortSignal) => Promise<T[]>): Source<T> {
  const [state, setState] = useState<Source<T>>({ status: "loading" });
  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal).then(
      (items) =>
        !controller.signal.aborted && setState({ status: "ready", items }),
      () => !controller.signal.aborted && setState({ status: "failed" }),
    );
    return () => controller.abort();
  }, [load]);
  return state;
}

/**
 * Production "Hoje" (PLAN-0021 deterministic tiers, PLAN-0042 resume). Every section reads an
 * authoritative source independently: in-progress cooking, the inventory attention list, and saved
 * recipes. None of them mutates state, and a failed source hides only its own section's content.
 * The menu tier waits for PLAN-0029; nothing here is a fixture.
 */
export function TodayPage() {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const recipes = useRecipeRepository();
  const inventory = useInventoryRepository();

  const cooking = useSource<RecipeExecutionSummary>(
    useMemo(
      () => (signal: AbortSignal) => recipes.listExecutionsInProgress(signal),
      [recipes],
    ),
  );
  const attention = useSource<InventoryLotView>(
    useMemo(
      () => (signal: AbortSignal) => inventory.listAttention(signal),
      [inventory],
    ),
  );
  const saved = useSource<RecipeSummary>(
    useMemo(
      () => (signal: AbortSignal) => recipes.listRecipes(signal),
      [recipes],
    ),
  );

  const greeting = buildHomeGreeting({
    displayName:
      session.status === "authenticated" ? session.displayName : null,
    profileTimeZone:
      session.status === "authenticated" ? session.timeZone : null,
    browserTimeZone: readBrowserTimeZone(),
  });
  const greetingText = greeting.displayName
    ? t(`home.greeting.named.${greeting.dayPart}`, {
        name: greeting.displayName,
      })
    : t(`home.greeting.anonymous.${greeting.dayPart}`);

  return (
    <div data-testid="today-page" className="space-y-8">
      <header>
        <p data-testid="home-greeting" className="text-muted-foreground">
          {greetingText}
        </p>
        <h1 className="font-display text-3xl">{t("home.primaryQuestion")}</h1>
      </header>

      {cooking.status === "ready" && cooking.items.length > 0 ? (
        <section
          aria-labelledby="today-cooking"
          data-testid="today-cooking"
          className="rounded-lg border border-primary/40 bg-primary/5 p-4"
        >
          <h2 id="today-cooking" className="font-display text-xl">
            {t("today.cooking.title")}
          </h2>
          <ul className="mt-2 space-y-2">
            {cooking.items.map((item) => (
              <li
                key={item.executionId}
                className="flex flex-wrap items-center justify-between gap-2"
              >
                <span className="font-medium">{item.recipeName}</span>
                <Button asChild size="sm">
                  <Link to={`/app/receitas/cozinhar/${item.executionId}`}>
                    {t("today.cooking.resume")}
                  </Link>
                </Button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <section
        aria-labelledby="today-cta"
        className="rounded-lg border bg-card p-5"
      >
        <h2 id="today-cta" className="font-display text-2xl">
          {t("today.cta.title")}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("today.cta.detail")}
        </p>
        <div className="mt-4 flex flex-wrap gap-2">
          <Button asChild>
            <Link to="/app/receitas/gerar" data-testid="today-generate">
              {t("today.cta.generate")}
            </Link>
          </Button>
          <Button asChild variant="secondary">
            <Link to="/app/despensa/novo" data-testid="today-add-item">
              {t("today.cta.addItem")}
            </Link>
          </Button>
        </div>
      </section>

      <section aria-labelledby="today-attention" data-testid="today-attention">
        <div className="flex items-baseline justify-between gap-2">
          <h2 id="today-attention" className="font-display text-xl">
            {t("today.attention.title")}
          </h2>
          <Link
            to="/app/despensa"
            className="text-sm underline underline-offset-4"
          >
            {t("today.attention.all")}
          </Link>
        </div>
        {attention.status === "loading" ? (
          <p role="status" className="mt-2 text-sm text-muted-foreground">
            {t("app.loading")}
          </p>
        ) : attention.status === "failed" ? (
          <p className="mt-2 text-sm text-muted-foreground">
            {t("today.attention.unavailable")}
          </p>
        ) : attention.items.length === 0 ? (
          <p className="mt-2 text-sm text-muted-foreground">
            {t("today.attention.empty")}
          </p>
        ) : (
          <ul className="mt-2 divide-y rounded-md border">
            {attention.items.slice(0, 5).map((lot) => (
              <li
                key={lot.lotId}
                className="flex flex-wrap items-center justify-between gap-2 p-3"
              >
                <Link
                  to={`/app/despensa/${lot.lotId}`}
                  className="font-medium underline-offset-4 hover:underline"
                >
                  {lot.productName}
                </Link>
                <AttentionBadge attention={lot.attention} />
              </li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="today-recipes" data-testid="today-recipes">
        <div className="flex items-baseline justify-between gap-2">
          <h2 id="today-recipes" className="font-display text-xl">
            {t("today.recipes.title")}
          </h2>
          <Link
            to="/app/receitas"
            className="text-sm underline underline-offset-4"
          >
            {t("today.recipes.all")}
          </Link>
        </div>
        {saved.status === "loading" ? (
          <p role="status" className="mt-2 text-sm text-muted-foreground">
            {t("app.loading")}
          </p>
        ) : saved.status === "failed" ? (
          <p className="mt-2 text-sm text-muted-foreground">
            {t("today.recipes.unavailable")}
          </p>
        ) : saved.items.length === 0 ? (
          <p className="mt-2 text-sm text-muted-foreground">
            {t("today.recipes.empty")}
          </p>
        ) : (
          <ul className="mt-2 space-y-1">
            {saved.items.slice(0, 3).map((recipe) => (
              <li key={recipe.recipeId}>
                <Link
                  to={`/app/receitas/${recipe.recipeId}`}
                  className="underline-offset-4 hover:underline"
                >
                  {recipe.name}
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
