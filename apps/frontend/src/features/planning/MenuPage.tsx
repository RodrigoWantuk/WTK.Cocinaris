import { Plus, Sparkles, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import type { Menu, MenuEntry, MenuEntryInput } from "@/contracts/planning";
import { RecipeApiError, type RecipeSummary } from "@/contracts/recipes";
import { useRecipeRepository } from "@/features/recipes/RecipeProvider";
import { MEAL_TYPES, mealTypeLabel } from "@/features/recipes/recipeLabels";
import {
  daysBetween,
  formatMenuDay,
  formatMenuPeriod,
  localIsoDate,
} from "./planningDates";
import { usePlanningRepository } from "./PlanningProvider";
import { AiMealSuggestion } from "./AiMealSuggestion";
import { formatLocaleDecimal } from "@/lib/localeDecimal";

const FLEXIBLE = "flexible";

/**
 * One menu (PLAN-0029): its days with their meals, adding recipes from the library, moving,
 * re-portioning, skipping, removing, and cooking a meal. Cooking from here links the session to
 * the meal, which then shows as cooking and, once completed, as cooked. Every change touches one
 * meal only; an archived menu is read-only.
 */
export function MenuPage() {
  const { menuId = "" } = useParams<{ menuId: string }>();
  const repo = usePlanningRepository();
  const recipes = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const [menu, setMenu] = useState<Menu | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [addingTo, setAddingTo] = useState<string | null>(null);
  const [suggestingFor, setSuggestingFor] = useState<string | null>(null);
  const [editing, setEditing] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo.getMenu(menuId, controller.signal).then(
      (value) => !controller.signal.aborted && setMenu(value),
      () => !controller.signal.aborted && setLoadError(true),
    );
    return () => controller.abort();
  }, [repo, menuId]);

  const mutate = useCallback(
    async (change: (csrfToken: string) => Promise<Menu>) => {
      if (!session.csrfToken) return false;
      setBusy(true);
      setActionError(null);
      try {
        setMenu(await change(session.csrfToken));
        return true;
      } catch {
        setActionError("planning.error.save");
        return false;
      } finally {
        setBusy(false);
      }
    },
    [session.csrfToken],
  );

  const cook = async (entry: MenuEntry) => {
    if (entry.status === "cooking" && entry.executionId) {
      navigate(`/app/receitas/cozinhar/${entry.executionId}`);
      return;
    }
    if (!session.csrfToken) return;
    setBusy(true);
    setActionError(null);
    try {
      const execution = await recipes.startExecution(entry.recipeId, {
        csrfToken: session.csrfToken,
        servings: entry.servings,
        menuEntryId: entry.entryId,
      });
      navigate(`/app/receitas/cozinhar/${execution.executionId}`);
    } catch {
      setActionError("planning.error.cook");
      setBusy(false);
    }
  };

  if (loadError) {
    return (
      <div data-testid="menu-error" className="space-y-4">
        <FeatureUnavailable
          feature="planning-menu"
          title={t("feature.unavailable")}
          detail={t("planning.error.load")}
        />
        <Button asChild variant="secondary">
          <Link to="/app/cardapio">{t("planning.back")}</Link>
        </Button>
      </div>
    );
  }

  if (!menu) {
    return (
      <div role="status" data-testid="menu-loading">
        <p>{t("planning.loading")}</p>
      </div>
    );
  }

  const editable = menu.status !== "archived";
  const today = localIsoDate();
  const groups = [
    ...daysBetween(menu.startDate, menu.endDate).map((day) => ({
      key: day,
      title:
        formatMenuDay(day, locale) +
        (day === today ? ` · ${t("today.menu.today")}` : ""),
      entries: menu.entries.filter((entry) => entry.plannedDate === day),
    })),
    {
      key: FLEXIBLE,
      title: t("planning.flexible"),
      entries: menu.entries.filter((entry) => entry.plannedDate === null),
    },
  ];

  return (
    <div data-testid="menu-page" className="space-y-6">
      <div className="space-y-2">
        <Link to="/app/cardapio" className="text-sm underline">
          {t("planning.back")}
        </Link>
        <h1 className="font-display text-3xl">
          {menu.name || formatMenuPeriod(menu.startDate, menu.endDate, locale)}
        </h1>
        <p className="text-muted-foreground">
          {formatMenuPeriod(menu.startDate, menu.endDate, locale)} ·{" "}
          <span data-testid="menu-status">
            {t(`planning.status.${menu.status}`)}
          </span>
        </p>
        <p className="text-sm text-muted-foreground">
          {t(`planning.statusHint.${menu.status}`)}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button asChild data-testid="menu-shopping-link">
          <Link to={`/app/cardapio/${menu.menuId}/compras`}>
            {t("planning.shoppingLink")}
          </Link>
        </Button>
        {menu.status === "draft" ? (
          <Button
            type="button"
            variant="secondary"
            data-testid="menu-accept"
            disabled={busy || menu.entries.length === 0}
            onClick={() =>
              void mutate((csrfToken) =>
                repo.acceptMenu(menu.menuId, { csrfToken }),
              )
            }
          >
            {t("planning.accept")}
          </Button>
        ) : null}
        {editable ? (
          <Button
            type="button"
            variant="ghost"
            data-testid="menu-archive"
            disabled={busy}
            onClick={() => {
              if (window.confirm(t("planning.archiveConfirm")))
                void mutate((csrfToken) =>
                  repo.archiveMenu(menu.menuId, { csrfToken }),
                );
            }}
          >
            {t("planning.archive")}
          </Button>
        ) : null}
      </div>
      {actionError ? (
        <p role="alert" className="text-sm text-destructive">
          {t(actionError)}
        </p>
      ) : null}

      {/* One surface with a row per day instead of a card per day (review M2). */}
      <ol className="surface divide-y divide-border overflow-hidden">
        {groups.map((group) => (
          <li
            key={group.key}
            data-testid={`menu-day-${group.key}`}
            className="space-y-2 p-4"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 className="font-display text-lg">{group.title}</h2>
              {editable ? (
                <div className="flex gap-1">
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    aria-expanded={addingTo === group.key}
                    data-testid={`menu-add-${group.key}`}
                    onClick={() => {
                      setSuggestingFor(null);
                      setAddingTo((current) =>
                        current === group.key ? null : group.key,
                      );
                    }}
                  >
                    <Plus aria-hidden="true" />
                    <span className="sr-only sm:not-sr-only">
                      {t("planning.addMeal")}
                    </span>
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    aria-expanded={suggestingFor === group.key}
                    data-testid={`menu-ai-${group.key}`}
                    onClick={() => {
                      setAddingTo(null);
                      setSuggestingFor((current) =>
                        current === group.key ? null : group.key,
                      );
                    }}
                  >
                    <Sparkles aria-hidden="true" />
                    <span className="sr-only sm:not-sr-only">
                      {t("planning.ai.open")}
                    </span>
                  </Button>
                </div>
              ) : null}
            </div>
            {group.entries.length === 0 &&
            addingTo !== group.key &&
            suggestingFor !== group.key ? (
              <p className="text-sm text-muted-foreground">
                {t(
                  group.key === FLEXIBLE
                    ? "planning.flexibleHint"
                    : "planning.emptyDay",
                )}
              </p>
            ) : null}
            <ul className="space-y-2">
              {group.entries.map((entry) => (
                <EntryRow
                  key={entry.entryId}
                  entry={entry}
                  menu={menu}
                  editable={editable}
                  busy={busy}
                  editing={editing === entry.entryId}
                  onEdit={() =>
                    setEditing((current) =>
                      current === entry.entryId ? null : entry.entryId,
                    )
                  }
                  onCook={() => void cook(entry)}
                  onChange={async (input) => {
                    if (
                      await mutate((csrfToken) =>
                        repo.changeEntry(menu.menuId, entry.entryId, input, {
                          csrfToken,
                        }),
                      )
                    )
                      setEditing(null);
                  }}
                  onRemove={() =>
                    void mutate((csrfToken) =>
                      repo.removeEntry(menu.menuId, entry.entryId, {
                        csrfToken,
                      }),
                    )
                  }
                />
              ))}
            </ul>
            {suggestingFor === group.key ? (
              <AiMealSuggestion
                menuId={menu.menuId}
                plannedDate={group.key === FLEXIBLE ? null : group.key}
                initialServings={2}
                onCancel={() => setSuggestingFor(null)}
                onChosen={async (input) => {
                  const added = await mutate((csrfToken) =>
                    repo.addEntry(
                      menu.menuId,
                      {
                        recipeId: input.recipeId,
                        plannedDate: group.key === FLEXIBLE ? null : group.key,
                        mealType: input.mealType,
                        servings: input.servings,
                      },
                      { csrfToken, idempotencyKey: crypto.randomUUID() },
                    ),
                  );
                  if (added) setSuggestingFor(null);
                  return added;
                }}
              />
            ) : null}
            {addingTo === group.key ? (
              <AddMeal
                plannedDate={group.key === FLEXIBLE ? null : group.key}
                busy={busy}
                onCancel={() => setAddingTo(null)}
                onAdd={async (input, idempotencyKey) => {
                  if (
                    await mutate((csrfToken) =>
                      repo.addEntry(menu.menuId, input, {
                        csrfToken,
                        idempotencyKey,
                      }),
                    )
                  )
                    setAddingTo(null);
                }}
              />
            ) : null}
          </li>
        ))}
      </ol>
    </div>
  );
}

