import { useEffect, useRef } from "react";
import { Link } from "react-router-dom";
import { ArrowRight, Leaf } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import type { ProductionLocale } from "@/app/i18n/productionCatalog";
import type { HomeTelemetry } from "@/contracts/contextualHome";
import { createNoOpHomeTelemetry } from "@/features/home/homeTelemetry";

/**
 * Resolves the reduced-motion preference.
 * Returns `null` when preference cannot be determined (missing/throwing matchMedia).
 * Unknown preference must use conservative non-smooth scrolling.
 */
function prefersReducedMotion(): boolean | null {
  if (typeof window.matchMedia !== "function") {
    return null;
  }
  try {
    return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  } catch {
    return null;
  }
}

function LocaleSwitcher() {
  const { locale, locales, setLocale, t } = useProductionI18n();
  return (
    <>
      <div
        className="hidden gap-1 sm:flex"
        role="group"
        aria-label={t("lang.label")}
      >
        {locales.map((code) => (
          <button
            key={code}
            type="button"
            data-testid={`landing-lang-${code}`}
            aria-pressed={locale === code}
            onClick={() => setLocale(code as ProductionLocale)}
            className={`rounded-full px-3 py-1 text-xs ${
              locale === code ? "bg-secondary" : "text-muted-foreground"
            }`}
          >
            {code === "pt-BR" ? "PT-BR" : code.toUpperCase()}
          </button>
        ))}
      </div>
      <label className="sm:hidden">
        <span className="sr-only">{t("lang.label")}</span>
        <select
          data-testid="production-lang-select"
          aria-label={t("lang.label")}
          className="max-w-[5.5rem] rounded-md border border-border bg-background px-2 py-1 text-xs"
          value={locale}
          onChange={(event) =>
            setLocale(event.target.value as ProductionLocale)
          }
        >
          {locales.map((code) => (
            <option key={code} value={code}>
              {code === "pt-BR" ? "PT-BR" : code.toUpperCase()}
            </option>
          ))}
        </select>
      </label>
    </>
  );
}

/**
 * Signed-out public entry.
 * Must not call SessionProvider / getSession or render authenticated personal data.
 * CTA always targets `/acesso` — do not inspect session to change the destination.
 * Signed-in visitors never reach this page: the `/` route redirects them to the home.
 */
