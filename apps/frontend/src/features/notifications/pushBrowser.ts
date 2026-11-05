import type { PushSubscriptionKeys } from "@/contracts/pushNotifications";

/** Longest wait for the browser's push service, so a logout or a page never hangs on it. */
const LOGOUT_TIMEOUT_MS = 2000;

/** True when this browser can receive web push at all. */
export function isPushSupported(): boolean {
  return (
    typeof window !== "undefined" &&
    "serviceWorker" in navigator &&
    "PushManager" in window &&
    "Notification" in window
  );
}

/** The current permission, or "denied" when the browser has no notifications. */
export function pushPermission(): NotificationPermission {
  return "Notification" in window ? Notification.permission : "denied";
}

async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!isPushSupported()) return null;
  // Not `ready`: it never settles where no worker exists, such as the prototype or a first load.
  return (await navigator.serviceWorker.getRegistration()) ?? null;
}

function toKeys(subscription: PushSubscription): PushSubscriptionKeys | null {
  const json = subscription.toJSON();
  if (!json.endpoint || !json.keys?.p256dh || !json.keys.auth) return null;
  return {
    endpoint: json.endpoint,
    p256dh: json.keys.p256dh,
    auth: json.keys.auth,
  };
}

function decodeBase64Url(value: string): Uint8Array<ArrayBuffer> {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, "="));
  const bytes = new Uint8Array(new ArrayBuffer(binary.length));
  for (let index = 0; index < binary.length; index++) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

/** True when a service worker is registered, which push needs. */
export async function hasPushWorker(): Promise<boolean> {
  return (await registration()) !== null;
}

/** This browser's current subscription, if any. */
export async function currentPushSubscription(): Promise<PushSubscriptionKeys | null> {
  const subscription = await (
    await registration()
  )?.pushManager.getSubscription();
  return subscription ? toKeys(subscription) : null;
}

/**
 * Asks for permission when needed and subscribes. Call only from a person's tap: the browser's prompt
 * must never appear on its own (ADR-0013).
 *
 * @param publicKey The server's key.
 * @returns The subscription, or null when permission was refused or the worker is missing.
 */
export async function subscribeToPush(
  publicKey: string,
): Promise<PushSubscriptionKeys | null> {
  const worker = await registration();
  if (!worker || (await Notification.requestPermission()) !== "granted") {
    return null;
  }
  const subscription = await worker.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: decodeBase64Url(publicKey),
  });
  return toKeys(subscription);
}

/** Ends this browser's subscription and returns its address, so the server can forget it. */
export async function unsubscribeFromPush(): Promise<string | null> {
  const subscription = await (
    await registration()
  )?.pushManager.getSubscription();
  if (!subscription) return null;
  const endpoint = subscription.endpoint;
  await subscription.unsubscribe();
  return endpoint;
}

/**
 * Removes this browser's subscription before a logout, so the next person on a shared device gets no
 * notifications. Best effort within a short limit: it never fails or delays the logout for long.
 *
 * @param forget Tells the server to forget the address.
 */
export async function removePushBeforeLogout(
  forget: (endpoint: string) => Promise<void>,
): Promise<void> {
  const removal = (async () => {
    const endpoint = await unsubscribeFromPush();
    if (endpoint) await forget(endpoint);
  })().catch(() => undefined);
  await Promise.race([
    removal,
    new Promise((resolve) => setTimeout(resolve, LOGOUT_TIMEOUT_MS)),
  ]);
}
