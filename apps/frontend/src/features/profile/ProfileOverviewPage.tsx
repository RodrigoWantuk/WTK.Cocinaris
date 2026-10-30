import { useState } from "react";
import { Link } from "react-router-dom";
import { useProfileWorkspace } from "./ProfileProvider";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { describeProfileMutationError } from "./describeProfileMutationError";
import { Button } from "@/components/ui/button";

/** Backend `AdultDeclarationState` values this page renders distinct copy for. */
type AdultStateKind = "declared" | "declined" | "notDeclared" | "unknown";

/**
 * Classifies the backend `AdultDeclarationState` wire string. Anything other than
 * the three known members is reported as `"unknown"` rather than silently folded
 * into `"notDeclared"` — a future or unrecognized state must never be presented to
 * the user as if no declaration had ever been made.
 */
function classifyAdultState(state: string): AdultStateKind {
  if (state === "Declared") return "declared";
  if (state === "Declined") return "declined";
  if (state === "NotDeclared") return "notDeclared";
  return "unknown";
}

interface NextStep {
  sectionKey: string;
  labelKey:
    | "profile.overview.nextSteps.household"
    | "profile.overview.nextSteps.preferences"
    | "profile.overview.nextSteps.equipment";
  linkTo: string;
  testId: string;
}

/** Sections `completeness.sectionCounts` reports that this page offers as progressive next steps. */
const NEXT_STEP_SECTIONS: readonly NextStep[] = [
  {
    sectionKey: "household",
    labelKey: "profile.overview.nextSteps.household",
    linkTo: "/app/perfil/dados",
    testId: "profile-overview-next-step-household",
  },
  {
    sectionKey: "preferences",
    labelKey: "profile.overview.nextSteps.preferences",
    linkTo: "/app/perfil/preferencias",
    testId: "profile-overview-next-step-preferences",
  },
  {
    sectionKey: "equipment",
    labelKey: "profile.overview.nextSteps.equipment",
    linkTo: "/app/perfil/equipamentos",
    testId: "profile-overview-next-step-equipment",
  },
];

/**
 * Read-only landing surface for the profile area: completeness, existence, adult
 * declaration status, progressive next steps, and links into the three editable
 * sections. The only writes it performs are the explicit adult declaration accept/
 * decline actions, gated on `adultPolicy.available`.
 */