function EntryRow({
  entry,
  menu,
  editable,
  busy,
  editing,
  onEdit,
  onCook,
  onChange,
  onRemove,
}: {
  entry: MenuEntry;
  menu: Menu;
  editable: boolean;
  busy: boolean;
  editing: boolean;
  onEdit: () => void;
  onCook: () => void;
  onChange: (input: MenuEntryInput) => Promise<void>;
  onRemove: () => void;
}) {
  const { t, locale } = useProductionI18n();
  const [day, setDay] = useState(entry.plannedDate ?? FLEXIBLE);
  const [meal, setMeal] = useState(entry.mealType ?? "");
  const [servings, setServings] = useState(String(entry.servings));
  const [reserve, setReserve] = useState(entry.reserveIngredients);
  const done = entry.status === "cooked";
  const input = (status: string) => ({
    plannedDate: entry.plannedDate,
    mealType: entry.mealType,
    servings: entry.servings,
    status,
  });

  return (
    <li
      data-testid="menu-entry"
      className="space-y-2 rounded-xl border border-border bg-background p-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <Link
            to={`/app/receitas/${entry.recipeId}`}
            className={`font-medium underline-offset-2 hover:underline ${entry.status === "skipped" ? "line-through" : ""}`}
          >
            {entry.recipeName}
          </Link>
          <p className="text-xs text-muted-foreground">
            {[
              entry.mealType ? mealTypeLabel(t, entry.mealType) : null,
              t("recipes.detail.servings", { count: entry.servings }),
              t(`planning.entryStatus.${entry.status}`),
            ]
              .filter(Boolean)
              .join(" · ")}
          </p>
          <Readiness entry={entry} />
        </div>
        {editable ? (
          <div className="flex flex-wrap gap-1">
            {entry.status === "planned" || entry.status === "cooking" ? (
              <Button
                type="button"
                size="sm"
                data-testid="menu-entry-cook"
                disabled={busy}
                onClick={onCook}
              >
                {t(
                  entry.status === "cooking"
                    ? "today.cooking.resume"
                    : "recipes.detail.cook",
                )}
              </Button>
            ) : null}
            {!done && entry.status !== "cooking" ? (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                data-testid="menu-entry-skip"
                disabled={busy}
                onClick={() =>
                  void onChange(
                    input(entry.status === "skipped" ? "planned" : "skipped"),
                  )
                }
              >
                {t(
                  entry.status === "skipped"
                    ? "planning.unskip"
                    : "planning.skip",
                )}
              </Button>
            ) : null}
            {!done ? (
              <Button
                type="button"
                size="sm"
                variant="ghost"
                data-testid="menu-entry-edit"
                aria-expanded={editing}
                onClick={onEdit}
              >
                {t("planning.edit")}
              </Button>
            ) : null}
            <Button
              type="button"
              size="sm"
              variant="ghost"
              data-testid="menu-entry-remove"
              aria-label={t("planning.remove", { name: entry.recipeName })}
              disabled={busy}
              title={t("planning.remove", { name: entry.recipeName })}
              onClick={onRemove}
            >
              <Trash2 aria-hidden="true" />
            </Button>
          </div>
        ) : null}
      </div>
      {editing ? (
        <form
          className="grid gap-2 sm:grid-cols-4 sm:items-end"
          onSubmit={(event) => {
            event.preventDefault();
            const portions = Number(servings);
            if (!Number.isInteger(portions) || portions < 1 || portions > 24)
              return;
            void onChange({
              plannedDate: day === FLEXIBLE ? null : day,
              mealType: meal || null,
              servings: portions,
              status: entry.status === "skipped" ? "skipped" : "planned",
              reserveIngredients: reserve,
            });
          }}
        >
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("planning.field.day")}
            <select
              className="h-11 w-full rounded-xl border border-input bg-background px-3 text-[15px] text-foreground"
              value={day}
              onChange={(event) => setDay(event.target.value)}
            >
              {daysBetween(menu.startDate, menu.endDate).map((value) => (
                <option key={value} value={value}>
                  {formatMenuDay(value, locale)}
                </option>
              ))}
              <option value={FLEXIBLE}>{t("planning.flexible")}</option>
            </select>
          </label>
          <MealSelect value={meal} onChange={setMeal} />
          <label className="space-y-1 text-xs text-muted-foreground">
            {t("editor.servings")}
            <Input
              type="number"
              min={1}
              max={24}
              value={servings}
              onChange={(event) => setServings(event.target.value)}
            />
          </label>
          <Button type="submit" size="sm" disabled={busy}>
            {t("planning.save")}
          </Button>
          <label className="flex items-center gap-2 text-xs text-muted-foreground sm:col-span-4">
            <input
              type="checkbox"
              className="h-4 w-4"
              data-testid="menu-entry-reserve"
              checked={reserve}
              onChange={(event) => setReserve(event.target.checked)}
            />
            {t("planning.field.reserve")}
          </label>
        </form>
      ) : null}
    </li>
  );
}

