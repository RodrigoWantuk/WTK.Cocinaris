/**
 * Application-owned shopping plan contracts (PLAN-0062). Failures are thrown as `RecipeApiError`
 * (`@/contracts/recipes`), which shares the recipe boundary's Problem Details mapping.
 */

/** The four meal types, in the order screens show them. */
export const MEAL_ORDER = ["breakfast", "lunch", "dinner", "snack"] as const;

/** One of {@link MEAL_ORDER}. */
export type MealType = (typeof MEAL_ORDER)[number];

/** How many of each meal a menu wants, independent of its days (PLAN-0065). */
export type MealCounts = Record<MealType, number>;

/** The switches above the options. They belong to one menu and never update the profile. */
export interface ShoppingPlanPreferences {
  /** Whether profile tastes reach the AI. Restrictions and dislikes always do. */
  usePreferences: boolean;
  /** Whether to build on the pantry and the dishes already chosen. */
  usePantry: boolean;
  goal: "quick" | "short" | "save" | null;
}

/** One meal type of a menu, with what is wanted, chosen, and offered (at most six, best first). */
export interface ShoppingPlanMealGroup {
  mealType: string;
  wanted: number;
  filled: number;
  /** Empty once nothing is missing. */
  options: ShoppingPlanCard[];
}

/** Setup answers. They belong to one plan and never update the profile. */
export interface ShoppingPlanSetup {
  /** The browser's local day (`YYYY-MM-DD`); the server does not know the owner's time zone. */
  today: string;
  days: number;
  mealTypes: string[];
  mealsOut: number;
  /** `once`, `mix`, or `daily`. */
  cookingStyle: string;
  /** `waste`, `short`, `save`, or `time`. */
  priority: string;
  prepDate: string | null;
  prepMinutes: number | null;
  anchorIngredient: string | null;
  people: number;
  /** How many of each meal; null on menus made before PLAN-0065. */
  mealCounts?: Partial<Record<string, number>> | null;
  /** First and last day of the period (`YYYY-MM-DD`). */
  startDate?: string;
  endDate?: string;
}

/** What a new menu is made from: the period, the people, and how many of each meal. */
export interface CreateShoppingPlanInput {
  today: string;
  days: number;
  people: number;
  mealCounts?: MealCounts | null;
  /** First day of the period; today when null. */
  startDate?: string | null;
  /** An accepted menu to repeat: its answers then replace these, except `today`. */
  fromMenuId?: string | null;
}

/** An accepted plan that can be repeated. */
export interface ShoppingPlanRecent {
  menuId: string;
  /** The menu's name; empty until the person gives one. */
  name: string;
  startDate: string;
  endDate: string;
  mealsTotal: number;
  /** Dishes whose recipe still exists. */
  dishes: string[];
}

/** Defaults for the setup screen. */
export interface ShoppingPlanStart {
  lastSetup: ShoppingPlanSetup | null;
  people: number;
  restrictions: Array<{ code: string; category: string }>;
  /** Codes the restriction guard cannot check; the screen must say so. */
  unverifiableRestrictions: string[];
  hasFreezer: boolean;
  current: { sessionId: string; menuId: string; status: string } | null;
  /** The newest accepted plans, at most five. */
  recent: ShoppingPlanRecent[];
  /** The last menu's switches, as defaults; null before the first. */
  lastPreferences?: ShoppingPlanPreferences | null;
}

/** A card to decide on. `newItemCount` is an estimate until the recipe is expanded. */
export interface ShoppingPlanCard {
  cardId: string;
  source: "saved" | "idea" | "ai" | string;
  name: string;
  summary: string;
  mealType: string;
  servings: number;
  totalMinutes: number;
  activeMinutes: number;
  newItemCount: number;
  reusedNames: string[];
  /** `uses_attention_item`, `reuses_list_items`, or null. */
  reasonCode: string | null;
  reasonIngredient: string | null;
  overBudget: boolean;
  guardCode: string | null;
  guardIngredient: string | null;
  leadIngredient: string | null;
  ingredients: Array<{
    name: string;
    quantity: number;
    unit: string;
    optional: boolean;
  }>;
  /** Present when the card is a reusable base rather than a dish. */
  component?: {
    outputName: string;
    outputQuantity: number;
    outputUnit: string;
    usedBy: string[];
  } | null;
  /** What the person typed, when this card answers a specific request. */
  requestText?: string | null;
}

