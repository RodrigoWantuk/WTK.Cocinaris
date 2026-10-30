import { useEffect, useRef, useState } from "react";
import {
  Link,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router-dom";
import { useSession } from "@/app/session/SessionProvider";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { FeatureUnavailable } from "@/components/runtime/FeatureUnavailable";
import {
  RecipeApiError,
  type RecipeContentInput,
  type RecipeDetail,
  type RecipeExecution,
} from "@/contracts/recipes";
import {
  formatLocaleDecimal,
  parseLocaleDecimal,
  type SupportedDecimalLocale,
} from "@/lib/localeDecimal";
import { parseNormalizedRecipeJson } from "./parseNormalizedRecipe";
import { useHouseholdMeasure } from "./RecipeContent";
import { toCanonicalAmount, type KitchenUnit } from "@/lib/kitchenMeasures";
import { useRecipeRepository } from "./RecipeProvider";
import { recipeLibraryErrorKey } from "./recipeErrors";
import { MEAL_TYPES, mealTypeLabel } from "./recipeLabels";

/** `new` writes a recipe, `edit` saves the next revision, `variation` creates a derived recipe. */
export type RecipeEditorMode = "new" | "edit" | "variation";

interface IngredientRow {
  key: string;
  ingredientId: string | null;
  displayName: string;
  quantity: string;
  unit: string;
  optional: boolean;
  fromPantry: boolean;
}

interface StageRow {
  key: string;
  stageId: string | null;
  name: string;
  instructions: string;
  active: string;
  passive: string;
  cues: string;
}

interface Draft {
  name: string;
  servings: string;
  mealTypes: string[];
  yield: string;
  ingredients: IngredientRow[];
  equipment: string;
  stages: StageRow[];
  reason: string;
}

let rowCounter = 0;
const rowKey = () => `row-${++rowCounter}`;

const emptyIngredient = (): IngredientRow => ({
  key: rowKey(),
  ingredientId: null,
  displayName: "",
  quantity: "",
  unit: "g",
  optional: false,
  fromPantry: false,
});

const emptyStage = (): StageRow => ({
  key: rowKey(),
  stageId: null,
  name: "",
  instructions: "",
  active: "0",
  passive: "0",
  cues: "",
});

const UNITS = ["g", "ml", "unit"] as const;
/** Kitchen measures accepted while editing; saved as grams or milliliters (PLAN-0054 Phase 3). */
const KITCHEN_UNITS = ["cup", "tbsp", "tsp"] as const;
const isKitchenUnit = (unit: string): unit is KitchenUnit =>
  (KITCHEN_UNITS as readonly string[]).includes(unit);

function draftFrom(
  detail: RecipeDetail,
  locale: SupportedDecimalLocale,
): Draft {
  const parsed = parseNormalizedRecipeJson(detail.normalizedRecipeJson);
  return {
    name: detail.name,
    servings: String(detail.servings),
    mealTypes: detail.mealTypes.filter((item) =>
      (MEAL_TYPES as readonly string[]).includes(item),
    ),
    yield: parsed?.yield ?? "",
    ingredients: (parsed?.ingredients ?? []).map((item) => ({
      key: rowKey(),
      ingredientId: item.ingredientId ?? null,
      displayName: item.displayName,
      quantity:
        item.quantity === undefined
          ? ""
          : formatLocaleDecimal(item.quantity, locale),
      unit: UNITS.includes(item.unit as (typeof UNITS)[number])
        ? (item.unit as string)
        : "unit",
      optional: item.optional,
      fromPantry: item.sourceType === "inventory",
    })),
    equipment: (parsed?.equipment ?? []).join("\n"),
    stages: (parsed?.stages ?? []).map((stage) => ({
      key: rowKey(),
      stageId: stage.stageId ?? null,
      name: stage.title,
      instructions: stage.instructions.join("\n"),
      active: String(stage.activeMinutes ?? 0),
      passive: String(stage.passiveMinutes ?? 0),
      cues: stage.sensoryCues.join("\n"),
    })),
    reason: "",
  };
}

const lines = (value: string) =>
  value
    .split(/\n+/)
    .map((line) => line.trim())
    .filter(Boolean);

const minutes = (value: string) => {
  const n = Number(value);
  return Number.isInteger(n) && n >= 0 && n <= 1440 ? n : null;
};

/**
 * Validates the draft with the same limits the backend enforces and builds the request content.
 * Error keys use the backend field names (`ingredients[0].quantity`) so server errors land on the
 * same fields.
 */
function buildContent(
  draft: Draft,
  locale: SupportedDecimalLocale,
  mode: RecipeEditorMode,
): { content: RecipeContentInput | null; errors: Record<string, string> } {
  const errors: Record<string, string> = {};
  if (!draft.name.trim()) errors.name = "editor.field.required";
  const servings = Number(draft.servings);
  if (!Number.isInteger(servings) || servings < 1 || servings > 24)
    errors.servings = "editor.field.servings";
  if (draft.mealTypes.length === 0) errors.mealTypes = "editor.field.mealTypes";
  if (draft.ingredients.length === 0)
    errors.ingredients = "editor.field.required";
  if (draft.stages.length === 0) errors.stages = "editor.field.required";
  if (mode === "variation" && !draft.reason.trim())
    errors.derivationReason = "editor.field.required";

  const ingredients = draft.ingredients.map((row, index) => {
    if (!row.displayName.trim())
      errors[`ingredients[${index}].displayName`] = "editor.field.required";
    const parsed = parseLocaleDecimal(row.quantity, locale);
    const entered = parsed.ok ? parsed.value : NaN;
    if (!(entered > 0))
      errors[`ingredients[${index}].quantity`] = "editor.field.quantity";
    const canonical =
      isKitchenUnit(row.unit) && entered > 0
        ? toCanonicalAmount(row.displayName, entered, row.unit)
        : { quantity: entered, unit: row.unit };
    return {
      ingredientId: row.ingredientId,
      displayName: row.displayName.trim(),
      quantity: canonical.quantity,
      unit: canonical.unit,
      optional: row.optional,
    };
  });

  const stages = draft.stages.map((row, index) => {
    if (!row.name.trim())
      errors[`stages[${index}].name`] = "editor.field.required";
    if (!row.instructions.trim())
      errors[`stages[${index}].instructions`] = "editor.field.required";
    const active = minutes(row.active);
    const passive = minutes(row.passive);
    if (active === null || passive === null)
      errors[`stages[${index}].minutes`] = "editor.field.minutes";
    return {
      stageId: row.stageId,
      name: row.name.trim(),
      instructions: row.instructions.trim(),
      activeMinutes: active ?? 0,
      passiveMinutes: passive ?? 0,
      sensoryCues: lines(row.cues),
    };
  });

  if (Object.keys(errors).length > 0) return { content: null, errors };
  return {
    content: {
      name: draft.name.trim(),
      mealTypes: draft.mealTypes,
      servings,
      yield: draft.yield.trim() || null,
      ingredients,
      equipment: lines(draft.equipment),
      stages,
    },
    errors,
  };
}

/** Lot unit of an adjustment and the editor unit it corresponds to. */
const LOT_UNIT_TO_EDITOR: Record<string, string> = {
  g: "g",
  ml: "ml",
  unit: "unit",
};

/**
 * Carries the changes a cooking session recorded (PLAN-0042 Phase 2) into an editor draft:
 * a substitution renames the ingredient and drops its pantry link, an amount used is converted
 * back to the recipe's portions, and an ingredient left out becomes optional (not removed, so
 * nothing is lost). Amounts apply only to rows in grams, milliliters, or units, which are the
 * units an adjustment is recorded in. Returns the new draft and the names of the rows changed.
 */
export function applyCookingAdjustments(
  draft: Draft,
  execution: RecipeExecution,
  locale: SupportedDecimalLocale,
): { draft: Draft; changed: string[] } {
  const byId = new Map(
    execution.adjustments.map((item) => [item.ingredientId, item]),
  );
  const ratio = execution.recipeServings / Math.max(1, execution.servings);
  const changed: string[] = [];
  const scaled = (value: number) =>
    formatLocaleDecimal(Math.round(value * ratio * 1000) / 1000, locale);
  const ingredients = draft.ingredients.map((row) => {
    const adjustment = row.ingredientId
      ? byId.get(row.ingredientId)
      : undefined;
    if (!adjustment) return row;
    changed.push(row.displayName);
    const amount =
      adjustment.quantity !== null && LOT_UNIT_TO_EDITOR[row.unit]
        ? { quantity: scaled(adjustment.quantity) }
        : {};
    if (adjustment.kind === "skip") return { ...row, optional: true };
    if (adjustment.kind === "quantity") return { ...row, ...amount };
    return {
      ...row,
      ...amount,
      displayName: adjustment.substituteName ?? row.displayName,
      fromPantry: false,
    };
  });
  return { draft: { ...draft, ingredients }, changed };
}

/**
 * Writes a new recipe, saves an edit as the next revision, or creates a variation (derived
 * recipe). With `?execucao=<id>` the page was opened after cooking: the cooking notes are shown as
 * reference and the saved revision records the session it came from.
 */
export function RecipeEditorPage({ mode }: { mode: RecipeEditorMode }) {
  const { recipeId = "" } = useParams<{ recipeId: string }>();
  const [searchParams] = useSearchParams();
  const executionId = searchParams.get("execucao");
  const repo = useRecipeRepository();
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const navigate = useNavigate();
  const [base, setBase] = useState<RecipeDetail | null>(null);
  const [execution, setExecution] = useState<RecipeExecution | null>(null);
  const [draft, setDraft] = useState<Draft | null>(
    mode === "new"
      ? {
          name: "",
          servings: "2",
          mealTypes: [],
          yield: "",
          ingredients: [emptyIngredient()],
          equipment: "",
          stages: [emptyStage()],
          reason: "",
        }
      : null,
  );
  const [loadError, setLoadError] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [saving, setSaving] = useState(false);
  // One key per intended creation; a retry after a network failure reuses it.
  const keyRef = useRef<string>(crypto.randomUUID());
  // The session's ingredient changes are applied once, after both the recipe and the session load.
  const [applied, setApplied] = useState<string[] | null>(null);

  useEffect(() => {
    if (
      !base ||
      !draft ||
      !execution ||
      applied !== null ||
      execution.adjustments.length === 0
    )
      return;
    const result = applyCookingAdjustments(
      draft,
      execution,
      locale as SupportedDecimalLocale,
    );
    setDraft(result.draft);
    setApplied(result.changed);
  }, [base, draft, execution, applied, locale]);

  useEffect(() => {
    if (mode === "new") return;
    const controller = new AbortController();
    void (async () => {
      try {
        const recipe = await repo.getRecipe(recipeId, controller.signal);
        setBase(recipe);
        setDraft(draftFrom(recipe, locale));
      } catch (err) {
        if (controller.signal.aborted) return;
        setLoadError(
          err instanceof RecipeApiError
            ? recipeLibraryErrorKey(err.code, "recipes.detail.error")
            : "recipes.detail.error",
        );
      }
    })();
    return () => controller.abort();
    // The locale only formats the initial quantities; switching it must not reset the draft.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, recipeId, repo]);

  useEffect(() => {
    if (!executionId) return;
    const controller = new AbortController();
    // The cooking notes are reference only; the editor works without them.
    repo
      .getExecution(executionId, controller.signal)
      .then((value) => {
        if (value.recipeId === recipeId && value.status === "Completed")
          setExecution(value);
      })
      .catch(() => undefined);
    return () => controller.abort();
  }, [executionId, recipeId, repo]);

  if (loadError) {
    return (
      <div data-testid="recipe-editor-error" className="space-y-4">
        <FeatureUnavailable
          feature="recipes-editor"
          title={t("feature.unavailable")}
          detail={t(loadError)}
        />
        <Button asChild variant="secondary">
          <Link to="/app/receitas">{t("recipes.actions.back")}</Link>
        </Button>
      </div>
    );
  }

  if (!draft) {
    return (
      <div data-testid="recipe-editor-loading" role="status">
        <p>{t("recipes.detail.loading")}</p>
      </div>
    );
  }

  const update = (change: Partial<Draft>) =>
    setDraft((current) => (current ? { ...current, ...change } : current));
  const updateIngredient = (index: number, change: Partial<IngredientRow>) =>
    update({
      ingredients: draft.ingredients.map((row, i) =>
        i === index ? { ...row, ...change } : row,
      ),
    });
  const updateStage = (index: number, change: Partial<StageRow>) =>
    update({
      stages: draft.stages.map((row, i) =>
        i === index ? { ...row, ...change } : row,
      ),
    });
  const moveStage = (index: number, delta: number) => {
    const target = index + delta;
    if (target < 0 || target >= draft.stages.length) return;
    const stages = [...draft.stages];
    [stages[index], stages[target]] = [stages[target], stages[index]];
    update({ stages });
  };

  const fieldError = (field: string) =>
    errors[field] ? (
      <p
        id={`${field}-error`}
        className="text-xs text-destructive"
        data-testid="recipe-editor-field-error"
      >
        {t(errors[field])}
      </p>
    ) : null;
  const described = (field: string) =>
    errors[field]
      ? { "aria-invalid": true, "aria-describedby": `${field}-error` }
      : {};

  const save = async () => {
    if (!session.csrfToken || saving) return;
    const built = buildContent(draft, locale, mode);
    setErrors(built.errors);
    setFormError(null);
    if (!built.content) {
      setFormError("editor.error.invalid");
      return;
    }
    setSaving(true);
    try {
      const options = { csrfToken: session.csrfToken };
      const sourceExecutionId = execution?.executionId ?? null;
      const saved =
        mode === "new"
          ? await repo.createRecipe(built.content, {
              ...options,
              idempotencyKey: keyRef.current,
            })
          : mode === "edit"
            ? await repo.reviseRecipe(
                recipeId,
                {
                  expectedRevisionNumber: base!.currentRevisionNumber,
                  content: built.content,
                  changeReason: draft.reason.trim() || null,
                  sourceExecutionId,
                },
                options,
              )
            : await repo.deriveRecipe(
                recipeId,
                {
                  parentRevisionNumber: base!.revisionNumber,
                  content: built.content,
                  derivationReason: draft.reason.trim(),
                  sourceExecutionId,
                },
                { ...options, idempotencyKey: keyRef.current },
              );
      navigate(`/app/receitas/${saved.recipeId}`);
    } catch (err) {
      if (err instanceof RecipeApiError && err.code === "precondition_failed") {
        setStale(true);
      } else if (
        err instanceof RecipeApiError &&
        err.code === "validation_failed" &&
        Object.keys(err.fieldErrors).length > 0
      ) {
        setErrors(
          Object.fromEntries(
            Object.keys(err.fieldErrors).map((field) => [
              field,
              "editor.field.invalid",
            ]),
          ),
        );
        setFormError("editor.error.invalid");
      } else {
        setFormError(
          err instanceof RecipeApiError
            ? recipeLibraryErrorKey(err.code, "recipes.error.unexpected")
            : "recipes.error.unexpected",
        );
      }
    } finally {
      setSaving(false);
    }
  };

  const title =
    mode === "new"
      ? t("editor.title.new")
      : mode === "edit"
        ? t("editor.title.edit", { name: base?.name ?? "" })
        : t("editor.title.variation", { name: base?.name ?? "" });
  const cancelTo =
    mode === "new" ? "/app/receitas" : `/app/receitas/${recipeId}`;

  return (
    <form
      data-testid="recipe-editor"
      className="space-y-8"
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        void save();
      }}
    >
      <div>
        <h1 className="font-display text-3xl">{title}</h1>
        <p className="mt-1 text-muted-foreground">{t(`editor.hint.${mode}`)}</p>
      </div>

      {applied && applied.length > 0 ? (
        <aside
          data-testid="recipe-editor-cooking-adjustments"
          className="rounded-2xl border border-border bg-secondary/40 p-4 text-sm"
        >
          <p className="font-medium">{t("editor.cookingAdjustments")}</p>
          <p className="mt-1">{applied.join(", ")}</p>
          <p className="mt-1 text-muted-foreground">
            {t("editor.cookingAdjustmentsHint")}
          </p>
        </aside>
      ) : null}

      {execution && (execution.notes || execution.rating) ? (
        <aside
          data-testid="recipe-editor-cooking-notes"
          className="rounded-2xl border border-border bg-secondary/40 p-4 text-sm"
        >
          <p className="font-medium">{t("editor.cookingNotes")}</p>
          {execution.rating ? (
            <p className="mt-1">
              {t("editor.cookingRating", { value: execution.rating })}
            </p>
          ) : null}
          {execution.notes ? (
            <p className="mt-1 whitespace-pre-line">{execution.notes}</p>
          ) : null}
        </aside>
      ) : null}

      {mode === "variation" ? (
        <div className="space-y-1">
          <label htmlFor="editor-reason" className="text-sm font-medium">
            {t("editor.derivationReason")}
          </label>
          <Input
            id="editor-reason"
            data-testid="recipe-editor-reason"
            value={draft.reason}
            maxLength={200}
            placeholder={t("editor.derivationReason.placeholder")}
            onChange={(event) => update({ reason: event.target.value })}
            {...described("derivationReason")}
          />
          {fieldError("derivationReason")}
        </div>
      ) : null}

      <section className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1 sm:col-span-2">
          <label htmlFor="editor-name" className="text-sm font-medium">
            {t("editor.name")}
          </label>
          <Input
            id="editor-name"
            data-testid="recipe-editor-name"
            value={draft.name}
            maxLength={80}
            onChange={(event) => update({ name: event.target.value })}
            {...described("name")}
          />
          {fieldError("name")}
        </div>
        <div className="space-y-1">
          <label htmlFor="editor-servings" className="text-sm font-medium">
            {t("editor.servings")}
          </label>
          <Input
            id="editor-servings"
            data-testid="recipe-editor-servings"
            type="number"
            inputMode="numeric"
            min={1}
            max={24}
            value={draft.servings}
            onChange={(event) => update({ servings: event.target.value })}
            {...described("servings")}
          />
          {fieldError("servings")}
        </div>
        <div className="space-y-1">
          <label htmlFor="editor-yield" className="text-sm font-medium">
            {t("editor.yield")}
          </label>
          <Input
            id="editor-yield"
            value={draft.yield}
            maxLength={80}
            onChange={(event) => update({ yield: event.target.value })}
          />
        </div>
        <fieldset className="space-y-2 sm:col-span-2">
          <legend className="text-sm font-medium">
            {t("editor.mealTypes")}
          </legend>
          <div className="flex flex-wrap gap-4">
            {MEAL_TYPES.map((mealType) => (
              <label key={mealType} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4"
                  data-testid={`recipe-editor-meal-${mealType}`}
                  checked={draft.mealTypes.includes(mealType)}
                  onChange={(event) =>
                    update({
                      mealTypes: event.target.checked
                        ? [...draft.mealTypes, mealType]
                        : draft.mealTypes.filter((item) => item !== mealType),
                    })
                  }
                />
                {mealTypeLabel(t, mealType)}
              </label>
            ))}
          </div>
          {fieldError("mealTypes")}
        </fieldset>
      </section>

      <section aria-labelledby="editor-ingredients" className="space-y-3">
        <h2 id="editor-ingredients" className="font-display text-xl">
          {t("editor.ingredients")}
        </h2>
        {fieldError("ingredients")}
        <ul className="space-y-3">
          {draft.ingredients.map((row, index) => {
            const label = row.displayName || String(index + 1);
            return (
              <li
                key={row.key}
                data-testid="recipe-editor-ingredient"
                className="grid grid-cols-2 gap-2 rounded-xl border border-border p-3 sm:grid-cols-[1fr_7rem_6rem_auto_auto] sm:items-end"
              >
                <div className="col-span-2 space-y-1 sm:col-span-1">
                  <label
                    htmlFor={`${row.key}-name`}
                    className="text-xs text-muted-foreground"
                  >
                    {t("editor.ingredient.name")}
                    {row.fromPantry
                      ? ` · ${t("editor.ingredient.pantry")}`
                      : ""}
                  </label>
                  <Input
                    id={`${row.key}-name`}
                    value={row.displayName}
                    maxLength={80}
                    onChange={(event) =>
                      updateIngredient(index, {
                        displayName: event.target.value,
                      })
                    }
                    {...described(`ingredients[${index}].displayName`)}
                  />
                  {fieldError(`ingredients[${index}].displayName`)}
                </div>
                <div className="space-y-1">
                  <label
                    htmlFor={`${row.key}-quantity`}
                    className="text-xs text-muted-foreground"
                  >
                    {t("editor.ingredient.quantity")}
                  </label>
                  <Input
                    id={`${row.key}-quantity`}
                    inputMode="decimal"
                    value={row.quantity}
                    onChange={(event) =>
                      updateIngredient(index, { quantity: event.target.value })
                    }
                    {...described(`ingredients[${index}].quantity`)}
                  />
                  {fieldError(`ingredients[${index}].quantity`)}
                </div>
                <div className="space-y-1">
                  <label
                    htmlFor={`${row.key}-unit`}
                    className="text-xs text-muted-foreground"
                  >
                    {t("editor.ingredient.unit")}
                  </label>
                  <select
                    id={`${row.key}-unit`}
                    className="h-11 w-full rounded-xl border border-input bg-background px-3 text-[15px]"
                    value={row.unit}
                    onChange={(event) =>
                      updateIngredient(index, { unit: event.target.value })
                    }
                  >
                    {UNITS.map((unit) => (
                      <option key={unit} value={unit}>
                        {t(`recipes.unit.${unit}`)}
                      </option>
                    ))}
                    {KITCHEN_UNITS.map((unit) => (
                      <option key={unit} value={unit}>
                        {t(`editor.unit.${unit}`)}
                      </option>
                    ))}
                  </select>
                </div>
                <MeasureHint
                  name={row.displayName}
                  quantity={row.quantity}
                  unit={row.unit}
                />
                <label className="flex items-center gap-2 text-sm sm:pb-2">
                  <input
                    type="checkbox"
                    className="h-4 w-4"
                    checked={row.optional}
                    onChange={(event) =>
                      updateIngredient(index, {
                        optional: event.target.checked,
                      })
                    }
                  />
                  {t("editor.ingredient.optional")}
                </label>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={t("editor.ingredient.remove", { name: label })}
                  onClick={() =>
                    update({
                      ingredients: draft.ingredients.filter(
                        (_, i) => i !== index,
                      ),
                    })
                  }
                >
                  ✕
                </Button>
              </li>
            );
          })}
        </ul>
        <Button
          type="button"
          variant="secondary"
          data-testid="recipe-editor-add-ingredient"
          disabled={draft.ingredients.length >= 40}
          onClick={() =>
            update({ ingredients: [...draft.ingredients, emptyIngredient()] })
          }
        >
          {t("editor.ingredient.add")}
        </Button>
      </section>

      <section className="space-y-1">
        <label htmlFor="editor-equipment" className="font-display text-xl">
          {t("editor.equipment")}
        </label>
        <p className="text-xs text-muted-foreground">
          {t("editor.equipment.hint")}
        </p>
        <Textarea
          id="editor-equipment"
          rows={3}
          value={draft.equipment}
          onChange={(event) => update({ equipment: event.target.value })}
        />
      </section>

      <section aria-labelledby="editor-stages" className="space-y-3">
        <h2 id="editor-stages" className="font-display text-xl">
          {t("editor.stages")}
        </h2>
        {fieldError("stages")}
        <ol className="space-y-4">
          {draft.stages.map((row, index) => (
            <li
              key={row.key}
              data-testid="recipe-editor-stage"
              className="space-y-3 rounded-xl border border-border p-3"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h3 className="font-medium">
                  {t("editor.stage.title", { number: index + 1 })}
                </h3>
                <div className="flex gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={index === 0}
                    aria-label={t("editor.stage.up", { number: index + 1 })}
                    onClick={() => moveStage(index, -1)}
                  >
                    ↑
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={index === draft.stages.length - 1}
                    aria-label={t("editor.stage.down", { number: index + 1 })}
                    onClick={() => moveStage(index, 1)}
                  >
                    ↓
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t("editor.stage.remove", { number: index + 1 })}
                    onClick={() =>
                      update({
                        stages: draft.stages.filter((_, i) => i !== index),
                      })
                    }
                  >
                    ✕
                  </Button>
                </div>
              </div>
              <div className="space-y-1">
                <label
                  htmlFor={`${row.key}-name`}
                  className="text-xs text-muted-foreground"
                >
                  {t("editor.stage.name")}
                </label>
                <Input
                  id={`${row.key}-name`}
                  value={row.name}
                  maxLength={80}
                  onChange={(event) =>
                    updateStage(index, { name: event.target.value })
                  }
                  {...described(`stages[${index}].name`)}
                />
                {fieldError(`stages[${index}].name`)}
              </div>
              <div className="space-y-1">
                <label
                  htmlFor={`${row.key}-instructions`}
                  className="text-xs text-muted-foreground"
                >
                  {t("editor.stage.instructions")} —{" "}
                  {t("editor.stage.instructionsHint")}
                </label>
                <Textarea
                  id={`${row.key}-instructions`}
                  rows={5}
                  maxLength={2000}
                  value={row.instructions}
                  onChange={(event) =>
                    updateStage(index, { instructions: event.target.value })
                  }
                  {...described(`stages[${index}].instructions`)}
                />
                {fieldError(`stages[${index}].instructions`)}
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div className="space-y-1">
                  <label
                    htmlFor={`${row.key}-active`}
                    className="text-xs text-muted-foreground"
                  >
                    {t("editor.stage.active")}
                  </label>
                  <Input
                    id={`${row.key}-active`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={1440}
                    value={row.active}
                    onChange={(event) =>
                      updateStage(index, { active: event.target.value })
                    }
                    {...described(`stages[${index}].minutes`)}
                  />
                </div>
                <div className="space-y-1">
                  <label
                    htmlFor={`${row.key}-passive`}
                    className="text-xs text-muted-foreground"
                  >
                    {t("editor.stage.passive")}
                  </label>
                  <Input
                    id={`${row.key}-passive`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={1440}
                    value={row.passive}
                    onChange={(event) =>
                      updateStage(index, { passive: event.target.value })
                    }
                    {...described(`stages[${index}].minutes`)}
                  />
                </div>
              </div>
              {fieldError(`stages[${index}].minutes`)}
              <div className="space-y-1">
                <label
                  htmlFor={`${row.key}-cues`}
                  className="text-xs text-muted-foreground"
                >
                  {t("editor.stage.cues")}
                </label>
                <Textarea
                  id={`${row.key}-cues`}
                  rows={2}
                  value={row.cues}
                  onChange={(event) =>
                    updateStage(index, { cues: event.target.value })
                  }
                />
              </div>
            </li>
          ))}
        </ol>
        <Button
          type="button"
          variant="secondary"
          data-testid="recipe-editor-add-stage"
          disabled={draft.stages.length >= 30}
          onClick={() => update({ stages: [...draft.stages, emptyStage()] })}
        >
          {t("editor.stage.add")}
        </Button>
      </section>

      {mode === "edit" ? (
        <div className="space-y-1">
          <label htmlFor="editor-change-reason" className="text-sm font-medium">
            {t("editor.changeReason")}
          </label>
          <Input
            id="editor-change-reason"
            data-testid="recipe-editor-reason"
            value={draft.reason}
            maxLength={200}
            onChange={(event) => update({ reason: event.target.value })}
          />
        </div>
      ) : null}

      {stale ? (
        <div
          role="alert"
          className="space-y-2"
          data-testid="recipe-editor-stale"
        >
          <p className="text-sm text-destructive">{t("editor.error.stale")}</p>
          <Button
            type="button"
            variant="secondary"
            onClick={() => window.location.reload()}
          >
            {t("recipes.actions.retry")}
          </Button>
        </div>
      ) : null}
      {formError ? (
        <p role="alert" className="text-sm text-destructive">
          {t(formError)}
        </p>
      ) : null}

      <div className="flex flex-wrap gap-2">
        <Button
          type="submit"
          data-testid="recipe-editor-save"
          disabled={saving || stale || !session.csrfToken}
        >
          {saving ? t("editor.saving") : t(`editor.save.${mode}`)}
        </Button>
        <Button asChild variant="secondary">
          <Link to={cancelTo}>{t("editor.cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}

/**
 * The other measure of an ingredient row, live while typing: the kitchen measure of a metric
 * amount ("≈ 1 xícara"), or what a kitchen measure is saved as ("salvo como ≈ 120 g").
 */
function MeasureHint({
  name,
  quantity,
  unit,
}: {
  name: string;
  quantity: string;
  unit: string;
}) {
  const { t, locale } = useProductionI18n();
  const household = useHouseholdMeasure();
  const decimalLocale = locale as SupportedDecimalLocale;
  const parsed = parseLocaleDecimal(quantity, decimalLocale);
  if (!parsed.ok || !(parsed.value > 0)) return null;
  let text: string;
  if (isKitchenUnit(unit)) {
    const canonical = toCanonicalAmount(name, parsed.value, unit);
    const amount = `${formatLocaleDecimal(canonical.quantity, decimalLocale)} ${t(`recipes.unit.${canonical.unit}`)}`;
    text = t("editor.unit.savedAs", {
      amount: canonical.approximate ? `≈ ${amount}` : amount,
    });
  } else {
    text = household({ displayName: name, quantity: parsed.value, unit });
  }
  return text ? (
    <p
      data-testid="editor-measure-hint"
      className="text-xs text-muted-foreground sm:col-span-full"
    >
      {text}
    </p>
  ) : null;
}