/**
 * Whether a meal's pantry ingredients are covered (PLAN-0029 Phase 3). On a menu in use the
 * quantities are reserved; on a draft this simulates accepting it after the menus in use.
 */
function Readiness({ entry }: { entry: MenuEntry }) {
  const { t, locale } = useProductionI18n();
  if (entry.status === "cooked" || entry.status === "skipped") return null;
  if (!entry.reserveIngredients) {
    return (
      <p
        className="text-xs text-muted-foreground"
        data-testid="menu-entry-readiness"
      >
        {t("planning.readiness.released")}
      </p>
    );
  }
  if (!entry.readiness) return null;
  if (entry.readiness.readiness === "ready") {
    return (
      <p className="text-xs text-primary" data-testid="menu-entry-readiness">
        {t("planning.readiness.ready")}
      </p>
    );
  }
  const unitKey: Record<string, string> = {
    Gram: "recipes.unit.g",
    Milliliter: "recipes.unit.ml",
    Unit: "recipes.unit.unit",
  };
  const items = entry.readiness.missing
    .map(
      (item) =>
        `${item.name} ${formatLocaleDecimal(item.quantity, locale)} ${t(unitKey[item.unit] ?? "recipes.unit.unit")}`,
    )
    .join(", ");
  return (
    <p
      className="mt-1 inline-block rounded-full bg-warning-soft px-2.5 py-0.5 text-xs font-semibold text-warning"
      data-testid="menu-entry-readiness"
    >
      {t("planning.readiness.missing", { items })}
    </p>
  );
}