/** An accepted card. */
export interface ShoppingPlanAccepted {
  cardId: string;
  name: string;
  mealType: string;
  repeats: number;
  mode: "yield" | "again" | null | string;
  /** `none`, `pending`, `ready`, or `failed`. */
  expansionState: string;
  failureCode: string | null;
  recipeId: string | null;
  guardCode: string | null;
  guardIngredient: string | null;
  /** Whether it is a base, which fills no meal. */
  isComponent?: boolean;
  /** The day it was given, if any; most dishes have none. */
  plannedDate?: string | null;
  totalMinutes?: number;
}

/** The state every plan route returns. */
export interface ShoppingPlan {
  sessionId: string;
  menuId: string;
  /** `building`, `reviewing`, `finished`, or `abandoned`. */
  status: string;
  setup: ShoppingPlanSetup;
  progress: {
    mealsTotal: number;
    mealsFilled: number;
    listItemCount: number;
    sessionMinutesUsed: number;
    sessionMinutesBudget: number | null;
  };
  queue: ShoppingPlanCard[];
  accepted: ShoppingPlanAccepted[];
  needsBatch: boolean;
  offerMoreOptions: boolean;
  exhausted: boolean;
  canUndo: boolean;
  /** Each meal type with a count above zero, in meal order (PLAN-0065). */
  groups: ShoppingPlanMealGroup[];
  preferences: ShoppingPlanPreferences;
}

/** One dish of a preparation session. */
export interface ShoppingPlanRouteRecipe {
  entryId: string;
  recipeId: string;
  name: string;
  servings: number;
  isComponent: boolean;
  producesName: string | null;
  /** `planned`, `cooking`, or `cooked`. */
  status: string;
  executionId: string | null;
  /** Already for the portions being cooked. */
  ingredients: Array<{
    name: string;
    quantity: number;
    unit: string;
    optional: boolean;
  }>;
}

/** One task of a preparation session; minutes count from the start of the session. */
export interface ShoppingPlanRouteTask {
  taskKey: string;
  entryId: string;
  /** `preparation` or `stage`. */
  kind: string;
  name: string;
  instructions: string;
  sensoryCues: string[];
  activeMinutes: number;
  passiveMinutes: number;
  startMinute: number;
  activeEndMinute: number;
  endMinute: number;
  dependsOn: string[];
}

/** How far a preparation session has come. The whole state is saved each time. */
export interface ShoppingPlanRouteProgress {
  /** Tasks whose hands-on part is finished. */
  done: string[];
  skipped: string[];
  /** Waits running in the background, as target times. */
  timers: Array<{ taskKey: string; endsAt: string }>;
}

/** A preparation session's route: dishes with bases first, tasks in the order the cook starts them. */
export interface ShoppingPlanRoute {
  sessionId: string;
  menuId: string;
  prepDate: string | null;
  totalMinutes: number;
  budgetMinutes: number | null;
  overBudget: boolean;
  recipes: ShoppingPlanRouteRecipe[];
  tasks: ShoppingPlanRouteTask[];
  progress: ShoppingPlanRouteProgress;
}

