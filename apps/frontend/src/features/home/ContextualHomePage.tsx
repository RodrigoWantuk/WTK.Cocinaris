import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { useRuntime } from "@/app/runtime/RuntimeProvider";
import {
  HOME_SOURCE_TIERS,
  type HomeQuickChooserDefinition,
  type HomeSourceResult,
  type HomeSourceTier,
  type HomeSuggestionCandidate,
} from "@/contracts/contextualHome";
import { Button } from "@/components/ui/button";
import { buildHomeGreeting, readBrowserTimeZone } from "./dayPart";
import { useContextualHome } from "./ContextualHomeProvider";
import { QuickChooser } from "./QuickChooser";
import { renderHomeText } from "./renderHomeText";
import { normalizeHomeQuickChooserDefinition } from "./validateHomeQuickChooserDefinition";

function CandidateCard({
  item,
  t,
}: {
  item: HomeSuggestionCandidate;
  t: (key: string, vars?: Readonly<Record<string, string | number>>) => string;
}) {
  return (
    <article
      data-testid={`home-candidate-${item.id}`}
      data-source-tier={item.sourceTier}
      data-freshness={item.freshness ?? "current"}
      data-readiness={item.readinessCode ?? ""}
      data-shopping={item.shoppingState ?? "unknown"}
      className="rounded-2xl border border-border bg-card p-4"
    >
      <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span className="rounded-full bg-secondary px-2 py-1">
          {t(item.sourceLabelKey)}
        </span>
        {item.freshness === "stale" ? (
          <span className="rounded-full border border-warning px-2 py-1">
            {t("home.source.staleBadge")}
          </span>
        ) : null}
        {item.attentionInfluenced ? (
          <span className="rounded-full border border-border px-2 py-1">
            {t("home.attention.advisory")}
          </span>
        ) : null}
        {item.readinessCode ? (
          <span data-testid={`home-readiness-${item.id}`}>
            {t(`home.readiness.${item.readinessCode}`)}
          </span>
        ) : null}
      </div>
      <h3 className="mt-3 font-display text-xl">
        {renderHomeText(item.title, t)}
      </h3>
      <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
        {item.reasonCodes.map((code) => (
          <li key={code}>{t(`home.reason.${code}`)}</li>
        ))}
      </ul>
      <div className="mt-3 flex flex-wrap gap-3 text-xs text-muted-foreground">
        {item.timing?.activeMinutes != null ? (
          <span data-testid={`home-active-min-${item.id}`}>
            {t("home.minutes.active", { count: item.timing.activeMinutes })}
          </span>
        ) : null}
        {item.timing?.totalMinutes != null ? (
          <span data-testid={`home-total-min-${item.id}`}>
            {t("home.minutes.total", { count: item.timing.totalMinutes })}
          </span>
        ) : null}
        {item.effortCode ? (
          <span>{t(`home.effort.${item.effortCode}`)}</span>
        ) : null}
        {item.cleanupCode ? (
          <span>{t(`home.cleanup.${item.cleanupCode}`)}</span>
        ) : null}
        {item.shoppingState && item.shoppingState !== "not_required" ? (
          <span>{t(`home.shopping.${item.shoppingState}`)}</span>
        ) : null}
      </div>
      {item.missingRequirements && item.missingRequirements.length > 0 ? (
        <div className="mt-3" data-testid={`home-missing-${item.id}`}>
          <p className="text-xs font-medium">{t("home.missing.title")}</p>
          <ul className="mt-1 list-disc pl-5 text-xs text-muted-foreground">
            {item.missingRequirements.map((req) => (
              <li key={req.code}>
                {renderHomeText(req.label, t)} (
                {t(`home.requirement.kind.${req.kind}`)})
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {item.availableRequirements && item.availableRequirements.length > 0 ? (
        <div className="mt-2" data-testid={`home-available-${item.id}`}>
          <p className="text-xs font-medium">{t("home.available.title")}</p>
          <ul className="mt-1 list-disc pl-5 text-xs text-muted-foreground">
            {item.availableRequirements.map((req) => (
              <li key={req.code}>{renderHomeText(req.label, t)}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {item.preparationRequirements &&
      item.preparationRequirements.length > 0 ? (
        <div className="mt-2" data-testid={`home-prep-${item.id}`}>
          <p className="text-xs font-medium">{t("home.prep.title")}</p>
          <ul className="mt-1 list-disc pl-5 text-xs text-muted-foreground">
            {item.preparationRequirements.map((prep) => (
              <li key={prep.code}>
                {renderHomeText(prep.label, t)}
                {prep.leadTimeHours != null
                  ? ` — ${t("home.prep.leadHours", { count: prep.leadTimeHours })}`
                  : null}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
      {item.uncertaintyCodes && item.uncertaintyCodes.length > 0 ? (
        <ul
          className="mt-2 space-y-1 text-xs text-muted-foreground"
          data-testid={`home-uncertainty-${item.id}`}
        >
          {item.uncertaintyCodes.map((code) => (
            <li key={code}>{t(`home.uncertainty.${code}`)}</li>
          ))}
        </ul>
      ) : null}
      {item.conflictCodes && item.conflictCodes.length > 0 ? (
        <ul className="mt-2 space-y-1 text-xs text-muted-foreground">
          {item.conflictCodes.map((code) => (
            <li key={code}>{t(`home.conflict.${code}`)}</li>
          ))}
        </ul>
      ) : null}
    </article>
  );
}

function SourceSection({
  result,
  t,
  onRetry,
}: {
  result: HomeSourceResult;
  t: (key: string, vars?: Readonly<Record<string, string | number>>) => string;
  onRetry?: () => void;
}) {
  return (
    <section
      data-testid={`home-source-${result.tier}`}
      data-status={result.status}
      data-retryable={result.retryable ? "true" : "false"}
      aria-labelledby={`home-source-heading-${result.tier}`}
      className="space-y-3"
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2
          id={`home-source-heading-${result.tier}`}
          className="font-display text-2xl"
        >
          {t(`home.source.heading.${result.tier}`)}
        </h2>
        <span className="text-xs text-muted-foreground">
          {t(`home.source.label.${result.tier}`)}
        </span>
      </div>
      {result.status === "ready" || result.status === "stale" ? (
        <div className="grid gap-3">
          {result.status === "stale" && result.statusReasonKey ? (
            <p role="status" className="text-sm text-muted-foreground">
              {t(result.statusReasonKey)}
            </p>
          ) : null}
          {result.items.map((item) => (
            <CandidateCard key={item.id} item={item} t={t} />
          ))}
          {result.retryable && onRetry ? (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid={`home-source-retry-${result.tier}`}
              onClick={onRetry}
            >
              {t("home.source.retry")}
            </Button>
          ) : null}
        </div>
      ) : (
        <div className="space-y-2">
          <p role="status" className="text-sm text-muted-foreground">
            {result.statusReasonKey
              ? t(result.statusReasonKey)
              : t("home.source.unavailable")}
          </p>
          {result.retryable && onRetry ? (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid={`home-source-retry-${result.tier}`}
              onClick={onRetry}
            >
              {t("home.source.retry")}
            </Button>
          ) : null}
        </div>
      )}
    </section>
  );
}

function shouldRenderSource(result: HomeSourceResult): boolean {
  if (result.tier === "menu" && result.status === "empty") {
    return false;
  }
  return true;
}

function failedResult(
  tier: HomeSourceTier,
  reasonKey: string,
): HomeSourceResult {
  return {
    tier,
    status: "failed",
    retryable: true,
    statusReasonKey: reasonKey,
    items: [],
  };
}

/**
 * Authenticated contextual home.
 * Sources load independently with generation tokens so stale responses cannot
 * overwrite newer context. Mock fixtures stay out of production composition.
 */
export function ContextualHomePage({
  now,
  browserTimeZone,
  scenarioOptions,
}: {
  now?: Date;
  browserTimeZone?: string | null;
  /** Prototype-only scenario ids; omit in production. */
  scenarioOptions?: readonly string[];
} = {}) {
  const { t, locale } = useProductionI18n();
  const { session } = useSession();
  const { enablePrototypeFixtures } = useRuntime();
  const { adapter, telemetry, scenarioId, setScenarioId } = useContextualHome();

  const [menu, setMenu] = useState<HomeSourceResult | null>(null);
  const [inventory, setInventory] = useState<HomeSourceResult | null>(null);
  const [profile, setProfile] = useState<HomeSourceResult | null>(null);
  const [chooserDef, setChooserDef] =
    useState<HomeQuickChooserDefinition | null>(null);
  const [chooserLoading, setChooserLoading] = useState(true);
  const [chooserResult, setChooserResult] = useState<HomeSourceResult | null>(
    null,
  );
  const [chooserOpen, setChooserOpen] = useState(false);
  const [timeZoneOverride, setTimeZoneOverride] = useState<string | null>(null);
  const chooserOpenerRef = useRef<HTMLButtonElement | null>(null);

  const generationRef = useRef(0);
  const mountedRef = useRef(true);
  const abortRef = useRef<AbortController | null>(null);

  const closeChooser = useCallback(() => {
    setChooserOpen(false);
    // Restore focus after Radix portal teardown (controlled unmount path).
    queueMicrotask(() => {
      chooserOpenerRef.current?.focus();
    });
  }, []);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      abortRef.current?.abort();
    };
  }, []);

  const clock = useMemo(() => now ?? new Date(), [now]);
  const resolvedBrowserTimeZone =
    browserTimeZone === undefined ? readBrowserTimeZone() : browserTimeZone;

  const greeting = useMemo(
    () =>
      buildHomeGreeting({
        displayName: session.displayName,
        overrideTimeZone: timeZoneOverride,
        profileTimeZone: session.timeZone,
        browserTimeZone: resolvedBrowserTimeZone,
        now: clock,
      }),
    [
      session.displayName,
      session.timeZone,
      resolvedBrowserTimeZone,
      clock,
      timeZoneOverride,
    ],
  );

  const greetingText = greeting.displayName
    ? t(`home.greeting.named.${greeting.dayPart}`, {
        name: greeting.displayName,
      })
    : t(`home.greeting.anonymous.${greeting.dayPart}`);

  const bumpGeneration = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;
    const generation = ++generationRef.current;
    setChooserResult(null);
    setChooserOpen(false);
    return { generation, signal: controller.signal };
  }, []);

  const isCurrent = useCallback(
    (generation: number) =>
      mountedRef.current && generation === generationRef.current,
    [],
  );

  const loadMenu = useCallback(
    async (generation: number, signal: AbortSignal) => {
      setMenu(null);
      try {
        const result = await adapter.loadMenuSource({
          locale,
          timeZone: greeting.timeZone,
          now: clock,
          signal,
        });
        if (!isCurrent(generation)) return;
        setMenu(result);
        telemetry.track({
          name:
            result.status === "unavailable"
              ? "source_unavailable"
              : "source_rendered",
          codes: { tier: "menu", status: result.status },
        });
      } catch (err) {
        if (signal.aborted || !isCurrent(generation)) return;
        if (err instanceof DOMException && err.name === "AbortError") return;
        setMenu(failedResult("menu", "home.source.failed.menu"));
      }
    },
    [adapter, locale, greeting.timeZone, clock, telemetry, isCurrent],
  );

  const loadInventory = useCallback(
    async (generation: number, signal: AbortSignal) => {
      setInventory(null);
      try {
        const result = await adapter.loadInventorySource({
          locale,
          timeZone: greeting.timeZone,
          now: clock,
          signal,
        });
        if (!isCurrent(generation)) return;
        setInventory(result);
        telemetry.track({
          name:
            result.status === "unavailable"
              ? "source_unavailable"
              : "source_rendered",
          codes: { tier: "inventory", status: result.status },
        });
      } catch (err) {
        if (signal.aborted || !isCurrent(generation)) return;
        if (err instanceof DOMException && err.name === "AbortError") return;
        setInventory(failedResult("inventory", "home.source.failed.inventory"));
      }
    },
    [adapter, locale, greeting.timeZone, clock, telemetry, isCurrent],
  );

  const loadProfile = useCallback(
    async (generation: number, signal: AbortSignal) => {
      setProfile(null);
      try {
        const result = await adapter.loadProfileSource({
          locale,
          timeZone: greeting.timeZone,
          now: clock,
          signal,
        });
        if (!isCurrent(generation)) return;
        setProfile(result);
        telemetry.track({
          name:
            result.status === "unavailable"
              ? "source_unavailable"
              : "source_rendered",
          codes: { tier: "profile", status: result.status },
        });
      } catch (err) {
        if (signal.aborted || !isCurrent(generation)) return;
        if (err instanceof DOMException && err.name === "AbortError") return;
        setProfile(failedResult("profile", "home.source.failed.profile"));
      }
    },
    [adapter, locale, greeting.timeZone, clock, telemetry, isCurrent],
  );

  const loadChooserDefinition = useCallback(
    async (generation: number, signal: AbortSignal) => {
      setChooserLoading(true);
      try {
        const chooser = await adapter.getQuickChooserDefinition({
          locale,
          timeZone: greeting.timeZone,
          now: clock,
          signal,
        });
        if (!isCurrent(generation)) return;
        setChooserDef(normalizeHomeQuickChooserDefinition(chooser));
      } catch (err) {
        if (signal.aborted || !isCurrent(generation)) return;
        if (err instanceof DOMException && err.name === "AbortError") return;
        setChooserDef({
          capabilityStatus: "temporarily_unavailable",
          retryable: true,
          statusReasonKey: "home.chooser.definitionFailed",
          questions: [],
        });
      } finally {
        if (isCurrent(generation)) setChooserLoading(false);
      }
    },
    [adapter, locale, greeting.timeZone, clock, isCurrent],
  );

  const reloadAll = useCallback(() => {
    const { generation, signal } = bumpGeneration();
    void loadMenu(generation, signal);
    void loadInventory(generation, signal);
    void loadProfile(generation, signal);
    void loadChooserDefinition(generation, signal);
  }, [
    bumpGeneration,
    loadMenu,
    loadInventory,
    loadProfile,
    loadChooserDefinition,
  ]);

  useEffect(() => {
    reloadAll();
  }, [reloadAll, scenarioId, adapter]);

  const retryTier = useCallback(
    (tier: HomeSourceTier) => {
      const generation = generationRef.current;
      const signal = abortRef.current?.signal ?? new AbortController().signal;
      if (tier === "menu") void loadMenu(generation, signal);
      if (tier === "inventory") void loadInventory(generation, signal);
      if (tier === "profile") void loadProfile(generation, signal);
    },
    [loadMenu, loadInventory, loadProfile],
  );

  const orderedSources = useMemo(() => {
    const map: Record<string, HomeSourceResult | null> = {
      menu,
      inventory,
      profile,
    };
    return HOME_SOURCE_TIERS.filter((tier) => tier !== "quickChooser").map(
      (tier) => map[tier],
    );
  }, [menu, inventory, profile]);

  // Every personalized source reports a permanent capability gap (production until the
  // live sources exist). Three identical "unavailable" sections would read as an error,
  // so the page shows one honest notice that points at what feeds future suggestions.
  const allSourcesUnavailable =
    menu?.status === "unavailable" &&
    inventory?.status === "unavailable" &&
    profile?.status === "unavailable";
  const hasAnyCandidate = orderedSources.some(
    (s) =>
      s && (s.status === "ready" || s.status === "stale") && s.items.length,
  );

  const canUseBrowserOverride =
    Boolean(resolvedBrowserTimeZone) &&
    greeting.timeZoneSource !== "browser" &&
    greeting.timeZoneSource !== "override";

  return (
    <div data-testid="contextual-home" className="space-y-8">
      <header className="space-y-3">
        <p
          data-testid="home-greeting"
          className="text-sm text-muted-foreground"
        >
          {greetingText}
        </p>
        <h1
          data-testid="home-primary-question"
          className="font-display text-3xl md:text-4xl"
        >
          {t("home.primaryQuestion")}
        </h1>
        {greeting.timeZoneSource === "unavailable" ? (
          <p role="status" data-testid="home-timezone-fallback">
            {t("home.timezone.missing")}
          </p>
        ) : null}
        {greeting.timeZoneSource === "invalid" ? (
          <p role="status" data-testid="home-timezone-invalid">
            {t("home.timezone.invalid")}
          </p>
        ) : null}
        {greeting.timeZone ? (
          <p
            className="text-xs text-muted-foreground"
            data-testid="home-timezone"
          >
            {t(`home.timezone.source.${greeting.timeZoneSource}`)}:{" "}
            {greeting.timeZone}
          </p>
        ) : null}
        <div className="flex flex-wrap gap-2">
          {canUseBrowserOverride ? (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="home-timezone-use-browser"
              onClick={() =>
                setTimeZoneOverride(resolvedBrowserTimeZone ?? null)
              }
            >
              {t("home.timezone.useBrowser")}
            </Button>
          ) : null}
          {timeZoneOverride ? (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              data-testid="home-timezone-clear-override"
              onClick={() => setTimeZoneOverride(null)}
            >
              {t("home.timezone.clearOverride")}
            </Button>
          ) : null}
          <Button asChild variant="secondary" size="sm">
            <Link to="/app/despensa" data-testid="home-nav-pantry">
              {t("home.nav.pantry")}
            </Link>
          </Button>
          <Button
            ref={chooserOpenerRef}
            type="button"
            size="sm"
            data-testid="home-open-chooser"
            disabled={chooserLoading || !chooserDef}
            onClick={() => {
              telemetry.track({ name: "quick_chooser_started" });
              setChooserOpen(true);
            }}
          >
            {t("home.chooser.open")}
          </Button>
        </div>
      </header>

      {enablePrototypeFixtures && setScenarioId && scenarioOptions ? (
        <label className="block text-sm">
          <span className="text-muted-foreground">
            {t("home.scenario.label")}
          </span>
          <select
            className="mt-1 block w-full max-w-sm rounded-md border border-border bg-background px-3 py-2"
            data-testid="home-scenario-select"
            value={scenarioId ?? "default"}
            onChange={(event) => setScenarioId(event.target.value)}
          >
            {scenarioOptions.map((id) => (
              <option key={id} value={id}>
                {id}
              </option>
            ))}
          </select>
        </label>
      ) : null}

      <div data-testid="home-sources" className="flex flex-col gap-10">
        {allSourcesUnavailable ? (
          <section
            data-testid="home-live-unavailable"
            aria-labelledby="home-live-unavailable-heading"
            className="space-y-3 rounded-2xl border border-border bg-secondary/40 p-6"
          >
            <h2
              id="home-live-unavailable-heading"
              className="font-display text-2xl"
            >
              {t("home.unavailable.title")}
            </h2>
            <p className="text-sm text-muted-foreground">
              {t("home.unavailable.detail")}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button asChild size="sm">
                <Link
                  to="/app/despensa"
                  data-testid="home-unavailable-pantry-link"
                >
                  {t("home.unavailable.pantryAction")}
                </Link>
              </Button>
              <Button asChild size="sm" variant="secondary">
                <Link
                  to="/app/perfil"
                  data-testid="home-unavailable-profile-link"
                >
                  {t("home.unavailable.profileAction")}
                </Link>
              </Button>
            </div>
          </section>
        ) : null}
        {orderedSources.map((result, index) => {
          if (allSourcesUnavailable) return null;
          const tier = HOME_SOURCE_TIERS[index];
          if (!result) {
            return (
              <p
                key={tier}
                role="status"
                data-testid={`home-source-loading-${tier}`}
                className="text-sm text-muted-foreground"
              >
                {t("home.loading")}
              </p>
            );
          }
          if (!shouldRenderSource(result)) {
            return null;
          }
          return (
            <SourceSection
              key={result.tier}
              result={result}
              t={t}
              onRetry={
                result.retryable ? () => retryTier(result.tier) : undefined
              }
            />
          );
        })}

        {!allSourcesUnavailable || chooserResult ? (
          <section
            data-testid="home-source-quickChooser"
            aria-labelledby="home-source-heading-quickChooser"
            className="space-y-3"
          >
            <h2
              id="home-source-heading-quickChooser"
              className="font-display text-2xl"
            >
              {t("home.source.heading.quickChooser")}
            </h2>
            <p className="text-sm text-muted-foreground">
              {t("home.source.label.quickChooser")}
            </p>
            {chooserResult ? (
              chooserResult.status === "ready" &&
              chooserResult.items.length > 0 ? (
                <div className="grid gap-3" data-testid="chooser-results">
                  <p className="text-sm font-medium">
                    {t("home.chooser.results")}
                  </p>
                  {chooserResult.items.map((item) => (
                    <CandidateCard key={item.id} item={item} t={t} />
                  ))}
                </div>
              ) : (
                <p role="status">
                  {chooserResult.statusReasonKey
                    ? t(chooserResult.statusReasonKey)
                    : t("home.chooser.empty")}
                </p>
              )
            ) : null}
          </section>
        ) : null}

        {!allSourcesUnavailable &&
        !hasAnyCandidate &&
        !chooserResult &&
        menu &&
        inventory &&
        profile ? (
          <p role="status" data-testid="home-no-suggestions">
            {t("home.noSuggestions")}
          </p>
        ) : null}
      </div>

      {chooserOpen && chooserDef ? (
        <QuickChooser
          definition={chooserDef}
          telemetry={telemetry}
          onCancel={closeChooser}
          onRetry={() => {
            closeChooser();
            const generation = generationRef.current;
            const signal =
              abortRef.current?.signal ?? new AbortController().signal;
            void loadChooserDefinition(generation, signal).then(() => {
              if (isCurrent(generation)) setChooserOpen(true);
            });
          }}
          onLoadSuggestions={async (answers, signal) =>
            adapter.loadQuickChooserSuggestions({
              locale,
              timeZone: greeting.timeZone,
              now: clock,
              quickChooserAnswers: answers,
              signal,
            })
          }
          onComplete={(result) => {
            setChooserResult(result);
            closeChooser();
          }}
        />
      ) : null}
    </div>
  );
}
