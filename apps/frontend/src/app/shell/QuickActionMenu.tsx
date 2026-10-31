import { useEffect, useId, useRef, useState } from "react";
import { ChefHat, Refrigerator, Sparkles, X } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";

/** The two things the main button starts (PLAN-0059, owner idea 2026-10-31). */
const ACTIONS = [
  {
    to: "/app/despensa/novo",
    labelKey: "quick.addPantry",
    shortKey: "quick.addPantryShort",
    icon: Refrigerator,
    testId: "quick-action-pantry",
  },
  {
    to: "/app/cozinhar",
    labelKey: "quick.cook",
    shortKey: "quick.cook",
    icon: ChefHat,
    testId: "quick-action-cook",
  },
] as const;

/**
 * The central main button and its half-moon menu (PLAN-0059). Tapping the accent button opens two
 * round actions on an arc just above it, "Adicionar à despensa" and "Cozinhar", over a dimmed
 * page; from `md` up, the same choices open as a small panel under the header button.
 *
 * Disclosure pattern: the trigger has `aria-expanded` and `aria-controls`; opening moves focus to
 * the first action; Escape, a tap outside, or a navigation closes it and focus returns to the
 * trigger. The arc animation runs only when motion is allowed.
 *
 * @param active Whether the current page is one of the two destinations (lights the button).
 */
export function QuickActionMenu({ active }: { active: boolean }) {
  const { t } = useProductionI18n();
  const { pathname } = useLocation();
  const [open, setOpen] = useState(false);
  const menuId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const firstRef = useRef<HTMLAnchorElement>(null);

  // Any navigation closes the menu.
  useEffect(() => setOpen(false), [pathname]);

  useEffect(() => {
    if (!open) return;
    firstRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open]);

  return (
    <div className="relative">
      {open ? (
        // The dimmed page behind the menu; tapping it closes the menu.
        <button
          type="button"
          tabIndex={-1}
          aria-hidden="true"
          data-testid="quick-action-backdrop"
          className="fixed inset-0 z-40 cursor-default bg-foreground/15 motion-safe:animate-in motion-safe:fade-in md:bg-transparent"
          onClick={() => setOpen(false)}
        />
      ) : null}
      <button
        ref={triggerRef}
        type="button"
        data-testid="production-nav-add"
        aria-label={t("nav.add")}
        aria-expanded={open}
        aria-controls={menuId}
        aria-current={active ? "page" : undefined}
        onClick={() => setOpen((value) => !value)}
        className="relative z-50 flex h-12 w-12 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-md transition-[background-color,transform] duration-150 hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 motion-safe:active:scale-95 md:ml-2 md:h-10 md:w-auto md:gap-1.5 md:px-4 md:text-sm md:font-semibold md:shadow-none"
      >
        {open ? (
          <X
            aria-hidden="true"
            className="h-6 w-6 motion-safe:animate-in motion-safe:spin-in-90 md:hidden"
          />
        ) : null}
        <Sparkles
          aria-hidden="true"
          className={`h-6 w-6 md:inline md:h-4 md:w-4 ${open ? "hidden" : ""}`}
        />
        <span className="hidden md:inline">{t("nav.add")}</span>
      </button>
      {open ? (
        <div id={menuId}>
          {/* Phones: a small, light half moon floating just above the button, with the two
              actions resting on its curve. */}
          <ul
            aria-label={t("nav.add")}
            className="absolute bottom-full left-1/2 z-50 mb-4 h-[110px] w-[220px] -translate-x-1/2 rounded-t-full border border-border/60 bg-popover/95 shadow-[0_-6px_24px_-10px_rgb(0_0_0/0.2)] backdrop-blur-sm motion-safe:animate-in motion-safe:fade-in motion-safe:zoom-in-90 motion-safe:slide-in-from-bottom-3 md:hidden"
          >
            {ACTIONS.map(
              ({ to, labelKey, shortKey, icon: Icon, testId }, index) => (
                <li
                  key={to}
                  className={`absolute top-[30px] ${index === 0 ? "left-[24px]" : "right-[24px]"}`}
                >
                  <Link
                    ref={index === 0 ? firstRef : undefined}
                    to={to}
                    data-testid={testId}
                    aria-label={t(labelKey)}
                    onClick={() => setOpen(false)}
                    className="group flex w-[72px] flex-col items-center gap-1 rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    <span className="flex h-11 w-11 items-center justify-center rounded-full bg-accent text-accent-foreground ring-1 ring-primary/15 transition-transform duration-150 group-hover:scale-105 motion-safe:group-active:scale-95">
                      <Icon aria-hidden="true" className="h-5 w-5" />
                    </span>
                    <span
                      aria-hidden="true"
                      className="text-[11px] font-semibold leading-none text-foreground"
                    >
                      {t(shortKey)}
                    </span>
                  </Link>
                </li>
              ),
            )}
          </ul>
          {/* From `md` up: a small panel under the header button. */}
          <ul
            aria-label={t("nav.add")}
            className="absolute right-0 top-full z-50 mt-2 hidden w-60 flex-col gap-1 rounded-2xl border border-border bg-popover p-2 shadow-lg md:flex"
          >
            {ACTIONS.map(({ to, labelKey, icon: Icon }) => (
              <li key={to}>
                <Link
                  to={to}
                  onClick={() => setOpen(false)}
                  className="flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-semibold transition-colors duration-150 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <span className="flex h-9 w-9 items-center justify-center rounded-full bg-accent text-accent-foreground">
                    <Icon aria-hidden="true" className="h-5 w-5" />
                  </span>
                  {t(labelKey)}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