function MealSelect({
  value,
  onChange,
}: {
  value: string;
  onChange: (value: string) => void;
}) {
  const { t } = useProductionI18n();
  return (
    <label className="space-y-1 text-xs text-muted-foreground">
      {t("planning.field.meal")}
      <select
        data-testid="menu-meal-select"
        className="h-11 w-full rounded-xl border border-input bg-background px-3 text-[15px] text-foreground"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">{t("planning.field.anyMeal")}</option>
        {MEAL_TYPES.map((mealType) => (
          <option key={mealType} value={mealType}>
            {mealTypeLabel(t, mealType)}
          </option>
        ))}
      </select>
    </label>
  );
}

function AddMeal({
  plannedDate,
  busy,
  onCancel,
  onAdd,
}: {
  plannedDate: string | null;
  busy: boolean;
  onCancel: () => void;
  onAdd: (
    input: {
      recipeId: string;
      plannedDate: string | null;
      mealType: string | null;
      servings: number | null;
    },
    idempotencyKey: string,
  ) => Promise<void>;
}) {
  const recipes = useRecipeRepository();
  const { t } = useProductionI18n();
  const [library, setLibrary] = useState<RecipeSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [search, setSearch] = useState("");
  const [recipeId, setRecipeId] = useState("");
  const [meal, setMeal] = useState("");
  const [servings, setServings] = useState("");
  // One key per intended addition, so a retried request never adds the meal twice.
  const keyRef = useRef(crypto.randomUUID());

  useEffect(() => {
    const controller = new AbortController();
    recipes.listRecipes(controller.signal).then(
      (items) => !controller.signal.aborted && setLibrary(items),
      (err) =>
        !controller.signal.aborted &&
        !(err instanceof RecipeApiError && err.code === "cancelled") &&
        setFailed(true),
    );
    return () => controller.abort();
  }, [recipes]);

  const visible = useMemo(() => {
    const term = search.trim().toLocaleLowerCase();
    return (library ?? []).filter(
      (item) => !term || item.name.toLocaleLowerCase().includes(term),
    );
  }, [library, search]);
  const selected = library?.find((item) => item.recipeId === recipeId);

  if (failed) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        {t("recipes.list.error")}
      </p>
    );
  }
  if (!library) return <p role="status">{t("planning.loading")}</p>;
  if (library.length === 0) {
    return (
      <p className="text-sm" data-testid="menu-add-empty">
        {t("planning.add.noRecipes")}{" "}
        <Link to="/app/receitas/gerar" className="underline">
          {t("recipes.actions.generate")}
        </Link>{" "}
        ·{" "}
        <Link to="/app/receitas/nova" className="underline">
          {t("library.new")}
        </Link>
      </p>
    );
  }

  return (
    <form
      data-testid="menu-add-form"
      className="space-y-2 rounded-xl border border-dashed border-border p-3"
      onSubmit={(event) => {
        event.preventDefault();
        if (!recipeId) return;
        const portions = servings ? Number(servings) : null;
        if (
          portions !== null &&
          (!Number.isInteger(portions) || portions < 1 || portions > 24)
        )
          return;
        void onAdd(
          { recipeId, plannedDate, mealType: meal || null, servings: portions },
          keyRef.current,
        );
      }}
    >
      <label htmlFor="menu-add-search" className="sr-only">
        {t("library.search")}
      </label>
      <Input
        id="menu-add-search"
        type="search"
        value={search}
        placeholder={t("library.search.placeholder")}
        onChange={(event) => setSearch(event.target.value)}
      />
      <fieldset>
        <legend className="sr-only">{t("planning.add.recipe")}</legend>
        <ul className="max-h-56 space-y-1 overflow-y-auto">
          {visible.map((item) => (
            <li key={item.recipeId}>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="radio"
                  name="menu-add-recipe"
                  data-testid="menu-add-recipe"
                  value={item.recipeId}
                  checked={recipeId === item.recipeId}
                  onChange={() => {
                    setRecipeId(item.recipeId);
                    setServings(String(item.servings));
                  }}
                />
                {item.isFavorite ? "★ " : ""}
                {item.name}
              </label>
            </li>
          ))}
        </ul>
      </fieldset>
      <div className="grid gap-2 sm:grid-cols-3 sm:items-end">
        <MealSelect value={meal} onChange={setMeal} />
        <label className="space-y-1 text-xs text-muted-foreground">
          {t("editor.servings")}
          <Input
            type="number"
            min={1}
            max={24}
            data-testid="menu-add-servings"
            value={servings}
            onChange={(event) => setServings(event.target.value)}
          />
        </label>
        <div className="flex gap-2">
          <Button
            type="submit"
            size="sm"
            data-testid="menu-add-submit"
            disabled={busy || !selected}
          >
            {t("planning.add.submit")}
          </Button>
          <Button type="button" size="sm" variant="ghost" onClick={onCancel}>
            {t("editor.cancel")}
          </Button>
        </div>
      </div>
    </form>
  );
}
