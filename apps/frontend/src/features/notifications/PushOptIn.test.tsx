import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PushRepository } from "@/contracts/pushNotifications";
import { PushOptIn } from "./PushOptIn";
import { removePushBeforeLogout } from "./pushBrowser";

const session = vi.hoisted(() => ({
  value: { csrfToken: "csrf", timeZone: "America/Sao_Paulo" } as {
    csrfToken: string | null;
    timeZone: string | null;
  },
}));

vi.mock("@/app/session/SessionProvider", () => ({
  useSession: () => ({ session: session.value }),
}));
vi.mock("@/app/i18n/ProductionI18nProvider", () => ({
  useProductionI18n: () => ({ t: (key: string) => key }),
}));

const KEY = "AQAB";
const KEYS = { endpoint: "https://push/1", p256dh: "p", auth: "a" };

function fakeSubscription() {
  return {
    endpoint: KEYS.endpoint,
    toJSON: () => ({
      endpoint: KEYS.endpoint,
      keys: { p256dh: KEYS.p256dh, auth: KEYS.auth },
    }),
    unsubscribe: vi.fn(async () => true),
  };
}

function installBrowser({
  existing = null,
  permission = "default",
  answer = "granted",
  worker = true,
}: {
  existing?: ReturnType<typeof fakeSubscription> | null;
  permission?: NotificationPermission;
  answer?: NotificationPermission;
  worker?: boolean;
} = {}) {
  const pushManager = {
    getSubscription: vi.fn(async () => existing),
    subscribe: vi.fn(async () => fakeSubscription()),
  };
  const requestPermission = vi.fn(async () => answer);
  vi.stubGlobal("PushManager", class {});
  vi.stubGlobal(
    "Notification",
    Object.assign(class {}, { permission, requestPermission }),
  );
  Object.defineProperty(navigator, "serviceWorker", {
    configurable: true,
    value: {
      getRegistration: vi.fn(async () =>
        worker ? { pushManager } : undefined,
      ),
    },
  });
  return { pushManager, requestPermission };
}

function repository(available = true): PushRepository {
  return {
    getStatus: vi.fn(async () => ({
      available,
      publicKey: available ? KEY : null,
    })),
    subscribe: vi.fn(async () => undefined),
    unsubscribe: vi.fn(async () => undefined),
  };
}

describe("PushOptIn", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
    session.value = { csrfToken: "csrf", timeZone: "America/Sao_Paulo" };
  });

  it("asks for permission only after the tap, then saves the subscription", async () => {
    const { requestPermission, pushManager } = installBrowser();
    const repo = repository();
    render(<PushOptIn variant="settings" repository={repo} />);

    const button = await screen.findByTestId("push-turn-on");
    expect(requestPermission).not.toHaveBeenCalled();
    await userEvent.click(button);

    expect(await screen.findByTestId("push-on")).toBeInTheDocument();
    expect(requestPermission).toHaveBeenCalledOnce();
    expect(pushManager.subscribe).toHaveBeenCalledWith(
      expect.objectContaining({ userVisibleOnly: true }),
    );
    expect(repo.subscribe).toHaveBeenCalledWith(KEYS, "csrf");
  });

  it("explains a refusal and saves nothing", async () => {
    const { pushManager } = installBrowser({ answer: "denied" });
    const repo = repository();
    render(<PushOptIn variant="settings" repository={repo} />);

    await userEvent.click(await screen.findByTestId("push-turn-on"));

    await screen.findByTestId("push-turn-on").catch(() => undefined);
    expect(pushManager.subscribe).not.toHaveBeenCalled();
    expect(repo.subscribe).not.toHaveBeenCalled();
  });

  it("shows a blocked permission without offering the button", async () => {
    installBrowser({ permission: "denied" });
    render(<PushOptIn variant="settings" repository={repository()} />);

    expect(await screen.findByTestId("push-denied")).toBeInTheDocument();
    expect(screen.queryByTestId("push-turn-on")).toBeNull();
  });

  it("turns off in the browser and on the server", async () => {
    const existing = fakeSubscription();
    installBrowser({ existing, permission: "granted" });
    const repo = repository();
    render(<PushOptIn variant="settings" repository={repo} />);

    await userEvent.click(await screen.findByTestId("push-turn-off"));

    await screen.findByTestId("push-turn-on");
    expect(existing.unsubscribe).toHaveBeenCalledOnce();
    expect(repo.unsubscribe).toHaveBeenCalledWith(KEYS.endpoint, "csrf");
  });

  it("reports a failure and stays usable", async () => {
    installBrowser();
    const repo = repository();
    vi.mocked(repo.subscribe).mockRejectedValueOnce(new Error("offline"));
    render(<PushOptIn variant="settings" repository={repo} />);

    await userEvent.click(await screen.findByTestId("push-turn-on"));

    expect(await screen.findByTestId("push-failed")).toBeInTheDocument();
    expect(screen.getByTestId("push-turn-on")).toBeEnabled();
  });

  it("hides itself without keys, without a worker, or without browser support", async () => {
    installBrowser();
    const off = repository(false);
    const { container, unmount } = render(
      <PushOptIn variant="settings" repository={off} />,
    );
    await waitFor(() => expect(off.getStatus).toHaveBeenCalled());
    await Promise.resolve();
    expect(container).toBeEmptyDOMElement();
    unmount();

    installBrowser({ worker: false });
    const noWorker = repository();
    const second = render(
      <PushOptIn variant="settings" repository={noWorker} />,
    );
    await waitFor(() => expect(noWorker.getStatus).toHaveBeenCalled());
    await new Promise((done) => setTimeout(done));
    expect(second.container).toBeEmptyDOMElement();
  });

  it("warns when the profile has no time zone", async () => {
    installBrowser();
    session.value = { csrfToken: "csrf", timeZone: null };
    render(<PushOptIn variant="settings" repository={repository()} />);
    expect(await screen.findByTestId("push-needs-zone")).toBeInTheDocument();
  });

  it("invites only someone who never answered, and remembers 'not now'", async () => {
    installBrowser();
    const first = render(
      <PushOptIn variant="prompt" repository={repository()} />,
    );
    await userEvent.click(await screen.findByTestId("push-not-now"));
    expect(first.container).toBeEmptyDOMElement();
    first.unmount();

    const repo = repository();
    const again = render(<PushOptIn variant="prompt" repository={repo} />);
    await new Promise((done) => setTimeout(done));
    expect(repo.getStatus).not.toHaveBeenCalled();
    expect(again.container).toBeEmptyDOMElement();
    again.unmount();

    window.localStorage.clear();
    installBrowser({ existing: fakeSubscription(), permission: "granted" });
    const subscribed = repository();
    const third = render(
      <PushOptIn variant="prompt" repository={subscribed} />,
    );
    await waitFor(() => expect(subscribed.subscribe).toHaveBeenCalled());
    await new Promise((done) => setTimeout(done));
    expect(third.container).toBeEmptyDOMElement();
  });
});

