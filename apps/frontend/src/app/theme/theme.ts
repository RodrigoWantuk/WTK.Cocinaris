import { useCallback, useEffect, useState } from "react";

/** The person's theme choice; `system` follows the operating system (PLAN-0057). */
export type ThemePreference = "system" | "light" | "dark";

/** Per-device storage key; a convenience, so a failed read simply means `system`. */
const STORAGE_KEY = "cocinaris.theme";
const QUERY = "(prefers-color-scheme: dark)";

/** Reads the stored preference; `system` when nothing usable is stored or storage is blocked. */
export function readThemePreference(): ThemePreference {
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : "system";
  } catch {
    return "system";
  }
}

function systemPrefersDark(): boolean {
  return (
    typeof window.matchMedia === "function" && window.matchMedia(QUERY).matches
  );
}

/** Applies a preference to `<html>` (the `dark` class drives the CSS tokens). */
export function applyTheme(preference: ThemePreference): void {
  const dark =
    preference === "dark" || (preference === "system" && systemPrefersDark());
  document.documentElement.classList.toggle("dark", dark);
}

/**
 * The current preference with a setter that stores and applies it. While the preference is
 * `system`, a change of the operating-system theme is applied live.
 */
export function useThemePreference(): [
  ThemePreference,
  (next: ThemePreference) => void,
] {
  const [preference, setPreference] =
    useState<ThemePreference>(readThemePreference);

  useEffect(() => {
    applyTheme(preference);
    if (preference !== "system" || typeof window.matchMedia !== "function")
      return;
    const media = window.matchMedia(QUERY);
    const onChange = () => applyTheme("system");
    media.addEventListener?.("change", onChange);
    return () => media.removeEventListener?.("change", onChange);
  }, [preference]);

  const choose = useCallback((next: ThemePreference) => {
    try {
      if (next === "system") window.localStorage.removeItem(STORAGE_KEY);
      else window.localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Storage may be blocked (private mode); the choice still applies for this visit.
    }
    setPreference(next);
  }, []);

  return [preference, choose];
}
