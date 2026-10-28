import type {
  InventoryLotView,
  StorageLocation,
  TransitionKind,
} from "@/adapters/live/inventoryTypes";

/**
 * Client-side mirror of the backend transition rules (PLAN-0037), used only to offer sensible
 * choices. The backend remains authoritative and rejects anything these helpers would allow by
 * mistake with `domain_rule_violated`.
 */
export function allowedTransitions(lot: InventoryLotView): TransitionKind[] {
  const inFreezer = lot.storageLocation === "Freezer";
  const kinds: TransitionKind[] = [];
  if (lot.packageState !== "Opened") kinds.push("Open");
  if (!inFreezer) kinds.push("Move", "Freeze");
  if (inFreezer) kinds.push("StartThaw");
  if (lot.lifecycle.state === "Thawing") kinds.push("CompleteThaw");
  return kinds;
}

/** Whether the transition can apply to part of the lot (measured lots only). */
export function supportsPartial(
  kind: TransitionKind,
  lot: InventoryLotView,
): boolean {
  return (
    lot.quantity.kind === "measured" &&
    (kind === "Move" || kind === "Freeze" || kind === "StartThaw")
  );
}

/**
 * Destination choices for a transition. `null` means "keep the current location" and is offered
 * only where the backend treats the target as optional. Custom ("Other") locations are not
 * offered here; they remain available through the lot edit form.
 */
export function targetOptions(
  kind: TransitionKind,
  lot: InventoryLotView,
): (StorageLocation | null)[] {
  const others = (["Pantry", "Refrigerator"] as const).filter(
    (location) => location !== lot.storageLocation,
  );
  switch (kind) {
    case "Move":
      return [...others];
    case "StartThaw":
      return ["Refrigerator", "Pantry"];
    case "Open":
      return lot.storageLocation === "Freezer" ? [null] : [null, ...others];
    default:
      return [];
  }
}
