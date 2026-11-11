import { useState } from "react";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useOptionalRuntime } from "@/app/runtime/RuntimeProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RecipeApiError } from "@/contracts/recipes";
import type { PrivacyRepository } from "@/contracts/privacy";
import { unsubscribeFromPush } from "@/features/notifications/pushBrowser";

/** Where the browser goes once the account is gone; outside the app, so no session is needed. */
export const ACCOUNT_DELETED_PATH = "/conta-excluida";
const RETURN_PATH = "/app/perfil/privacidade";
/** The browser's own push removal waits at most this long; the server already forgot the address. */
const PUSH_TIMEOUT_MS = 1500;

type State = "idle" | "working" | "reauth" | "wrong" | "failed";

/** Sends the browser through sign-in again, asking for the password, and back to this page (decision E1). */
function signInAgain() {
  const form = document.createElement("form");
  form.method = "POST";
  form.action = `/api/v1/auth/login?returnUrl=${encodeURIComponent(RETURN_PATH)}&reauth=true`;
  form.style.display = "none";
  document.body.appendChild(form);
  form.submit();
}

/**
 * "Excluir minha conta" (PLAN-0049 Slice 2, decision D1): says what goes and what stays, asks for the
 * word of the page's language, and deletes at once. A sign-in older than 15 minutes is sent through
 * sign-in again first. After the deletion, this device forgets its offline list and its notification
 * subscription, as on logout, and leaves the app.
 *
 * @param repository The privacy repository of the page.
 * @param leave Replaces the navigation away, in tests.
 */
export function AccountDeletionSection({
  repository,
  leave = (path) => window.location.assign(path),
}: {
  repository: PrivacyRepository;
  leave?: (path: string) => void;
}) {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const runtime = useOptionalRuntime();
  const [typed, setTyped] = useState("");
  const [state, setState] = useState<State>("idle");
  const word = t("profile.privacy.delete.word");
  const matches = typed.trim().toLocaleUpperCase() === word.toLocaleUpperCase();

  const remove = async () => {
    const csrfToken = session.csrfToken;
    if (!matches || state === "working" || !csrfToken) return;
    setState("working");
    try {
      await repository.deleteAccount(typed.trim(), csrfToken);
    } catch (err) {
      const status = err instanceof RecipeApiError ? err.status : 0;
      setState(status === 403 ? "reauth" : status === 400 ? "wrong" : "failed");
      return;
    }
    // The account is gone; what this device kept goes too. Neither may hold the person here.
    await Promise.race([
      unsubscribeFromPush().catch(() => null),
      new Promise((resolve) => setTimeout(resolve, PUSH_TIMEOUT_MS)),
    ]);
    await runtime?.offlineListStore?.clearAll().catch(() => undefined);
    leave(ACCOUNT_DELETED_PATH);
  };

  return (
    <section aria-labelledby="privacy-delete" className="surface space-y-3 p-4">
      <h2 id="privacy-delete" className="text-xl font-semibold">
        {t("profile.privacy.delete.title")}
      </h2>
      <ul className="list-disc space-y-1 pl-5 text-sm">
        <li>{t("profile.privacy.delete.goes")}</li>
        <li>{t("profile.privacy.delete.login")}</li>
        <li>{t("profile.privacy.delete.usage")}</li>
        <li className="font-semibold">{t("profile.privacy.delete.final")}</li>
      </ul>
      <label
        htmlFor="privacy-delete-word"
        className="block text-sm font-medium"
      >
        {t("profile.privacy.delete.type", { word })}
      </label>
      <Input
        id="privacy-delete-word"
        data-testid="privacy-delete-word"
        value={typed}
        autoComplete="off"
        autoCapitalize="characters"
        onChange={(event) => {
          setTyped(event.target.value);
          if (state === "wrong") setState("idle");
        }}
      />
      <Button
        type="button"
        variant="destructive"
        data-testid="privacy-delete"
        disabled={!matches || state === "working"}
        onClick={() => void remove()}
      >
        {state === "working"
          ? t("profile.privacy.delete.working")
          : t("profile.privacy.delete.action")}
      </Button>
      <div role="status" aria-live="polite" className="text-sm">
        {state === "reauth" ? (
          <div className="space-y-2" data-testid="privacy-delete-reauth">
            <p>{t("profile.privacy.delete.reauth")}</p>
            <Button type="button" variant="secondary" onClick={signInAgain}>
              {t("profile.privacy.delete.reauth.action")}
            </Button>
          </div>
        ) : null}
        {state === "wrong" ? (
          <p className="text-destructive">
            {t("profile.privacy.delete.wrong", { word })}
          </p>
        ) : null}
        {state === "failed" ? (
          <p className="text-destructive">
            {t("profile.privacy.delete.failed")}
          </p>
        ) : null}
      </div>
    </section>
  );
}

/**
 * The page after a deletion, outside the app: the data is gone, and the login stays at the provider
 * (decision D2), so signing in again starts an empty account.
 */
export function AccountDeletedPage() {
  const { t } = useProductionI18n();
  return (
    <main
      className="mx-auto max-w-xl space-y-4 px-4 py-10"
      data-testid="account-deleted"
    >
      <h1 className="font-display text-3xl">
        {t("profile.privacy.deleted.title")}
      </h1>
      <p>{t("profile.privacy.deleted.body")}</p>
      <p className="text-sm text-muted-foreground">
        {t("profile.privacy.deleted.login")}
      </p>
      <Button asChild variant="secondary">
        <a href="/">{t("profile.privacy.deleted.home")}</a>
      </Button>
    </main>
  );
}
