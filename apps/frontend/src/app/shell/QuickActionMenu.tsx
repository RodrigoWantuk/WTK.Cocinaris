import { useEffect, useId, useRef, useState } from "react";
import { ChefHat, Refrigerator, Sparkles } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";

/** The two things the main button starts (PLAN-0059, owner idea 2026-10-31). */
const ACTIONS = [
  {
    to: "/app/despensa/novo",
    labelKey: "quick.addPantry",
    icon: Refrigerator,
    testId: "quick-action-pantry",
  },
  {
    to: "/app/cozinhar",
    labelKey: "quick.cook",
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
          className="fixed inset-0 z-40 cursor-default bg-foreground/30 motion-safe:animate-in motion-safe:fade-in md:bg-transparent"
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
        className={`relative z-50 flex h-12 w-12 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-md transition-[background-color,transform] duration-150 hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 motion-safe:active:scale-95 md:ml-2 md:h-10 md:w-auto md:gap-1.5 md:px-4 md:text-sm md:font-semibold md:shadow-none ${
          open ? "motion-safe:rotate-12 md:rotate-0" : ""
        }`}
      >
        <Sparkles aria-hidden="true" className="h-6 w-6 md:h-4 md:w-4" />
        <span className="hidden md:inline">{t("nav.add")}</span>
      </button>
      {open ? (
        <div
          id={menuId}
          className="absolute bottom-full left-1/2 z-50 mb-2 -translate-x-1/2 md:bottom-auto md:left-auto md:right-0 md:top-full md:mb-0 md:mt-2 md:translate-x-0"
        >
          {/* Phones: a half moon rising from the button, with the two actions on its arc. From
              `md` up the same list is a small panel under the header button. */}
          <ul
            aria-label={t("nav.add")}
            className="relative flex h-36 w-72 items-start justify-between rounded-t-full border border-b-0 border-border bg-popover px-6 pt-7 shadow-[0_-8px_30px_-12px_rgb(0_0_0/0.25)] motion-safe:animate-in motion-safe:fade-in motion-safe:zoom-in-95 motion-safe:slide-in-from-bottom-6 md:h-auto md:w-60 md:flex-col md:items-stretch md:gap-1 md:rounded-2xl md:border-b md:p-2 md:shadow-lg"
          >
            {ACTIONS.map(({ to, labelKey, icon: Icon, testId }, index) => (
              <li
                key={to}
                // On the arc: each action sits lower toward the rim, framing the button below.
                className={index === 0 ? "mt-6 md:mt-0" : "mt-6 md:mt-0"}
              >
                <Link
                  ref={index === 0 ? firstRef : undefined}
                  to={to}
                  data-testid={testId}
                  onClick={() => setOpen(false)}
                  className="group flex w-24 flex-col items-center gap-1.5 rounded-2xl p-1 text-center focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring md:w-auto md:flex-row md:gap-3 md:px-3 md:py-2.5 md:text-left md:hover:bg-secondary"
                >
                  <span className="flex h-14 w-14 items-center justify-center rounded-full bg-accent text-accent-foreground transition-transform duration-150 group-hover:scale-105 motion-safe:group-active:scale-95 md:h-9 md:w-9">
                    <Icon
                      aria-hidden="true"
                      className="h-6 w-6 md:h-5 md:w-5"
                    />
                  </span>
                  <span className="text-xs font-semibold leading-tight text-foreground md:text-sm">
                    {t(labelKey)}
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}
