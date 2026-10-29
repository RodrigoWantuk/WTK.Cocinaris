/**
 * Application-owned inventory presentation models.
 * Isolated from generated OpenAPI DTO shapes used by the wire client.
 */

export type StorageLocation = "Pantry" | "Refrigerator" | "Freezer" | "Other";

export type PackageState = "Sealed" | "Opened" | "Unknown";

export type AvailabilityState = "Available" | "Low" | "Unavailable";

export type MeasuredUnit = "Gram" | "Milliliter" | "Unit";

export type InventoryQuantity =
  | {
      kind: "measured";
      value: number;
      unit: MeasuredUnit;
      /** The owner's approximation ("half a package"), not a count or weighing (PLAN-0054). Absent means exact. */
      estimated?: true;
    }
  | {
      kind: "qualitative";
      availability: AvailabilityState;
    };

export type LifecycleState =
  | "AsAcquired"
  | "Prepared"
  | "Frozen"
  | "Thawing"
  | "Thawed";

export type TransitionKind =
  | "Open"
  | "Move"
  | "Freeze"
  | "StartThaw"
  | "CompleteThaw";

/** Current lifecycle facts; instants are ISO 8601 UTC strings of the latest transition of each kind. */
export interface LotLifecycle {
  state: LifecycleState | string;
  openedAt: string | null;
  frozenAt: string | null;
  thawStartedAt: string | null;
  thawedAt: string | null;
  /** Source lot when this lot was split off by a partial transition. */
  parentLotId: string | null;
}

/** Lifecycle facts of a transition history entry; `relatedLotId` is the other side of a split. */
export interface LotTransitionFacts {
  kind: TransitionKind | string;
  previousState: LifecycleState | string;
  resultingState: LifecycleState | string;
  previousStorageLocation: StorageLocation | string;
  resultingStorageLocation: StorageLocation | string;
  relatedLotId: string | null;
}

export type AttentionState =
  | "LikelyDiscard"
  | "CheckCondition"
  | "UsePriority"
  | "UseSoon"
  | "EstimateUncertain"
  | "Normal";

/**
 * Advisory attention computed by the backend in the user's calendar (PLAN-0037 Phase 2). It is
 * guidance with reasons, never a safety guarantee; the user decides.
 */
export interface LotAttention {
  state: AttentionState | string;
  /** Calendar date YYYY-MM-DD, or null when no date-based estimate exists. */
  estimatedUseBy: string | null;
  evidence:
    | "PrintedDate"
    | "PreparedRecord"
    | "CuratedRule"
    | "Unknown"
    | string;
  /** Stable reason codes, localized under `inventory.attention.reason.*`. */
  reasons: string[];
}

export interface InventoryLotView {
  lotId: string;
  productId: string;
  productName: string;
  quantity: InventoryQuantity;
  storageLocation: StorageLocation | string;
  customLocation: string | null;
  packageState: PackageState | string | null;
  /** Calendar date YYYY-MM-DD; never a timezone-shifted instant. */
  printedExpirationDate: string | null;
  notes: string | null;
  version: string;
  etag: string;
  createdAt: string;
  updatedAt: string;
  lifecycle: LotLifecycle;
  /** Absent only when an older API did not send it. */
  attention?: LotAttention;
}

export interface InventoryListPage {
  items: InventoryLotView[];
  nextCursor: string | null;
}

export interface InventoryHistoryEntry {
  entryId: string;
  kind: string;
  type: string | null;
  reasonCode: string | null;
  changedFields: string[] | null;
  occurredAt: string;
  transition: LotTransitionFacts | null;
}

export type AdjustmentType =
  | "Consume"
  | "Discard"
  | "Correct"
  | "AvailabilityChanged";

export interface CreateLotInput {
  productName: string;
  quantity: InventoryQuantity;
  storageLocation: StorageLocation;
  customLocation?: string | null;
  packageState?: PackageState | null;
  printedExpirationDate?: string | null;
  notes?: string | null;
}

export interface UpdateLotInput {
  productName?: string;
  storageLocation?: StorageLocation;
  customLocation?: string | null;
  packageState?: PackageState | null;
  printedExpirationDate?: string | null;
  notes?: string | null;
}

export interface AdjustLotInput {
  type: AdjustmentType;
  value?: number | null;
  availabilityState?: AvailabilityState | null;
  reasonCode?: string | null;
  note?: string | null;
}

/**
 * One lifecycle transition. `partialValue`, in the lot's unit, splits that quantity into a new
 * derived lot that takes the transition; the source keeps the remainder.
 */
export interface TransitionLotInput {
  kind: TransitionKind;
  partialValue?: number | null;
  unit?: MeasuredUnit | null;
  targetStorageLocation?: StorageLocation | null;
  targetCustomLocation?: string | null;
}

export type InventoryApiErrorCode =
  | "authentication_required"
  | "validation_failed"
  | "precondition_required"
  | "precondition_failed"
  | "not_found"
  | "conflict"
  | "unavailable"
  | "cancelled"
  | "unexpected";

export class InventoryApiError extends Error {
  readonly code: InventoryApiErrorCode;
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(
    code: InventoryApiErrorCode,
    message: string,
    status: number,
    fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
    this.name = "InventoryApiError";
    this.code = code;
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

export interface InventoryRepository {
  listLots(params?: {
    search?: string;
    status?: string;
    storageLocation?: string;
    cursor?: string;
    pageSize?: number;
    signal?: AbortSignal;
  }): Promise<InventoryListPage>;
  getLot(lotId: string, signal?: AbortSignal): Promise<InventoryLotView>;
  createLot(
    input: CreateLotInput,
    options: {
      csrfToken: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<InventoryLotView>;
  updateLot(
    lotId: string,
    input: UpdateLotInput,
    options: {
      csrfToken: string;
      etag: string;
      signal?: AbortSignal;
    },
  ): Promise<InventoryLotView>;
  adjustLot(
    lotId: string,
    input: AdjustLotInput,
    options: {
      csrfToken: string;
      etag: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<InventoryLotView>;
  /** Lots needing attention, most urgent first (at most 100). */
  listAttention(signal?: AbortSignal): Promise<InventoryLotView[]>;
  /** Applies a lifecycle transition and returns the source lot. */
  transitionLot(
    lotId: string,
    input: TransitionLotInput,
    options: {
      csrfToken: string;
      etag: string;
      idempotencyKey: string;
      signal?: AbortSignal;
    },
  ): Promise<InventoryLotView>;
  deleteLot(
    lotId: string,
    options: { csrfToken: string; etag: string; signal?: AbortSignal },
  ): Promise<void>;
  getHistory(
    lotId: string,
    signal?: AbortSignal,
  ): Promise<InventoryHistoryEntry[]>;
}
