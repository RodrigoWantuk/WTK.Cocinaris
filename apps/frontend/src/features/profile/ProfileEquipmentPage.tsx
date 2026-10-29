import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useProfileWorkspace } from "./ProfileProvider";
import { createCustomStableCode, isCustomStableCode } from "./customCodes";
import { EQUIPMENT_CODES } from "./catalog/profileCatalogCodes";
import { resolveLabel } from "./catalog/profileCatalog";
import {
  FieldErrorSummary,
  splitKnownFieldErrors,
  useFocusFirstFieldError,
  type FieldErrorItem,
} from "./fieldErrors";
import { useRegisterUnsavedChanges } from "./UnsavedChangesCoordinator";
import { describeProfileMutationError } from "./describeProfileMutationError";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { ProfileApiError, type EquipmentInput } from "@/contracts/profile";
import { Button, buttonVariants } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

type Translate = ReturnType<typeof useProductionI18n>["t"];

/** Backend bounds, `ValidateEquipmentCommands` in `ProfileApplicationWorkflow.cs`. */
const CUSTOM_NAME_MAX_LENGTH = 80;
const CAPACITY_UNIT_MAX_LENGTH = 20;
const CONSTRAINT_NOTE_MAX_LENGTH = 200;

interface DraftItem {
  /** Local-only React key; never sent to the backend. */
  key: string;
  stableCode: string;
  customName: string;
  capacity: string;
  capacityUnit: string;
  constraintNote: string;
}

interface DraftItemErrors {
  stableCode?: string;
  customName?: string;
  capacity?: string;
  capacityUnit?: string;
  constraintNote?: string;
  duplicateCode?: string;
}

function localKey(): string {
  const cryptoObj = globalThis.crypto as Crypto | undefined;
  return cryptoObj?.randomUUID ? cryptoObj.randomUUID() : String(Math.random());
}

function toDraftItems(
  entries: readonly {
    entryId: string;
    stableCode: string;
    customName: string | null;
    capacity: number | null;
    capacityUnit: string | null;
    constraintNote: string | null;
    sortOrder: number;
  }[],
): DraftItem[] {
  return [...entries]
    .sort((a, b) => a.sortOrder - b.sortOrder)
    .map((entry) => ({
      key: entry.entryId,
      stableCode: entry.stableCode,
      customName: entry.customName ?? "",
      capacity: entry.capacity != null ? String(entry.capacity) : "",
      capacityUnit: entry.capacityUnit ?? "",
      constraintNote: entry.constraintNote ?? "",
    }));
}

function toEquipmentInputs(items: DraftItem[]): EquipmentInput[] {
  return items.map((item) => ({
    stableCode: item.stableCode,
    customName: item.customName.trim() || null,
    capacity: item.capacity.trim() === "" ? null : Number(item.capacity),
    capacityUnit: item.capacityUnit.trim() || null,
    constraintNote: item.constraintNote.trim() || null,
  }));
}

/**
 * Validates every draft item against the same bounds the backend enforces
 * (`ValidateEquipmentCommands`): unique stable codes, a required, bounded custom
 * name for `custom_*` entries, a non-negative finite capacity, a bounded capacity
 * unit, a bounded constraint note, and capacity/unit pair coherence (a unit with no
 * capacity, or a capacity with no unit, is ambiguous and rejected locally rather
 * than silently sent half-filled).
 */