/** "More options": closed codes only, never free text. */
export interface ShoppingPlanBatchOptions {
  dishFamilies?: string[];
  cuisine?: string | null;
  time?: string | null;
  /** `use` sticks to the pantry and chosen dishes; `free` ignores them. */
  pantry?: "use" | "free" | null;
  /** Compared with `referenceCardId`. */
  similarity?: "any" | "similar" | "different" | null;
  referenceCardId?: string | null;
  /** At most 40 characters. */
  hint?: string | null;
  /** One meal type to ask for (PLAN-0065); the fields above are then ignored by the server. */
  mealType?: string | null;
  /** A specific dish in the person's words, at most 120 characters; for this request only. */
  request?: string | null;
  /** A dish of this menu to stay close to. */
  likeCardId?: string | null;
  /** Whether earlier unseen dishes of this meal give way, as when a switch changes. */
  replaceOffers?: boolean;
}

interface Mutation {
  csrfToken: string;
  signal?: AbortSignal;
  language?: string;
}

/** Shopping plan repository. Every method is owner-scoped by the backend session. */
export interface ShoppingPlanRepository {
  getStart(signal?: AbortSignal): Promise<ShoppingPlanStart>;
  /** Goes back to choosing dishes from the review. */
  reopen(
    sessionId: string,
    options: { csrfToken: string },
  ): Promise<ShoppingPlan>;
  /** Creates a menu; a retry with the same key returns the same one. */
  create(
    input: CreateShoppingPlanInput,
    options: Mutation & { idempotencyKey: string },
  ): Promise<ShoppingPlan>;
  /** Chooses one of the person's saved recipes for a meal; restrictions still apply. */
  chooseRecipe(
    sessionId: string,
    recipeId: string,
    mealType: string,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  /** Sets the switches; the server re-ranks the options. */
  setPreferences(
    sessionId: string,
    preferences: ShoppingPlanPreferences,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  /** Changes how many of each meal; never below what is chosen (`meal_count_below_chosen`). */
  setMealCounts(
    sessionId: string,
    counts: MealCounts,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  get(sessionId: string, signal?: AbortSignal): Promise<ShoppingPlan>;
  /** Asks for more AI cards; a retry with the same key spends no second batch. */
  addBatch(
    sessionId: string,
    input: ShoppingPlanBatchOptions,
    options: Mutation & { idempotencyKey: string },
  ): Promise<ShoppingPlan>;
  decide(
    sessionId: string,
    cardId: string,
    input: {
      decision: "accept" | "reject";
      mode?: "yield" | "again" | null;
      repeats?: number | null;
      /** The meal it fills; the card's own when absent. */
      mealType?: string | null;
      plannedDate?: string | null;
    },
    options: Mutation,
  ): Promise<ShoppingPlan>;
  setReason(
    sessionId: string,
    cardId: string,
    reason: "dislike" | "too_much_work" | "tired_of",
    options: Mutation,
  ): Promise<ShoppingPlan>;
  undo(sessionId: string, options: Mutation): Promise<ShoppingPlan>;
  /** Expands an accepted card and puts its meals on the menu; safe to retry. */
  expand(
    sessionId: string,
    cardId: string,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  removeCard(
    sessionId: string,
    cardId: string,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  review(sessionId: string, options: Mutation): Promise<ShoppingPlan>;
  replace(
    sessionId: string,
    entryId: string,
    options: Mutation,
  ): Promise<ShoppingPlan>;
  accept(sessionId: string, options: Mutation): Promise<ShoppingPlan>;
  abandon(sessionId: string, options: Mutation): Promise<ShoppingPlan>;
  /** Gets the preparation session of an accepted plan; a plan not yet accepted fails with 422. */
  getRoute(sessionId: string, signal?: AbortSignal): Promise<ShoppingPlanRoute>;
  /** Replaces the session's progress; safe to retry. */
  saveRouteProgress(
    sessionId: string,
    progress: ShoppingPlanRouteProgress,
    options: Mutation,
  ): Promise<void>;
  /** Finds the plan whose session is still to finish, for a menu or for a day; null when none. */
  findRouteLink(
    query: { menuId?: string; date?: string },
    signal?: AbortSignal,
  ): Promise<string | null>;
}
