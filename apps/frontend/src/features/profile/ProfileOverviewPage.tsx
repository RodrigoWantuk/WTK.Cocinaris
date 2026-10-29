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

      {!profile.profileExists && (
        <div
          data-testid="profile-overview-not-started"
          className="rounded-xl border border-border p-4"
        >
          <p className="font-medium">{t("profile.overview.notStarted")}</p>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.overview.notStartedDetail")}
          </p>
        </div>
      )}

      <div
        data-testid="profile-overview-completeness"
        className="rounded-xl border border-border p-4"
      >
        <p className="text-2xl font-display">
          {t("profile.overview.completeness").replace(
            "{percent}",
            String(completeness.percentComplete),
          )}
        </p>
        <p className="text-sm text-muted-foreground">
          {t("profile.overview.completenessSummary")
            .replace("{completed}", String(completeness.completedSections))
            .replace("{total}", String(completeness.totalSections))}
        </p>
      </div>

      <div
        data-testid="profile-overview-next-steps"
        className="rounded-xl border border-border p-4"
      >
        <h2 className="font-medium">{t("profile.overview.nextSteps")}</h2>
        {nextSteps.length === 0 ? (
          <p
            className="mt-1 text-sm text-muted-foreground"
            data-testid="profile-overview-next-steps-done"
          >
            {t("profile.overview.nextSteps.done")}
          </p>
        ) : (
          <ul className="mt-2 space-y-1">
            {nextSteps.map((step) => (
              <li key={step.sectionKey}>
                <Link
                  to={step.linkTo}
                  data-testid={step.testId}
                  className="text-sm text-primary underline-offset-4 hover:underline"
                >
                  {t(step.labelKey)}
                </Link>
              </li>
            ))}
          </ul>
        )}
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
        className="grid gap-3 sm:grid-cols-3"
      >
        <Link
          to="/app/perfil/dados"
          data-testid="profile-overview-link-data"
          className="block rounded-xl border border-border p-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <h3 className="font-medium">
            {t("profile.overview.section.household")}
          </h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.overview.linkData")}
          </p>
        </Link>
        <Link
          to="/app/perfil/preferencias"
          data-testid="profile-overview-link-preferences"
          className="block rounded-xl border border-border p-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <h3 className="font-medium">
            {t("profile.overview.section.preferences")}
          </h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.overview.linkPreferences")}
          </p>
        </Link>
        <Link
          to="/app/perfil/equipamentos"
          data-testid="profile-overview-link-equipment"
          className="block rounded-xl border border-border p-4 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <h3 className="font-medium">
            {t("profile.overview.section.equipment")}
          </h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("profile.overview.linkEquipment")}
          </p>
        </Link>
      </nav>
    </div>
  );
}