function validateEquipmentDraft(
  items: DraftItem[],
  t: Translate,
): { errors: Record<string, DraftItemErrors>; hasErrors: boolean } {
  const errors: Record<string, DraftItemErrors> = {};
  const seenCodes = new Set<string>();

  for (const item of items) {
    const itemErrors: DraftItemErrors = {};
    const isCustom = isCustomStableCode(item.stableCode);
    const trimmedName = item.customName.trim();
    if (isCustom && trimmedName === "") {
      itemErrors.customName = t("profile.equipment.error.customNameRequired");
    } else if (trimmedName.length > CUSTOM_NAME_MAX_LENGTH) {
      itemErrors.customName = t("profile.equipment.error.customNameTooLong");
    }

    const capacityTrimmed = item.capacity.trim();
    const unitTrimmed = item.capacityUnit.trim();
    if (capacityTrimmed !== "") {
      const parsed = Number(capacityTrimmed);
      if (!Number.isFinite(parsed) || parsed < 0) {
        itemErrors.capacity = t("profile.equipment.error.capacityInvalid");
      }
    }
    if (unitTrimmed.length > CAPACITY_UNIT_MAX_LENGTH) {
      itemErrors.capacityUnit = t(
        "profile.equipment.error.capacityUnitTooLong",
      );
    } else if (capacityTrimmed !== "" && unitTrimmed === "") {
      itemErrors.capacityUnit = t(
        "profile.equipment.error.capacityUnitRequired",
      );
    } else if (
      capacityTrimmed === "" &&
      unitTrimmed !== "" &&
      !itemErrors.capacity
    ) {
      itemErrors.capacity = t("profile.equipment.error.capacityRequired");
    }

    if (item.constraintNote.trim().length > CONSTRAINT_NOTE_MAX_LENGTH) {
      itemErrors.constraintNote = t(
        "profile.equipment.error.constraintNoteTooLong",
      );
    }

    if (seenCodes.has(item.stableCode)) {
      itemErrors.duplicateCode = t("profile.equipment.error.duplicateCode");
    } else {
      seenCodes.add(item.stableCode);
    }

    if (Object.keys(itemErrors).length > 0) {
      errors[item.key] = itemErrors;
    }
  }

  return { errors, hasErrors: Object.keys(errors).length > 0 };
}

const KNOWN_EQUIPMENT_ERROR_SUFFIXES = [
  "stableCode",
  "customName",
  "capacity",
  "capacityUnit",
  "constraintNote",
] as const;

/**
 * Equipment editor: add from catalog or custom, edit fields, remove, and reorder via
 * explicit Move Up/Down controls. Array order is canonical, so the whole ordered
 * collection is submitted together with the shared workspace etag on save.
 */
