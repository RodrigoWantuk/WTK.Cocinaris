import { useState } from "react";
import { Share, X } from "lucide-react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { isIosSafari, isStandalone, useInstallPrompt } from "./installPrompt";

/** Per-device memory of "not now"; the offer comes back after this many days. */
const DISMISS_KEY = "cocinaris.install.dismissedAt";
const DISMISS_DAYS = 30;

function dismissedRecently(): boolean {
  try {
    const value = Number(window.localStorage.getItem(DISMISS_KEY));
    return (
      Number.isFinite(value) &&
      value > 0 &&
      Date.now() - value < DISMISS_DAYS * 86_400_000
    );
  } catch {
    return false;
  }
}

function onPhone(): boolean {
  return (
    typeof window.matchMedia === "function" &&
    window.matchMedia("(pointer: coarse)").matches
  );
}

/**
 * "Instale o Cocinaris" (PLAN-0059): on a phone, offers to install the site as an app. On
 * Android and other Chromium browsers it opens the browser's install dialog; on iPhone and iPad
 * it explains the Share → "Adicionar à Tela de Início" steps, because Safari has no install
 * event. Hidden when the app already runs installed, on devices without touch, and for 30 days
 * after "Agora não". Announces nothing and takes no focus: it is an offer, not an interruption.
 */
export function InstallAppBanner() {
  const { t } = useProductionI18n();
  const install = useInstallPrompt();
  const [hidden, setHidden] = useState(() => dismissedRecently());
  const ios = isIosSafari();

  if (hidden || install.installed || isStandalone() || !onPhone()) return null;
  if (!install.canPrompt && !ios) return null;

  const dismiss = () => {
    try {
      window.localStorage.setItem(DISMISS_KEY, String(Date.now()));
    } catch {
      // Blocked storage only means the offer may come back sooner.
    }
    setHidden(true);
  };

  return (
    <section
      aria-labelledby="install-app-title"
      data-testid="install-app-banner"
      className="surface relative flex gap-3 p-4"
    >
      <img
        src="/icons/icon-192.png"
        alt=""
        width={48}
        height={48}
        className="h-12 w-12 shrink-0 rounded-xl"
      />
      <div className="min-w-0 flex-1 space-y-2 pr-6">
        <h2 id="install-app-title" className="font-semibold">
          {t("install.title")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("install.detail")}</p>
        {install.canPrompt ? (
          <div className="flex flex-wrap gap-2 pt-1">
            <Button
              size="sm"
              data-testid="install-app-action"
              onClick={() => {
                void install.prompt().then((outcome) => {
                  if (outcome !== "accepted") dismiss();
                });
              }}
            >
              {t("install.action")}
            </Button>
            <Button size="sm" variant="ghost" onClick={dismiss}>
              {t("install.later")}
            </Button>
          </div>
        ) : (
          <p
            data-testid="install-app-ios"
            className="flex flex-wrap items-center gap-1 rounded-xl bg-accent px-3 py-2 text-sm text-accent-foreground"
          >
            {t("install.iosBefore")}
            <Share aria-label={t("install.iosShare")} className="h-4 w-4" />
            {t("install.iosAfter")}
          </p>
        )}
      </div>
      <button
        type="button"
        onClick={dismiss}
        aria-label={t("install.close")}
        className="absolute right-2 top-2 flex h-9 w-9 items-center justify-center rounded-full text-muted-foreground transition-colors duration-150 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <X aria-hidden="true" className="h-4 w-4" />
      </button>
    </section>
  );
}