export function ProfileOverviewPage() {
  const {
    status,
    workspace,
    error,
    reload,
    adultPolicy,
    patchProfile,
    canMutate,
    lastMutationError,
    clearMutationError,
  } = useProfileWorkspace();
  const { t } = useProductionI18n();
  const [declarationError, setDeclarationError] = useState<string | null>(null);

  if (status === "session") {
    return (
      <div
        role="alert"
        data-testid="profile-overview-session"
        className="mx-auto max-w-xl space-y-3"
      >
        <h1 className="font-display text-3xl">{t("profile.overview.title")}</h1>
        <p>{t("profile.error.session")}</p>
      </div>
    );
  }

  if ((status === "loading" || status === "idle") && !workspace) {
    return (
      <p role="status" data-testid="profile-overview-loading">
        {t("profile.loading")}
      </p>
    );
  }

  if (status === "error" && !workspace) {
    return (
      <div
        role="alert"
        data-testid="profile-overview-error"
        className="mx-auto max-w-xl space-y-3"
      >
        <h1 className="font-display text-3xl">{t("profile.overview.title")}</h1>
        <p>{error || t("profile.error.load")}</p>
        <Button
          type="button"
          data-testid="profile-overview-retry"
          onClick={() => void reload()}
        >
          {t("profile.actions.retry")}
        </Button>
      </div>
    );
  }

  if (!workspace) {
    return null;
  }

  const { profile, completeness } = workspace;
  const adultKind = classifyAdultState(profile.adultDeclaration.state);
  const canDeclare = adultKind !== "declared" && adultPolicy.available;

  function describeDeclarationError(err: unknown): string {
    return describeProfileMutationError(err, t);
  }

  async function submitDeclaration(adultDeclared: boolean) {
    if (!adultPolicy.available || !adultPolicy.termsVersion) return;
    setDeclarationError(null);
    clearMutationError();
    try {
      await patchProfile({
        adultDeclaration: {
          adultDeclared,
          termsVersion: adultPolicy.termsVersion,
          privacyVersion: adultPolicy.privacyVersion ?? null,
        },
      });
    } catch (err) {
      setDeclarationError(describeDeclarationError(err));
    }
  }

  const nextSteps = NEXT_STEP_SECTIONS.filter(
    (step) => (completeness.sectionCounts[step.sectionKey] ?? 0) === 0,
  );

  return (
    <div data-testid="profile-overview" className="mx-auto max-w-2xl space-y-6">
      <div>
        <h1 className="font-display text-3xl">{t("profile.overview.title")}</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {t("profile.overview.subtitle")}
        </p>
      </div>

      {status === "version_conflict" && (
        <div
          role="alert"
          data-testid="profile-overview-version-conflict"
          className="space-y-2 rounded-lg border border-warning p-4"
        >
          <p>{t("profile.error.versionConflict")}</p>
          <p className="text-sm text-muted-foreground">
            {t("profile.error.versionConflictDetail")}
          </p>
          <Button
            type="button"
            data-testid="profile-overview-reload"
            onClick={() => void reload()}
          >
            {t("profile.actions.reload")}
          </Button>
        </div>
      )}

      {/* One status block instead of three repeating ones (review M5): whether the profile was
          started, how complete it is, and a bar; each section row below says what is missing. */}
      <div className="surface space-y-3 p-4">
        {!profile.profileExists ? (
          <p data-testid="profile-overview-not-started">
            <span className="font-medium">
              {t("profile.overview.notStarted")}
            </span>{" "}
            <span className="text-sm text-muted-foreground">
              {t("profile.overview.notStartedDetail")}
            </span>
          </p>
        ) : null}
        <div data-testid="profile-overview-completeness">
          <p className="flex items-baseline justify-between gap-2 text-sm">
            <span className="font-semibold">
              {t("profile.overview.completeness").replace(
                "{percent}",
                String(completeness.percentComplete),
              )}
            </span>
            <span className="text-muted-foreground">
              {t("profile.overview.completenessSummary")
                .replace("{completed}", String(completeness.completedSections))
                .replace("{total}", String(completeness.totalSections))}
            </span>
          </p>
          <div
            className="mt-2 h-2 overflow-hidden rounded-full bg-secondary"
            aria-hidden="true"
          >
            <div
              className="h-full rounded-full bg-primary transition-[width] duration-200"
              style={{ width: `${completeness.percentComplete}%` }}
            />
          </div>
        </div>
      </div>

      {/* Owner decision 2026-10-28: the adult confirmation is asked once and then disappears;
          the age requirement itself lives in the terms of use. Without published terms there is
          nothing to confirm, so the card stays hidden instead of becoming a permanent notice. */}
      {adultKind !== "declared" && adultPolicy.available && (
        <div
          data-testid="profile-overview-adult-declaration"
          className="rounded-xl border border-border p-4"
        >
          <h2 className="font-medium">
            {t("profile.overview.adultDeclaration.title")}
          </h2>
          <p
            className="mt-1 text-sm text-muted-foreground"
            data-testid="profile-overview-adult-state"
          >
            {adultKind === "declined" &&
              t("profile.overview.adultDeclaration.declined")}
            {adultKind === "notDeclared" &&
              t("profile.overview.adultDeclaration.notDeclared")}
            {adultKind === "unknown" &&
              t("profile.overview.adultDeclaration.unknown")}
          </p>
          {(declarationError || lastMutationError) && (
            <p
              role="alert"
              className="mt-1 text-xs text-destructive"
              data-testid="profile-overview-adult-error"
            >
              {declarationError ??
                (lastMutationError
                  ? describeDeclarationError(lastMutationError)
                  : "")}
            </p>
          )}
          {canDeclare && (
            <div className="mt-2 flex gap-2">
              <Button
                type="button"
                size="sm"
                disabled={!canMutate}
                data-testid="profile-overview-adult-accept"
                onClick={() => void submitDeclaration(true)}
              >
                {t("profile.actions.confirm")}
              </Button>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                disabled={!canMutate}
                data-testid="profile-overview-adult-decline"
                onClick={() => void submitDeclaration(false)}
              >
                {t("profile.actions.remove")}
              </Button>
            </div>
          )}
        </div>
      )}

      <nav
        aria-label={t("profile.overview.title")}
        className="surface divide-y divide-border overflow-hidden"
      >
        {(
          [
            [
              "household",
              "/app/perfil/dados",
              "profile-overview-link-data",
              "profile.overview.section.household",
              "profile.overview.linkData",
            ],
            [
              "preferences",
              "/app/perfil/preferencias",
              "profile-overview-link-preferences",
              "profile.overview.section.preferences",
              "profile.overview.linkPreferences",
            ],
            [
              "equipment",
              "/app/perfil/equipamentos",
              "profile-overview-link-equipment",
              "profile.overview.section.equipment",
              "profile.overview.linkEquipment",
            ],
          ] as const
        ).map(([section, to, testId, title, detail]) => {
          const step = nextSteps.find((item) => item.sectionKey === section);
          return (
            <Link
              key={section}
              to={to}
              data-testid={testId}
              className="flex items-center justify-between gap-3 p-4 transition-colors duration-150 hover:bg-secondary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              <span>
                <span className="block font-medium">{t(title)}</span>
                <span className="block text-sm text-muted-foreground">
                  {t(detail)}
                </span>
              </span>
              {step ? (
                <span
                  data-testid={step.testId}
                  className="shrink-0 rounded-full bg-secondary px-2.5 py-0.5 text-xs font-semibold text-secondary-foreground"
                >
                  {t("profile.overview.notFilled")}
                </span>
              ) : null}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}
