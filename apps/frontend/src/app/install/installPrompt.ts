import { useEffect, useState } from "react";

/**
 * Installable-app support (PLAN-0059). Chromium browsers fire `beforeinstallprompt` once the site
 * meets their install criteria (manifest, icons, HTTPS). The event can arrive before React
 * renders, so it is captured at startup and kept here until the install banner asks for it.
 * Safari on iOS has no such event: there the banner explains "Compartilhar → Adicionar à Tela de
 * Início" instead.
 */

/** The non-standard Chromium event; typed here because it is not in the DOM library. */
export interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

let deferred: BeforeInstallPromptEvent | null = null;
let installed = false;
let started = false;
const listeners = new Set<() => void>();
const notify = () => listeners.forEach((listener) => listener());

/**
 * Starts listening for the browser's install offer. Idempotent; call once at startup. The
 * browser's own mini-infobar is suppressed so the app can offer installation in its own words,
 * at a calmer moment; the browser menu keeps its install entry.
 */
export function startInstallPromptCapture(): void {
  if (started || typeof window === "undefined") return;
  started = true;
  window.addEventListener("beforeinstallprompt", (event) => {
    event.preventDefault();
    deferred = event as BeforeInstallPromptEvent;
    notify();
  });
  window.addEventListener("appinstalled", () => {
    deferred = null;
    installed = true;
    notify();
  });
}

/** Whether the app already runs from the home screen (installed). */
export function isStandalone(): boolean {
  if (typeof window === "undefined") return false;
  const iosStandalone = (navigator as Navigator & { standalone?: boolean })
    .standalone;
  return (
    iosStandalone === true ||
    (typeof window.matchMedia === "function" &&
      window.matchMedia("(display-mode: standalone)").matches)
  );
}

/** Safari on iPhone or iPad, where installation is manual (no install event). */
export function isIosSafari(userAgent = navigator.userAgent): boolean {
  const ios =
    /iPhone|iPad|iPod/.test(userAgent) ||
    // iPadOS reports a Mac user agent; touch support tells them apart.
    (/Macintosh/.test(userAgent) && navigator.maxTouchPoints > 1);
  // Other browsers on iOS (Chrome, Firefox, Edge) cannot add to the home screen the same way.
  return ios && !/CriOS|FxiOS|EdgiOS/.test(userAgent);
}

/**
 * The install offer as React state: whether the browser offered installation, whether it was
 * installed, and `prompt()` to show the browser's dialog (usable once per offer).
 */
export function useInstallPrompt() {
  const [, setVersion] = useState(0);
  useEffect(() => {
    const listener = () => setVersion((value) => value + 1);
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
    };
  }, []);
  return {
    canPrompt: deferred !== null,
    installed,
    async prompt(): Promise<"accepted" | "dismissed" | "unavailable"> {
      const event = deferred;
      if (!event) return "unavailable";
      deferred = null;
      await event.prompt();
      const { outcome } = await event.userChoice;
      notify();
      return outcome;
    },
  };
}

/** Test hook: forget the captured state between tests. */
export function resetInstallPromptForTests(): void {
  deferred = null;
  installed = false;
  started = false;
  listeners.clear();
}
