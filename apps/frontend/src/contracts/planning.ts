/**
 * Application-owned menu planning and shopping contracts (PLAN-0029). Failures are thrown as
 * `RecipeApiError` (`@/contracts/recipes`): planning shares the recipe boundary's Problem Details
 * mapping and error codes.
 */

/** `draft`, `accepted`, or `archived`. */
export type MenuStatus = "draft" | "accepted" | "archived" | string;

/** `planned`, `skipped`, `cooking`, or `cooked` (the last two come from the cooking session started from the entry). */
export type MenuEntryStatus =
  | "planned"
  | "skipped"
  | "cooking"
  | "cooked"
  | string;

/** Menu list row. */
export interface MenuSummary {
  menuId: string;
  name: string;
  /** ISO date (`YYYY-MM-DD`) in the owner's calendar. */
  startDate: string;
  endDate: string;
  status: MenuStatus;
  entryCount: number;
  updatedAt: string;
}

/** One meal of a menu. */
export interface MenuEntry {
  entryId: string;
  recipeId: string;
  recipeName: string;
  revisionNumber: number;
  revisionPinned: boolean;
  /** Intended day, or null for "some time in the period". */
  plannedDate: string | null;
  mealType: string | null;
  servings: number;
  status: MenuEntryStatus;
  executionId: string | null;
  /** Whether the meal reserves pantry quantity once its menu is in use (PLAN-0029 Phase 3). */
  reserveIngredients: boolean;
  /**
   * Present for meals still to cook that reserve ingredients: real reservations on a menu in use,
   * a simulation on a draft.
   */
  readiness: MealReadiness | null;
}

/** `ready` when every required pantry ingredient is reserved; `missing` lists what is not. */
export interface MealReadiness {
  entryId: string;
  readiness: "ready" | "missing" | string;
  missing: Array<{ name: string; unit: string; quantity: number }>;
}

/** Quantity of a lot reserved for one meal. */
export interface LotReservation {
  menuEntryId: string;
  recipeName: string;
  plannedDate: string | null;
  quantity: number;
}

/** One lot with what is promised from it; quantities in `unit`. */
export interface LotCommitment {
  lotId: string;
  productName: string;
  unit: string;
  quantity: number;
  protectedQuantity: number;
  reservedQuantity: number;
  freeQuantity: number;
  /** `whole`, or `minimum` with the quantity to keep. */
  protection: { kind: string; minimumQuantity: number | null } | null;
  reservations: LotReservation[];
}

/** Pantry commitments; reading or changing them never changes physical quantity. */
export interface Reservations {
  from: string;
  lots: LotCommitment[];
  meals: MealReadiness[];
}

/** A menu with its meals, ordered by day (flexible last) and meal. */
export interface Menu {
  menuId: string;
  name: string;
  startDate: string;
  endDate: string;
  status: MenuStatus;
  acceptedAt: string | null;
  updatedAt: string;
  entries: MenuEntry[];
}

/** A meal of an accepted menu for the requested day. */
export interface TodayMenuEntry {
  menuId: string;
  menuName: string;
  entry: MenuEntry;
}

/** One shopping line; quantities are in `unit` (`Gram`, `Milliliter`, `Unit`). */
export interface ShoppingItem {
  itemKey: string;
  displayName: string;
  unit: string;
  /** `required`, `assumed`, `optional`, `runs_out`, or `available`. */
  category: string;
  requiredQuantity: number;
  availableQuantity: number;
  /** Whether `availableQuantity` includes an estimated pantry amount (shown with "~"). */
  availableIsEstimate?: boolean;
  /**
   * Store section to group the line by (PLAN-0037 Phase 3): `produce`, `butcher_fish`,
   * `dairy_eggs`, `bakery`, `dry_goods`, `drinks`, or `other`.
   */
  storeSection?: string;
  toBuyQuantity: number;
  checked: boolean;
  /**
   * Package size the owner confirmed for this item, in `unit` (PLAN-0029 Phase 4). Absent when
   * none was confirmed: Cocinaris never assumes one.
   */
  packageSize?: number | null;
  /** Whole packages that cover `toBuyQuantity`; absent without a package size or nothing to buy. */
  packageCount?: number | null;
  /** What those packages leave after the menu, in `unit`. */
  expectedRemainder?: number | null;
  uses: Array<{
    entryId: string;
    recipeName: string;
    plannedDate: string | null;
    quantity: number;
    optional: boolean;
    /** What this meal still lacks after the pantry is allocated in plan order. */
    missingQuantity?: number;
  }>;
}