export function ProfileEquipmentPage() {
  const {
    status,
    workspace,
    canMutate,
    lastMutationError,
    clearMutationError,
    replaceEquipment,
    reload,
  } = useProfileWorkspace();
  const { t, locale } = useProductionI18n();

  const [draft, setDraft] = useState<DraftItem[] | null>(null);
  const [pendingCode, setPendingCode] = useState("");
  const [customName, setCustomName] = useState("");
  const [actionError, setActionError] = useState<string | null>(null);
  const [itemErrors, setItemErrors] = useState<Record<string, DraftItemErrors>>(
    {},
  );
  const [unknownErrorCount, setUnknownErrorCount] = useState(0);
  const [liveMessage, setLiveMessage] = useState("");
  const focusAfterRemoveRef = useRef(false);
  /** Order submitted with the in-flight/last save, used to map `entries[i].*` backend errors back to draft item keys. */
  const submittedOrderRef = useRef<string[]>([]);

  const isDirty = draft !== null;
  const discardDraft = useCallback(() => {
    setDraft(null);
    setItemErrors({});
    setActionError(null);
    setUnknownErrorCount(0);
  }, []);
  useRegisterUnsavedChanges(isDirty, discardDraft);

  useEffect(() => {
    if (!focusAfterRemoveRef.current) return;
    focusAfterRemoveRef.current = false;
    const target = document.getElementById("profile-equipment-catalog-select");
    if (target instanceof HTMLElement) target.focus();
  });

  // Computed unconditionally (with safe fallbacks) so every hook below runs on
  // every render regardless of `status`/`workspace` — the early returns for
  // session/loading/error states happen after all hooks, per rules of hooks.
  const items = draft ?? toDraftItems(workspace?.equipment.entries ?? []);
  const errorItems: FieldErrorItem[] = Object.entries(itemErrors).flatMap(
    ([key, fieldErrors]) =>
      (Object.entries(fieldErrors) as [keyof DraftItemErrors, string][])
        .filter(([field]) => field !== "duplicateCode")
        .map(([field, message]) => {
          const item = items.find((candidate) => candidate.key === key);
          const label = resolveLabel(
            locale,
            "equipment",
            item?.stableCode ?? "",
          );
          const fieldLabel =
            field === "stableCode"
              ? t("profile.equipment.stableCodeInvalid")
              : t(
                  `profile.equipment.${field === "customName" ? "customNamePlaceholder" : field}` as never,
                );

          return {
            id:
              field === "stableCode"
                ? `profile-equipment-entry-${key}`
                : `profile-equipment-${field}-${key}`,
            label: `${label} — ${fieldLabel}`,
            message,
          };
        }),
  );
  useFocusFirstFieldError(errorItems);

  if (status === "session") {
    return (
      <div role="alert" data-testid="profile-equipment-session">
        <p>{t("profile.error.session")}</p>
      </div>
    );
  }

  if ((status === "loading" || status === "idle") && !workspace) {
    return (
      <p role="status" data-testid="profile-equipment-loading">
        {t("profile.loading")}
      </p>
    );
  }

  if (!workspace) {
    return (
      <div role="alert" data-testid="profile-equipment-error">
        <p>{t("profile.error.load")}</p>
        <Button type="button" onClick={() => void reload()}>
          {t("profile.actions.retry")}
        </Button>
      </div>
    );
  }

  const blocked = status === "version_conflict";
  const usedCodes = new Set(items.map((item) => item.stableCode));
  const catalogOptions = EQUIPMENT_CODES.filter((code) => !usedCodes.has(code));

  function ensureDraft(): DraftItem[] {
    return draft ?? toDraftItems(workspace!.equipment.entries);
  }

  function addFromCatalog() {
    if (!pendingCode) return;
    const next = [
      ...ensureDraft(),
      {
        key: localKey(),
        stableCode: pendingCode,
        customName: "",
        capacity: "",
        capacityUnit: "",
        constraintNote: "",
      },
    ];
    setDraft(next);
    setPendingCode("");
  }

  function addCustom() {
    const trimmed = customName.trim();
    if (!trimmed) return;
    const next = [
      ...ensureDraft(),
      {
        key: localKey(),
        stableCode: createCustomStableCode(),
        customName: trimmed,
        capacity: "",
        capacityUnit: "",
        constraintNote: "",
      },
    ];
    setDraft(next);
    setCustomName("");
  }

  function updateItem(key: string, patch: Partial<DraftItem>) {
    setDraft(
      ensureDraft().map((item) =>
        item.key === key ? { ...item, ...patch } : item,
      ),
    );
  }

  function removeItem(key: string) {
    setDraft(ensureDraft().filter((item) => item.key !== key));
    setItemErrors((prev) => {
      if (!(key in prev)) return prev;
      const next = { ...prev };
      delete next[key];
      return next;
    });
    focusAfterRemoveRef.current = true;
  }

  function moveItem(key: string, direction: -1 | 1) {
    const current = ensureDraft();
    const index = current.findIndex((item) => item.key === key);
    const targetIndex = index + direction;
    if (index < 0 || targetIndex < 0 || targetIndex >= current.length) return;
    const next = [...current];
    [next[index], next[targetIndex]] = [next[targetIndex], next[index]];
    setDraft(next);
    const moved = current[index];
    const label = resolveLabel(locale, "equipment", moved.stableCode);
    setLiveMessage(
      t("profile.equipment.reorderAnnouncement")
        .replace("{name}", moved.customName || label)
        .replace("{position}", String(targetIndex + 1))
        .replace("{total}", String(next.length)),
    );
  }

  async function save() {
    setActionError(null);
    clearMutationError();
    setUnknownErrorCount(0);
    const current = ensureDraft();
    const { errors, hasErrors } = validateEquipmentDraft(current, t);
    if (hasErrors) {
      setItemErrors(errors);
      return;
    }
    setItemErrors({});
    submittedOrderRef.current = current.map((item) => item.key);
    try {
      await replaceEquipment(toEquipmentInputs(current));
      setDraft(null);
    } catch (err) {
      if (
        err instanceof ProfileApiError &&
        err.code === "validation_failed" &&
        err.fieldErrors
      ) {
        const knownIds = submittedOrderRef.current.flatMap((_, index) =>
          KNOWN_EQUIPMENT_ERROR_SUFFIXES.map(
            (suffix) => `entries[${index}].${suffix}`,
          ),
        );
        const { known, unknownCount } = splitKnownFieldErrors(
          err.fieldErrors,
          knownIds,
        );
        const mapped: Record<string, DraftItemErrors> = {};
        for (const [path, message] of Object.entries(known)) {
          const match = /^entries\[(\d+)\]\.(\w+)$/.exec(path);
          if (!match) continue;
          const index = Number(match[1]);
          const field = match[2] as keyof DraftItemErrors;
          const itemKey = submittedOrderRef.current[index];
          if (!itemKey) continue;
          mapped[itemKey] = { ...mapped[itemKey], [field]: message };
        }
        setItemErrors(mapped);
        setUnknownErrorCount(unknownCount);
        if (Object.keys(mapped).length === 0 && unknownCount === 0) {
          setActionError(describeProfileMutationError(err, t));
        }
        return;
      }
      setActionError(describeProfileMutationError(err, t));
    }
  }

  return (
    <div
      data-testid="profile-equipment"
      className="mx-auto max-w-2xl space-y-6"
    >
      <div
        aria-live="polite"
        className="sr-only"
        data-testid="profile-equipment-live-region"
      >
        {liveMessage}
      </div>
      <div className="flex items-center justify-between gap-2">
        <div>
          <h1 className="font-display text-3xl">
            {t("profile.equipment.title")}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.equipment.subtitle")}
          </p>
        </div>
        <Link
          to="/app/perfil"
          data-testid="profile-equipment-back"
          className={buttonVariants({ variant: "secondary" })}
        >
          {t("profile.actions.back")}
        </Link>
      </div>

      {blocked && (
        <div role="alert" data-testid="profile-equipment-version-conflict">
          <p>{t("profile.error.versionConflict")}</p>
          <Button type="button" onClick={() => void reload()}>
            {t("profile.actions.reload")}
          </Button>
        </div>
      )}

      {(actionError || lastMutationError) && (
        <p role="alert" data-testid="profile-equipment-error-message">
          {actionError ??
            (lastMutationError
              ? describeProfileMutationError(lastMutationError, t)
              : "")}
        </p>
      )}

      <FieldErrorSummary
        items={errorItems}
        unknownCount={unknownErrorCount}
        t={t}
        testId="profile-equipment-error-summary"
      />

      <ul className="space-y-2" data-testid="profile-equipment-entries">
        {items.length === 0 && (
          <li className="text-sm text-muted-foreground">
            {t("profile.equipment.empty")}
          </li>
        )}
        {items.map((item, index) => {
          const isCustom = isCustomStableCode(item.stableCode);
          const primaryLabel =
            isCustom && item.customName
              ? item.customName
              : resolveLabel(locale, "equipment", item.stableCode);
          const fieldErrors = itemErrors[item.key] ?? {};
          return (
            <li
              key={item.key}
              id={`profile-equipment-entry-${item.key}`}
              tabIndex={-1}
              data-testid={`profile-equipment-entry-${item.key}`}
              className="space-y-2 rounded-lg border border-border p-3"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-medium">{primaryLabel}</span>
                <div className="flex gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={!canMutate || index === 0}
                    data-testid={`profile-equipment-move-up-${item.key}`}
                    onClick={() => moveItem(item.key, -1)}
                  >
                    {t("profile.actions.moveUp")}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={!canMutate || index === items.length - 1}
                    data-testid={`profile-equipment-move-down-${item.key}`}
                    onClick={() => moveItem(item.key, 1)}
                  >
                    {t("profile.actions.moveDown")}
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={!canMutate}
                    data-testid={`profile-equipment-remove-${item.key}`}
                    onClick={() => removeItem(item.key)}
                  >
                    {t("profile.equipment.removeEntry")}
                  </Button>
                </div>
              </div>
              {isCustom && !item.customName && (
                <p className="text-xs text-muted-foreground">
                  {t("profile.equipment.unknownCode")}
                </p>
              )}
              {fieldErrors.duplicateCode && (
                <p role="alert" className="text-xs text-destructive">
                  {fieldErrors.duplicateCode}
                </p>
              )}
              {fieldErrors.stableCode && (
                <p role="alert" className="text-xs text-destructive">
                  {fieldErrors.stableCode}
                </p>
              )}
              <div className="flex flex-wrap items-center gap-2">
                {isCustom && (
                  <div className="space-y-1">
                    <label
                      htmlFor={`profile-equipment-customName-${item.key}`}
                      className="text-xs"
                    >
                      {t("profile.equipment.field.customName", {
                        name: primaryLabel,
                      })}
                    </label>
                    <Input
                      id={`profile-equipment-customName-${item.key}`}
                      data-testid={`profile-equipment-custom-name-${item.key}`}
                      className="max-w-xs"
                      maxLength={CUSTOM_NAME_MAX_LENGTH}
                      placeholder={t("profile.equipment.customNamePlaceholder")}
                      value={item.customName}
                      disabled={!canMutate}
                      aria-invalid={fieldErrors.customName ? true : undefined}
                      aria-describedby={
                        fieldErrors.customName
                          ? `profile-equipment-customName-error-${item.key}`
                          : undefined
                      }
                      onChange={(event) =>
                        updateItem(item.key, { customName: event.target.value })
                      }
                    />
                    {fieldErrors.customName && (
                      <p
                        role="alert"
                        id={`profile-equipment-customName-error-${item.key}`}
                        className="text-xs text-destructive"
                      >
                        {fieldErrors.customName}
                      </p>
                    )}
                  </div>
                )}
                <div className="space-y-1">
                  <label
                    htmlFor={`profile-equipment-capacity-${item.key}`}
                    className="block text-xs"
                  >
                    {t("profile.equipment.field.capacity", {
                      name: primaryLabel,
                    })}
                  </label>
                  <Input
                    id={`profile-equipment-capacity-${item.key}`}
                    data-testid={`profile-equipment-capacity-${item.key}`}
                    type="number"
                    min={0}
                    className="w-24"
                    value={item.capacity}
                    disabled={!canMutate}
                    aria-invalid={fieldErrors.capacity ? true : undefined}
                    aria-describedby={
                      fieldErrors.capacity
                        ? `profile-equipment-capacity-error-${item.key}`
                        : undefined
                    }
                    onChange={(event) =>
                      updateItem(item.key, { capacity: event.target.value })
                    }
                  />
                </div>
                <div className="space-y-1">
                  <label
                    htmlFor={`profile-equipment-capacityUnit-${item.key}`}
                    className="block text-xs"
                  >
                    {t("profile.equipment.field.capacityUnit", {
                      name: primaryLabel,
                    })}
                  </label>
                  <Input
                    id={`profile-equipment-capacityUnit-${item.key}`}
                    data-testid={`profile-equipment-capacity-unit-${item.key}`}
                    className="w-24"
                    maxLength={CAPACITY_UNIT_MAX_LENGTH}
                    value={item.capacityUnit}
                    disabled={!canMutate}
                    aria-invalid={fieldErrors.capacityUnit ? true : undefined}
                    aria-describedby={
                      fieldErrors.capacityUnit
                        ? `profile-equipment-capacityUnit-error-${item.key}`
                        : undefined
                    }
                    onChange={(event) =>
                      updateItem(item.key, {
                        capacityUnit: event.target.value,
                      })
                    }
                  />
                </div>
                <div className="space-y-1">
                  <label
                    htmlFor={`profile-equipment-constraintNote-${item.key}`}
                    className="block text-xs"
                  >
                    {t("profile.equipment.field.constraintNote", {
                      name: primaryLabel,
                    })}
                  </label>
                  <Input
                    id={`profile-equipment-constraintNote-${item.key}`}
                    data-testid={`profile-equipment-constraint-${item.key}`}
                    className="max-w-xs"
                    maxLength={CONSTRAINT_NOTE_MAX_LENGTH}
                    placeholder={t("profile.equipment.constraintNote")}
                    value={item.constraintNote}
                    disabled={!canMutate}
                    aria-invalid={fieldErrors.constraintNote ? true : undefined}
                    aria-describedby={
                      fieldErrors.constraintNote
                        ? `profile-equipment-constraintNote-error-${item.key}`
                        : undefined
                    }
                    onChange={(event) =>
                      updateItem(item.key, {
                        constraintNote: event.target.value,
                      })
                    }
                  />
                </div>
              </div>
              {(fieldErrors.capacity ||
                fieldErrors.capacityUnit ||
                fieldErrors.constraintNote) && (
                <div className="space-y-1 pl-1">
                  {fieldErrors.capacity && (
                    <p
                      role="alert"
                      id={`profile-equipment-capacity-error-${item.key}`}
                      className="text-xs text-destructive"
                    >
                      {fieldErrors.capacity}
                    </p>
                  )}
                  {fieldErrors.capacityUnit && (
                    <p
                      role="alert"
                      id={`profile-equipment-capacityUnit-error-${item.key}`}
                      className="text-xs text-destructive"
                    >
                      {fieldErrors.capacityUnit}
                    </p>
                  )}
                  {fieldErrors.constraintNote && (
                    <p
                      role="alert"
                      id={`profile-equipment-constraintNote-error-${item.key}`}
                      className="text-xs text-destructive"
                    >
                      {fieldErrors.constraintNote}
                    </p>
                  )}
                </div>
              )}
            </li>
          );
        })}
      </ul>

      <div
        className="flex flex-wrap items-center gap-2 rounded-xl border border-border p-4"
        data-testid="profile-equipment-add-catalog"
      >
        <div className="space-y-1">
          <label
            htmlFor="profile-equipment-catalog-select"
            className="block text-xs"
          >
            {t("profile.equipment.catalogSelect")}
          </label>
          <select
            id="profile-equipment-catalog-select"
            data-testid="profile-equipment-catalog-select"
            className="flex h-9 rounded-xl border border-input bg-background px-3 text-[15px]"
            value={pendingCode}
            disabled={!canMutate}
            onChange={(event) => setPendingCode(event.target.value)}
          >
            <option value="">{t("profile.equipment.addFromCatalog")}</option>
            {catalogOptions.map((code) => (
              <option key={code} value={code}>
                {resolveLabel(locale, "equipment", code)}
              </option>
            ))}
          </select>
        </div>
        <Button
          type="button"
          variant="secondary"
          disabled={!canMutate || !pendingCode}
          data-testid="profile-equipment-catalog-submit"
          onClick={addFromCatalog}
        >
          {t("profile.actions.add")}
        </Button>
        <div className="space-y-1">
          <label
            htmlFor="profile-equipment-custom-input"
            className="block text-xs"
          >
            {t("profile.equipment.newCustomName")}
          </label>
          <Input
            id="profile-equipment-custom-input"
            data-testid="profile-equipment-custom-input"
            className="max-w-xs"
            maxLength={CUSTOM_NAME_MAX_LENGTH}
            placeholder={t("profile.equipment.customNamePlaceholder")}
            value={customName}
            disabled={!canMutate}
            onChange={(event) => setCustomName(event.target.value)}
          />
        </div>
        <Button
          type="button"
          variant="secondary"
          disabled={!canMutate || !customName.trim()}
          data-testid="profile-equipment-custom-submit"
          onClick={addCustom}
        >
          {t("profile.equipment.addCustom")}
        </Button>
      </div>

      <div className="flex gap-2">
        <Button
          type="button"
          disabled={!canMutate || !isDirty}
          data-testid="profile-equipment-save"
          onClick={() => void save()}
        >
          {t("profile.equipment.saveOrder")}
        </Button>
        {isDirty && (
          <Button
            type="button"
            variant="secondary"
            disabled={!canMutate}
            data-testid="profile-equipment-cancel"
            onClick={() => {
              setDraft(null);
              setItemErrors({});
              setUnknownErrorCount(0);
            }}
          >
            {t("profile.actions.cancel")}
          </Button>
        )}
      </div>
    </div>
  );
}
