/** What a browser subscription needs on the server (ADR-0013). */
export interface PushSubscriptionKeys {
  endpoint: string;
  p256dh: string;
  auth: string;
}

/** Whether notifications can be offered. */
export interface PushStatus {
  available: boolean;
  publicKey: string | null;
}

/** Server side of a person's choice about notifications. */
export interface PushRepository {
  getStatus(signal?: AbortSignal): Promise<PushStatus>;
  subscribe(keys: PushSubscriptionKeys, csrfToken: string): Promise<void>;
  unsubscribe(endpoint: string, csrfToken: string): Promise<void>;
}
