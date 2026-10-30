import { UserRound } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type { ProductionLocale } from "@/app/i18n/productionCatalog";
import { useThemePreference, type ThemePreference } from "@/app/theme/theme";

const THEMES: ThemePreference[] = ["system", "light", "dark"];

/** Theme buttons (system, light, dark) for the account menu; the choice is kept per device. */
export function ThemeChoice() {
  const { t } = useProductionI18n();
  const [preference, setPreference] = useThemePreference();
  return (
    <div role="group" aria-label={t("theme.label")} className="flex gap-1">
      {THEMES.map((option) => (
        <button
          key={option}
          type="button"
          data-testid={`production-theme-${option}`}
          aria-pressed={preference === option}
          onClick={() => setPreference(option)}
          className={`h-9 rounded-lg px-3 text-sm font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
            preference === option
              ? "bg-primary text-primary-foreground"
              : "bg-secondary text-secondary-foreground hover:bg-secondary/70"
          }`}
        >
          {t(`theme.${option}`)}
        </button>
      ))}
    </div>
  );
}

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
          className={`h-9 rounded-lg px-3 text-sm font-semibold transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
            locale === code
              ? "bg-primary text-primary-foreground"
              : "bg-secondary text-secondary-foreground hover:bg-secondary/70"
          }`}
        >
          {code === "pt-BR" ? "PT" : code.toUpperCase()}
        </button>
      ))}
    </div>
  );
}

/**
 * Avatar button opening the account menu: profile, interface language, theme, and sign out.
 * Without a display name the avatar is a person icon, never "?" (which reads as help). Closes on Escape, on a click outside, and after navigating. Sign out goes through
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
  const initial = displayName?.trim()[0]?.toUpperCase() ?? null;

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
        className="flex h-10 w-10 items-center justify-center rounded-full border border-border bg-card text-base font-semibold text-foreground transition-colors duration-150 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
      >
        {initial ?? <UserRound aria-hidden="true" className="h-5 w-5" />}
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
          <div className="space-y-2 px-1">
            <p className="text-xs font-medium text-muted-foreground">
              {t("theme.label")}
            </p>
            <ThemeChoice />
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
