import { useState } from "react";
import {
  resolveLabel,
  type ProfileCatalogKind,
  type ProfileCatalogLocale,
} from "@/features/profile/catalog/profileCatalog";
import { Button } from "@/components/ui/button";
import type { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";

type Translate = ReturnType<typeof useProductionI18n>["t"];

/**
 * Editable ordered list of curated catalog stable codes (known techniques,
 * techniques to learn, goals, or abandonment reasons).
 *
 * These backend fields are plain `string[]` with no attached free-text note, unlike
 * preference/restriction entries. Minting an opaque custom stable code here would
 * therefore have no way to carry a human-readable label, so this editor intentionally
 * offers catalog selection only; custom entries with a note belong on the
 * preferences page instead.
 */
export function CodeListEditor({
  id,
  label,
  catalogKind,
  catalogCodes,
  value,
  onChange,
  locale,
  disabled = false,
  errorMessage,
  t,
}: {
  id: string;
  label: string;
  catalogKind: ProfileCatalogKind;
  catalogCodes: readonly string[];
  value: string[];
  onChange: (next: string[]) => void;
  locale: ProfileCatalogLocale;
  disabled?: boolean;
  /** Backend `fieldErrors` message mapped to this list field, if any. */
  errorMessage?: string | null;
  t: Translate;
}) {
  const [pendingCode, setPendingCode] = useState("");

  const available = catalogCodes.filter((code) => !value.includes(code));

  function addCode(code: string) {
    if (!code || value.includes(code)) return;
    onChange([...value, code]);
  }

  function removeCode(code: string) {
    onChange(value.filter((existing) => existing !== code));
  }

  return (
    <div
      className="space-y-2 rounded-lg border border-border p-3"
      data-testid={`code-list-${id}`}
    >
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-medium">{label}</span>
      </div>
      <p className="text-xs text-muted-foreground">
        {t("profile.data.customListHint")}
      </p>
      <ul className="flex flex-wrap gap-2">
        {value.map((code) => (
          <li key={code}>
            <span
              className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-sm"
              data-testid={`code-list-${id}-item-${code}`}
            >
              {resolveLabel(locale, catalogKind, code)}
              <button
                type="button"
                aria-label={t("profile.data.removeListItem")}
                data-testid={`code-list-${id}-remove-${code}`}
                disabled={disabled}
                onClick={() => removeCode(code)}
                className="text-muted-foreground hover:text-foreground"
              >
                ×
              </button>
            </span>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap items-center gap-2">
        <select
          data-testid={`code-list-${id}-select`}
          aria-label={`${label}: ${t("profile.data.addListItem")}`}
          aria-describedby={errorMessage ? `code-list-${id}-error` : undefined}
          // Long catalog labels must not widen the page on narrow screens.
          className="flex h-9 w-full min-w-0 max-w-full rounded-xl border border-input bg-background px-3 text-[15px] sm:w-auto"
          value={pendingCode}
          disabled={disabled}
          onChange={(event) => setPendingCode(event.target.value)}
        >
          <option value="">{t("profile.data.addListItem")}</option>
          {available.map((code) => (
            <option key={code} value={code}>
              {resolveLabel(locale, catalogKind, code)}
            </option>
          ))}
        </select>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={disabled || !pendingCode}
          data-testid={`code-list-${id}-add-catalog`}
          onClick={() => {
            addCode(pendingCode);
            setPendingCode("");
          }}
        >
          {t("profile.actions.add")}
        </Button>
      </div>
      {errorMessage && (
        <p
          role="alert"
          id={`code-list-${id}-error`}
          data-testid={`code-list-${id}-error`}
          className="text-xs text-destructive"
        >
          {errorMessage}
        </p>
      )}
    </div>
  );
}
