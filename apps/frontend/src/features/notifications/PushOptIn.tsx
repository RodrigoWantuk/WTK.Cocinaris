import { useEffect, useMemo, useState } from "react";
import { createLivePushRepository } from "@/adapters/live/livePushRepository";
import { useProductionI18n } from "@/app/i18n/ProductionI18nProvider";
import { useSession } from "@/app/session/SessionProvider";
import { Button } from "@/components/ui/button";
import type { PushRepository } from "@/contracts/pushNotifications";
import {
  currentPushSubscription,
  hasPushWorker,
  isPushSupported,
  pushPermission,
  subscribeToPush,
  unsubscribeFromPush,
} from "./pushBrowser";

const PROMPT_DISMISSED_KEY = "cocinaris.push.promptDismissed";

function promptDismissed(): boolean {
  try {
    return window.localStorage.getItem(PROMPT_DISMISSED_KEY) === "1";
  } catch {
    return false;
  }
}

function rememberDismissal(): void {
  try {
    window.localStorage.setItem(PROMPT_DISMISSED_KEY, "1");
  } catch {
    // Only a convenience: at worst, the invitation appears again.
  }
}

type PushState =
  | { kind: "hidden" }
  | { kind: "off" | "on" | "denied"; publicKey: string };

/**
 * Lets a person turn plan notifications on or off (ADR-0013). Renders nothing while loading, when the
 * browser or server cannot do push, or when no worker is registered. The browser's permission prompt
 * appears only after the button is tapped.
 *
 * @param variant `settings` always shows the current choice. `prompt` is the invitation after a plan
 * is accepted: it appears only for someone who never answered, and "Agora não" is remembered.
 * @param repository Injected in tests; the live repository is used when omitted.
 */
export function PushOptIn({
  variant,
  repository,
}: {
  variant: "settings" | "prompt";
  repository?: PushRepository;
}) {
  const { t } = useProductionI18n();
  const { session } = useSession();
  const repo = useMemo(
    () => repository ?? createLivePushRepository(),
    [repository],
  );
  const [state, setState] = useState<PushState>({ kind: "hidden" });
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const csrfToken = session.csrfToken ?? null;

  useEffect(() => {
    if (!isPushSupported() || !csrfToken) return;
    if (variant === "prompt" && promptDismissed()) return;
    const controller = new AbortController();
    void (async () => {
      const status = await repo.getStatus(controller.signal);
      if (!status.available || !status.publicKey) return;
      if (!(await hasPushWorker())) return;
      const current = await currentPushSubscription();
      if (controller.signal.aborted) return;
      if (current) {
        // Restores a row the server dropped; repeating it changes nothing.
        await repo.subscribe(current, csrfToken);
      }
      if (controller.signal.aborted) return;
      setState({
        kind: current ? "on" : pushPermission() === "denied" ? "denied" : "off",
        publicKey: status.publicKey,
      });
    })().catch(() => undefined);
    return () => controller.abort();
  }, [repo, csrfToken, variant]);

  if (state.kind === "hidden") return null;
  if (variant === "prompt" && state.kind !== "off") return null;

  const { publicKey } = state;

  async function run(action: () => Promise<PushState>) {
    setBusy(true);
    setFailed(false);
    try {
      setState(await action());
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  const turnOn = () =>
    run(async () => {
      const keys = await subscribeToPush(publicKey);
      if (!keys) {
        return {
          kind: pushPermission() === "denied" ? "denied" : "off",
          publicKey,
        };
      }
      await repo.subscribe(keys, csrfToken!);
      return { kind: "on", publicKey };
    });

  const turnOff = () =>
    run(async () => {
      const endpoint = await unsubscribeFromPush();
      if (endpoint) await repo.unsubscribe(endpoint, csrfToken!);
      return { kind: "off", publicKey };
    });

  return (
    <section
      data-testid={`push-opt-in-${variant}`}
      className="space-y-2 rounded-lg border border-border p-4"
    >
      <h2 className="font-medium">{t("push.title")}</h2>
      <p className="text-sm text-muted-foreground">{t("push.explain")}</p>
      {variant === "settings" ? (
        <p className="text-sm text-muted-foreground">{t("push.iphone")}</p>
      ) : null}
      {!session.timeZone ? (
        <p className="text-sm" data-testid="push-needs-zone">
          {t("push.needsZone")}
        </p>
      ) : null}
      {state.kind === "denied" ? (
        <p role="status" className="text-sm" data-testid="push-denied">
          {t("push.denied")}
        </p>
      ) : null}
      {state.kind === "on" ? (
        <p role="status" className="text-sm" data-testid="push-on">
          {t("push.on")}
        </p>
      ) : null}
      {failed ? (
        <p role="alert" className="text-sm" data-testid="push-failed">
          {t("push.failed")}
        </p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        {state.kind === "off" ? (
          <Button
            type="button"
            variant="secondary"
            disabled={busy}
            data-testid="push-turn-on"
            onClick={() => void turnOn()}
          >
            {t("push.turnOn")}
          </Button>
        ) : null}
        {state.kind === "on" ? (
          <Button
            type="button"
            variant="ghost"
            disabled={busy}
            data-testid="push-turn-off"
            onClick={() => void turnOff()}
          >
            {t("push.turnOff")}
          </Button>
        ) : null}
        {variant === "prompt" ? (
          <Button
            type="button"
            variant="ghost"
            disabled={busy}
            data-testid="push-not-now"
            onClick={() => {
              rememberDismissal();
              setState({ kind: "hidden" });
            }}
          >
            {t("push.notNow")}
          </Button>
        ) : null}
      </div>
    </section>
  );
}