export function PublicEntryPage({
  telemetry = createNoOpHomeTelemetry(),
}: {
  telemetry?: HomeTelemetry;
}) {
  const { t } = useProductionI18n();
  const demoRef = useRef<HTMLElement | null>(null);
  const tracked = useRef(false);

  useEffect(() => {
    if (tracked.current) return;
    tracked.current = true;
    telemetry.track({ name: "public_entry_viewed" });
  }, [telemetry]);

  const loginHref = "/acesso";

  function scrollToDemo() {
    const preference = prefersReducedMotion();
    demoRef.current?.scrollIntoView({
      behavior: preference === false ? "smooth" : "auto",
    });
  }

  return (
    <div
      data-testid="production-landing"
      className="min-h-screen bg-background text-foreground"
    >
      <header className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3 px-4 py-6 sm:px-6 md:px-10">
        <div className="flex items-center gap-2">
          <span
            className="grid h-10 w-10 place-items-center rounded-xl bg-primary text-primary-foreground font-display text-lg"
            aria-hidden
          >
            C
          </span>
          <span className="font-display text-2xl">{t("brand.name")}</span>
        </div>
        <div className="flex items-center gap-2">
          <LocaleSwitcher />
          <Button asChild size="sm" className="rounded-full">
            <Link
              to={loginHref}
              data-testid="landing-enter"
              onClick={() =>
                telemetry.track({
                  name: "login_cta_selected",
                  codes: { surface: "header" },
                })
              }
            >
              {t("entry.cta.login")}{" "}
              <ArrowRight
                className="ml-1 hidden h-4 w-4 sm:inline"
                aria-hidden
              />
            </Link>
          </Button>
        </div>
      </header>

      <main>
        <section
          aria-labelledby="entry-hero-title"
          className="mx-auto max-w-6xl px-6 pb-12 pt-4 md:px-10 md:pb-16 md:pt-8"
        >
          <p className="mb-3 inline-flex items-center gap-2 text-sm text-muted-foreground">
            <Leaf className="h-4 w-4 text-primary" aria-hidden />
            {t("entry.hero.eyebrow")}
          </p>
          <h1
            id="entry-hero-title"
            data-testid="production-landing-title"
            className="font-display text-4xl leading-tight sm:text-5xl lg:text-6xl"
          >
            {t("entry.hero.title")}
          </h1>
          <p
            data-testid="production-landing-tagline"
            className="mt-5 max-w-2xl text-base text-muted-foreground md:text-lg"
          >
            {t("entry.hero.lead")}
          </p>
          <p
            data-testid="production-landing-subtitle"
            className="mt-3 max-w-2xl text-sm text-muted-foreground"
          >
            {t("entry.hero.notOnlyRecipes")}
          </p>
          <div className="mt-8 flex flex-wrap gap-3">
            <Button asChild size="lg" className="rounded-full px-6">
              <Link
                to={loginHref}
                data-testid="hero-enter"
                onClick={() =>
                  telemetry.track({
                    name: "login_cta_selected",
                    codes: { surface: "hero" },
                  })
                }
              >
                {t("entry.cta.login")}
              </Link>
            </Button>
            <Button
              type="button"
              size="lg"
              variant="secondary"
              className="rounded-full px-6"
              data-testid="entry-cta-demo"
              onClick={scrollToDemo}
            >
              {t("entry.cta.secondary")}
            </Button>
          </div>
        </section>

        <section
          ref={demoRef}
          id="entry-demo"
          data-testid="entry-demo"
          aria-labelledby="entry-demo-title"
          className="border-y border-border bg-secondary/30"
        >
          <div className="mx-auto max-w-6xl px-6 py-14 md:px-10">
            <h2
              id="entry-demo-title"
              className="font-display text-3xl md:text-4xl"
            >
              {t("entry.demo.title")}
            </h2>
            <ol className="mt-8 grid gap-4 md:grid-cols-2 lg:grid-cols-3">
              {(
                [
                  "entry.demo.step1",
                  "entry.demo.step2",
                  "entry.demo.step3",
                  "entry.demo.step4",
                  "entry.demo.step5",
                ] as const
              ).map((key, index) => (
                <li
                  key={key}
                  data-testid={`entry-demo-step-${index}`}
                  className="rounded-2xl border border-border bg-card p-5"
                >
                  <span className="text-xs font-medium text-muted-foreground">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  <p className="mt-2 text-sm md:text-base">{t(key)}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section
          aria-labelledby="entry-outcomes-title"
          className="mx-auto max-w-6xl px-6 py-14 md:px-10"
        >
          <h2
            id="entry-outcomes-title"
            className="font-display text-3xl md:text-4xl"
          >
            {t("entry.outcomes.title")}
          </h2>
          <ul className="mt-6 grid gap-3 md:grid-cols-2">
            {(
              [
                "entry.outcomes.inventory",
                "entry.outcomes.decide",
                "entry.outcomes.attention",
                "entry.outcomes.plan",
                "entry.outcomes.adapt",
              ] as const
            ).map((key) => (
              <li
                key={key}
                className="rounded-xl border border-border bg-card px-4 py-3 text-sm md:text-base"
              >
                {t(key)}
              </li>
            ))}
          </ul>
        </section>

        <section
          aria-labelledby="entry-why-title"
          className="mx-auto max-w-6xl px-6 pb-14 md:px-10"
        >
          <h2
            id="entry-why-title"
            className="font-display text-3xl md:text-4xl"
          >
            {t("entry.why.title")}
          </h2>
          <p className="mt-4 max-w-3xl text-muted-foreground">
            {t("entry.why.account")}
          </p>
        </section>
      </main>

      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-col gap-3 px-6 py-10 text-xs text-muted-foreground md:px-10">
          <p className="flex flex-wrap gap-4">
            {/* Inert policy placeholders until legal destinations are accepted. */}
            <a
              href="#terms-placeholder"
              data-testid="entry-legal-terms"
              aria-disabled="true"
              onClick={(event) => event.preventDefault()}
              className="underline-offset-2 hover:underline"
            >
              {t("entry.legal.terms")}
            </a>
            <a
              href="#privacy-placeholder"
              data-testid="entry-legal-privacy"
              aria-disabled="true"
              onClick={(event) => event.preventDefault()}
              className="underline-offset-2 hover:underline"
            >
              {t("entry.legal.privacy")}
            </a>
          </p>
          <p>{t("entry.legal.placeholderNote")}</p>
        </div>
      </footer>
    </div>
  );
}
