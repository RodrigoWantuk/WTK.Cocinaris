import { useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { Button } from "@/components/ui/button";
import { createLivePrivacyRepository } from "@/adapters/live/livePrivacyRepository";
import { RecipeApiError } from "@/contracts/recipes";
import type { PrivacyRepository } from "@/contracts/privacy";
import { AccountDeletionSection } from "./AccountDeletionSection";

const KEPT = [
  "account",
  "profile",
  "restrictions",
  "pantry",
  "cooking",
  "typed",
  "usage",
  "notifications",
] as const;
const PROCESSORS = [
  "host",
  "cdn",
  "deepseek",
  "openrouter",
  "login",
  "push",
] as const;
const CORRECT = [
  ["data", "/app/perfil/dados"],
  ["preferences", "/app/perfil/preferencias"],
  ["equipment", "/app/perfil/equipamentos"],
  ["pantry", "/app/despensa"],
  ["recipes", "/app/receitas"],
  ["menus", "/app/cardapio"],
] as const;

type DownloadState = "idle" | "working" | "done" | "failed" | "limited";

/**
 * "Seus dados" (PLAN-0049 Slice 1): what Cocinaris keeps and who else receives a part of it, the
 * export download, where each part is corrected, and account deletion (Slice 2).
 * The download is a ZIP saved through an object URL, so a failure can be said on the page instead of
 * opening an error file.
 *
 * @param repository Replaces the live repository in tests.
 */
export function ProfilePrivacyPage({
  repository,
}: {
  repository?: PrivacyRepository;
}) {
  const { t, locale } = useProductionI18n();
  const repo = useMemo(
    () => repository ?? createLivePrivacyRepository(),
    [repository],
  );
  const [contactEmail, setContactEmail] = useState<string | null>(null);
  const [download, setDownload] = useState<DownloadState>("idle");
  const running = useRef<AbortController | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    repo.getCenter(controller.signal).then(
      (center) =>
        !controller.signal.aborted && setContactEmail(center.contactEmail),
      // Without the settings the page still works; only the support line is missing.
      () => undefined,
    );
    return () => {
      controller.abort();
      running.current?.abort();
    };
  }, [repo]);

  const startDownload = async () => {
    if (download === "working") return;
    const controller = new AbortController();
    running.current = controller;
    setDownload("working");
    try {
      const file = await repo.downloadExport(locale, controller.signal);
      const url = URL.createObjectURL(file.blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = file.fileName;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // The browser has started saving; the URL is no longer needed.
      setTimeout(() => URL.revokeObjectURL(url), 0);
      setDownload("done");
    } catch (err) {
      if (controller.signal.aborted) return;
      setDownload(
        err instanceof RecipeApiError && err.status === 429
          ? "limited"
          : "failed",
      );
    }
  };

  return (
    <div className="space-y-8" data-testid="profile-privacy">
      <header className="space-y-2">
        <h1 className="font-display text-3xl">{t("profile.privacy.title")}</h1>
        <p className="text-muted-foreground">{t("profile.privacy.intro")}</p>
      </header>

      <section aria-labelledby="privacy-kept" className="space-y-2">
        <h2 id="privacy-kept" className="text-xl font-semibold">
          {t("profile.privacy.kept.title")}
        </h2>
        <ul className="list-disc space-y-1 pl-5">
          {KEPT.map((key) => (
            <li key={key} data-testid={`privacy-kept-${key}`}>
              {t(`profile.privacy.kept.${key}`)}
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="privacy-processors" className="space-y-2">
        <h2 id="privacy-processors" className="text-xl font-semibold">
          {t("profile.privacy.processors.title")}
        </h2>
        <ul className="list-disc space-y-1 pl-5">
          {PROCESSORS.map((key) => (
            <li key={key}>{t(`profile.privacy.processors.${key}`)}</li>
          ))}
        </ul>
      </section>

      <section
        aria-labelledby="privacy-download"
        className="surface space-y-3 p-4"
      >
        <h2 id="privacy-download" className="text-xl font-semibold">
          {t("profile.privacy.download.title")}
        </h2>
        <p className="text-sm text-muted-foreground">
          {t("profile.privacy.download.detail")}
        </p>
        <Button
          type="button"
          data-testid="privacy-download"
          disabled={download === "working"}
          onClick={() => void startDownload()}
        >
          {download === "working"
            ? t("profile.privacy.download.working")
            : t("profile.privacy.download.action")}
        </Button>
        <p
          role="status"
          aria-live="polite"
          className="text-sm"
          data-testid="privacy-download-status"
        >
          {download === "done" ? t("profile.privacy.download.done") : null}
          {download === "failed" ? (
            <span className="text-destructive">
              {t("profile.privacy.download.failed")}
            </span>
          ) : null}
          {download === "limited" ? (
            <span className="text-destructive">
              {t("profile.privacy.download.limited")}
            </span>
          ) : null}
        </p>
      </section>

      <section aria-labelledby="privacy-correct" className="space-y-2">
        <h2 id="privacy-correct" className="text-xl font-semibold">
          {t("profile.privacy.correct.title")}
        </h2>
        <nav
          aria-labelledby="privacy-correct"
          className="surface divide-y divide-border overflow-hidden"
        >
          {CORRECT.map(([key, to]) => (
            <Link
              key={key}
              to={to}
              data-testid={`privacy-correct-${key}`}
              className="flex min-h-11 items-center p-4 transition-colors duration-150 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              {t(`profile.privacy.correct.${key}`)}
            </Link>
          ))}
        </nav>
      </section>

      <section aria-labelledby="privacy-notifications" className="space-y-2">
        <h2 id="privacy-notifications" className="text-xl font-semibold">
          {t("profile.privacy.notifications.title")}
        </h2>
        <p>
          {t("profile.privacy.notifications.detail")}{" "}
          <Link
            to="/app/perfil"
            className="font-semibold text-primary underline"
          >
            {t("profile.privacy.notifications.link")}
          </Link>
        </p>
      </section>

      <AccountDeletionSection repository={repo} />

      {contactEmail ? (
        <p className="text-sm" data-testid="privacy-contact">
          {t("profile.privacy.contact")}{" "}
          <a
            href={`mailto:${contactEmail}`}
            className="font-semibold text-primary underline"
          >
            {contactEmail}
          </a>
        </p>
      ) : null}
    </div>
  );
}
