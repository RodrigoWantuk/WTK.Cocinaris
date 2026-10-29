import {
  isValidControlledValue,
  controlledFieldGroup,
  CONTROLLED_FIELDS,
  type ControlledFieldName,
  type ProgressiveProfileField,
} from "@/contracts/profile";
import { Button } from "@/components/ui/button";
import type { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import {
  UNCHANGED_FIELD_MODE,
  type FieldMode,
} from "./ProgressiveFieldControl";

type Translate = ReturnType<typeof useProductionI18n>["t"];

function resolveSelectValue(
  field: ProgressiveProfileField<string>,
  mode: FieldMode,
): string {
  if (mode.kind === "confirm") return mode.value;
  if (mode.kind === "remove") return "";
  if (field.value != null) return field.value;
  if (field.presence === "default" && field.defaultValue != null) {
    return field.defaultValue;
  }
  return "";
}

function resolveStatusLabel(
  field: ProgressiveProfileField<string>,
  mode: FieldMode,
  t: Translate,
): string {
  if (mode.kind === "confirm") return t("profile.data.pendingConfirm");
  if (mode.kind === "remove") return t("profile.data.pendingRemoval");
  return t(`profile.data.presence.${field.presence}`);
}

/**
 * Editor for one closed-set ("controlled") profile field — see
 * `@/contracts/profile/controlledCodes`. Renders a `select` populated only with
 * that field's exact backend wire values, each shown with a localized label,
 * instead of a free-text input. This keeps typos and unsupported wire values
 * from ever being typed in the first place; {@link mapControlledStringField}
 * (`src/adapters/live/profile/mapProfile.ts`) still fails closed defensively on
 * the read side for values this frontend build does not yet know about.
 *
 * If the field's current server value is not one of the known options (for
 * example a legacy value, or a member added on the backend before this build
 * knows about it), it is still offered as a selectable, humanized "unknown"
 * option so an existing value is never silently discarded by only opening this
 * control — the user can still explicitly change or remove it.
 */
export function ControlledFieldControl({
  id,
  fieldName,
  label,
  field,
  mode,
  onModeChange,
  disabled = false,
  errorMessage,
  t,
  testIdPrefix,
}: {
  id: string;
  /** Which closed-set field this is (drives the allowed options and their labels). */
  fieldName: ControlledFieldName;
  label: string;
  field: ProgressiveProfileField<string>;
  mode: FieldMode;
  onModeChange: (mode: FieldMode) => void;
  disabled?: boolean;
  /** Field-level validation message (local or mapped from a backend `fieldErrors` path); wired to `aria-invalid`/`aria-describedby`. */
  errorMessage?: string | null;
  t: Translate;
  testIdPrefix: string;
}) {
  const group = controlledFieldGroup(fieldName);
  const codes: readonly string[] = CONTROLLED_FIELDS[fieldName].codes;
  const selectValue = resolveSelectValue(field, mode);
  const statusLabel = resolveStatusLabel(field, mode, t);
  const showUseDefault =
    field.presence === "default" &&
    mode.kind === "unchanged" &&
    field.defaultValue != null;
  const hasUnknownCurrentValue =
    selectValue !== "" && !isValidControlledValue(fieldName, selectValue);

  return (
    <div
      className="space-y-2 rounded-lg border border-border p-3"
      data-testid={`${testIdPrefix}-field-${id}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label
          htmlFor={`${testIdPrefix}-input-${id}`}
          className="text-sm font-medium"
        >
          {label}
        </label>
        <span
          className="text-xs text-muted-foreground"
          data-testid={`${testIdPrefix}-status-${id}`}
        >
          {statusLabel}
        </span>
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <select
          id={`${testIdPrefix}-input-${id}`}
          data-testid={`${testIdPrefix}-input-${id}`}
          className="flex h-9 rounded-xl border border-input bg-background px-3 text-[15px]"
          value={selectValue}
          disabled={disabled || mode.kind === "remove"}
          aria-invalid={errorMessage ? true : undefined}
          aria-describedby={
            errorMessage ? `${testIdPrefix}-error-${id}` : undefined
          }
          onChange={(event) =>
            onModeChange({ kind: "confirm", value: event.target.value })
          }
        >
          <option value="">{t("profile.data.selectPlaceholder")}</option>
          {hasUnknownCurrentValue && (
            <option value={selectValue}>{selectValue}</option>
          )}
          {codes.map((code) => (
            <option key={code} value={code}>
              {t(`profile.data.option.${group}.${code}`)}
            </option>
          ))}
        </select>
        {showUseDefault && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={disabled}
            data-testid={`${testIdPrefix}-use-default-${id}`}
            onClick={() =>
              onModeChange({
                kind: "confirm",
                value: String(field.defaultValue),
              })
            }
          >
            {t("profile.actions.useDefault")}
          </Button>
        )}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={disabled || mode.kind === "remove"}
          data-testid={`${testIdPrefix}-remove-${id}`}
          onClick={() => onModeChange({ kind: "remove" })}
        >
          {t("profile.actions.remove")}
        </Button>
        {mode.kind !== "unchanged" && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={disabled}
            data-testid={`${testIdPrefix}-undo-${id}`}
            onClick={() => onModeChange(UNCHANGED_FIELD_MODE)}
          >
            {t("profile.actions.cancel")}
          </Button>
        )}
      </div>
      {errorMessage && (
        <p
          role="alert"
          id={`${testIdPrefix}-error-${id}`}
          data-testid={`${testIdPrefix}-error-${id}`}
          className="text-xs text-destructive"
        >
          {errorMessage}
        </p>
      )}
    </div>
  );
}
