/**
 * Application-owned shopping plan contracts (PLAN-0062). Failures are thrown as `RecipeApiError`
 * (`@/contracts/recipes`), which shares the recipe boundary's Problem Details mapping.
 */

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
}

interface Mutation {
  csrfToken: string;
  signal?: AbortSignal;
  language?: string;
}

/** Shopping plan repository. Every method is owner-scoped by the backend session. */
export interface ShoppingPlanRepository {
  getStart(signal?: AbortSignal): Promise<ShoppingPlanStart>;
  /** Creates a plan; a retry with the same key returns the same plan. */
  create(
    input: Omit<ShoppingPlanSetup, "anchorIngredient"> & {
      anchorIngredient: string | null;
      /** An accepted plan to repeat: its answers then replace these, except `today`. */
      fromMenuId?: string | null;
    },
    options: Mutation & { idempotencyKey: string },
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