describe("removePushBeforeLogout", () => {
  it("ends the subscription and tells the server", async () => {
    const existing = fakeSubscription();
    installBrowser({ existing, permission: "granted" });
    const forget = vi.fn(async () => undefined);

    await removePushBeforeLogout(forget);

    expect(existing.unsubscribe).toHaveBeenCalledOnce();
    expect(forget).toHaveBeenCalledWith(KEYS.endpoint);
  });

  it("never fails the logout", async () => {
    installBrowser({ existing: fakeSubscription(), permission: "granted" });
    await expect(
      removePushBeforeLogout(async () => {
        throw new Error("offline");
      }),
    ).resolves.toBeUndefined();
  });

  it("does not wait for a stuck server past its limit", async () => {
    vi.useFakeTimers();
    try {
      installBrowser({ existing: fakeSubscription(), permission: "granted" });
      const finished = vi.fn();
      void removePushBeforeLogout(() => new Promise(() => undefined)).then(
        finished,
      );
      await vi.advanceTimersByTimeAsync(2000);
      expect(finished).toHaveBeenCalledOnce();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("push-sw.js", () => {
  function loadWorker() {
    const source = readFileSync(
      resolve(process.cwd(), "public/push-sw.js"),
      "utf8",
    );
    const listeners: Record<string, (event: unknown) => void> = {};
    const showNotification = vi.fn(async () => undefined);
    const focused = { focus: vi.fn(async () => undefined) };
    const open = {
      navigate: vi.fn(async () => focused),
      focus: vi.fn(async () => undefined),
    };
    const clients = {
      matchAll: vi.fn(async () => [open] as unknown[]),
      openWindow: vi.fn(async () => null),
    };
    const fakeSelf = {
      addEventListener: (name: string, listener: (event: unknown) => void) => {
        listeners[name] = listener;
      },
      registration: { showNotification },
      clients,
    };
    new Function("self", source)(fakeSelf);
    return { listeners, showNotification, clients, open, focused };
  }

  async function push(data: unknown) {
    const worker = loadWorker();
    let pending: Promise<unknown> = Promise.resolve();
    worker.listeners.push({
      data,
      waitUntil: (work: Promise<unknown>) => {
        pending = work;
      },
    });
    await pending;
    return worker.showNotification.mock.calls[0] as unknown as [
      string,
      { body: string; lang: string },
    ];
  }

  it.each([
    ["pt-BR", "Você tem algo do seu plano para resolver hoje"],
    ["en", "You have something from your plan to take care of today"],
    ["es", "Tienes algo de tu plan para resolver hoy"],
  ])("shows the generic sentence in %s", async (language, sentence) => {
    const [title, options] = await push({
      json: () => ({ type: "plan_reminder", language }),
    });
    expect(title).toBe("Cocinaris");
    expect(options.body).toBe(sentence);
    expect(options.lang).toBe(language);
  });

  it.each([
    ["no data", null],
    ["broken JSON", { json: () => JSON.parse("{") }],
    ["an unknown language", { json: () => ({ language: "__proto__" }) }],
    [
      "injected text",
      { json: () => ({ language: "xx", body: "Comprar remédio" }) },
    ],
  ])("still shows the same notification with %s", async (_name, data) => {
    const [, options] = await push(data);
    expect(options.body).toBe("Você tem algo do seu plano para resolver hoje");
  });

  it("opens only the app's own page, ignoring anything in the message", async () => {
    const worker = loadWorker();
    let pending: Promise<unknown> = Promise.resolve();
    const event = {
      notification: { close: vi.fn(), data: { url: "https://evil.example" } },
      waitUntil: (work: Promise<unknown>) => {
        pending = work;
      },
    };
    worker.clients.matchAll.mockResolvedValueOnce([]);
    worker.listeners.notificationclick(event);
    await pending;
    expect(event.notification.close).toHaveBeenCalledOnce();
    expect(worker.clients.openWindow).toHaveBeenCalledWith("/app/hoje");

    worker.listeners.notificationclick(event);
    await pending;
    expect(worker.open.navigate).toHaveBeenCalledWith("/app/hoje");
    expect(worker.focused.focus).toHaveBeenCalledOnce();
  });
});