/**
 * Something to do before a planned meal (PLAN-0029 Phase 4). Derived on every read, so it
 * disappears once its cause does.
 */
export interface PreparationReminder {
  menuId: string;
  entryId: string;
  recipeName: string;
  plannedDate: string;
  mealType: string | null;
  /** `buy` (not covered), `thaw` (a reserved lot is frozen), or `use_by` (a reserved lot's date is before the meal). */
  kind: string;
  itemName: string;
  unit: string | null;
  quantity: number | null;
  /** Day to act by (`YYYY-MM-DD`). */
  dueDate: string;
  lotId: string | null;
  useByDate: string | null;
}

/** A menu's derived shopping list; reading it never changes the pantry. */
export interface ShoppingList {
  menuId: string;
  from: string;
  mealCount: number;
  items: ShoppingItem[];
}

/** Fields of an entry; every change replaces all of them. */
export interface MenuEntryInput {
  plannedDate: string | null;
  mealType: string | null;
  servings: number;
  /** `planned` or `skipped`. */
  status: string;
  /** Releases (false) or restores (true) the meal's reservations; unchanged when omitted. */
  reserveIngredients?: boolean;
}

interface Mutation {
  csrfToken: string;
  signal?: AbortSignal;
}

/** Live or unavailable planning boundary. Every failure is a `RecipeApiError`. */
export interface PlanningRepository {
  listMenus(
    signal?: AbortSignal,
    options?: { archived?: boolean },
  ): Promise<MenuSummary[]>;
  getMenu(menuId: string, signal?: AbortSignal): Promise<Menu>;
  /** Creates a draft; a retry with the same key returns the same menu. */
  createMenu(
    input: { name: string | null; startDate: string; endDate: string },
    options: Mutation & { idempotencyKey: string },
  ): Promise<Menu>;
  changeMenu(
    menuId: string,
    input: { name: string | null; startDate: string; endDate: string },
    options: Mutation,
  ): Promise<Menu>;
  acceptMenu(menuId: string, options: Mutation): Promise<Menu>;
  archiveMenu(menuId: string, options: Mutation): Promise<Menu>;
  /** Adds a recipe; a retry with the same key does not add it twice. */
  addEntry(
    menuId: string,
    input: {
      recipeId: string;
      plannedDate: string | null;
      mealType: string | null;
      servings: number | null;
    },
    options: Mutation & { idempotencyKey: string },
  ): Promise<Menu>;
  changeEntry(
    menuId: string,
    entryId: string,
    input: MenuEntryInput,
    options: Mutation,
  ): Promise<Menu>;
  removeEntry(
    menuId: string,
    entryId: string,
    options: Mutation,
  ): Promise<Menu>;
  /** `from` is the owner's current day (`YYYY-MM-DD`); earlier dated meals count as missed. */
  getShopping(
    menuId: string,
    from: string,
    signal?: AbortSignal,
  ): Promise<ShoppingList>;
  setChecked(
    menuId: string,
    itemKey: string,
    checked: boolean,
    options: Mutation,
  ): Promise<void>;
  /** Protections and menu reservations from `from` (the owner's current day). */
  getReservations(from: string, signal?: AbortSignal): Promise<Reservations>;
  /** Protects a whole lot, or keeps at least `minimumQuantity` of it. */
  protectLot(
    lotId: string,
    protection: { kind: "whole" | "minimum"; minimumQuantity: number | null },
    options: Mutation,
  ): Promise<Reservations>;
  unprotectLot(lotId: string, options: Mutation): Promise<Reservations>;
  /** Meals of accepted menus for `date`, the owner's current day. */
  getToday(date: string, signal?: AbortSignal): Promise<TodayMenuEntry[]>;
  /**
   * Records the package size the owner buys an item in, or forgets it with `null`. Optional:
   * without it the shopping list offers no package sizes.
   */
  setPackageSize?(
    itemKey: string,
    size: number | null,
    options: Mutation,
  ): Promise<void>;
  /**
   * Pre-meal reminders for the dated meals of menus in use, from `from` (the owner's current
   * day) for `days` days. Optional: without it "Hoje" shows no reminders.
   */
  getPreparation?(
    from: string,
    days: number,
    signal?: AbortSignal,
  ): Promise<PreparationReminder[]>;
}
