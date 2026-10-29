import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type { ProductionLocale } from "@/app/i18n/productionCatalog";

/** Interface language buttons; used in the account menu and, signed out, in the header. */
export function LanguageChoice() {
  const { locale, locales, setLocale, t } = useProductionI18n();
  return (
    <div role="group" aria-label={t("lang.label")} className="flex gap-1">
      {locales.map((code) => (
        <button
          key={code}
          type="button"
          data-testid={`production-lang-${code}`}
          aria-pressed={locale === code}
          onClick={() => setLocale(code as ProductionLocale)}
          className={`h-9 rounded-full px-3 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
            locale === code
              ? "bg-primary text-primary-foreground"
              : "bg-secondary text-secondary-foreground"
          }`}
        >
          {code === "pt-BR" ? "PT" : code.toUpperCase()}
        </button>
      ))}
    </div>
  );
}

/**
 * Avatar button opening the account menu: profile and household, interface language, and sign
 * out. Closes on Escape, on a click outside, and after navigating. Sign out goes through
 * `onLogout`, which asks before discarding unsaved profile edits.
 */
export function AccountMenu({
  displayName,
  onLogout,
}: {
  displayName: string | null;
  onLogout: () => void;
}) {
  const { t } = useProductionI18n();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const initial = (displayName?.trim()[0] ?? "?").toUpperCase();

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", onPointer);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onPointer);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        data-testid="production-account-menu"
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={t("account.menu")}
        onClick={() => setOpen((value) => !value)}
        className="flex h-10 w-10 items-center justify-center rounded-full bg-secondary text-base font-semibold text-secondary-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        {initial}
      </button>
      {open ? (
        <div
          data-testid="production-account-panel"
          className="absolute right-0 top-12 z-50 w-64 space-y-4 rounded-2xl border border-border bg-popover p-4 text-popover-foreground shadow-lg"
        >
          {displayName ? (
            <p className="truncate text-sm font-semibold">{displayName}</p>
          ) : null}
          <Link
            to="/app/perfil"
            data-testid="production-nav-perfil"
            onClick={() => setOpen(false)}
            className="flex h-11 items-center rounded-xl px-3 text-[15px] font-medium hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {t("nav.profile")}
          </Link>
          <div className="space-y-2 px-1">
            <p className="text-xs font-medium text-muted-foreground">
              {t("lang.label")}
            </p>
            <LanguageChoice />
          </div>
          <button
            type="button"
            data-testid="production-logout"
            onClick={() => {
              setOpen(false);
              onLogout();
            }}
            className="flex h-11 w-full items-center rounded-xl px-3 text-left text-[15px] font-medium hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            {t("access.logout")}
          </button>
        </div>
      ) : null}
    </div>
  );
}
