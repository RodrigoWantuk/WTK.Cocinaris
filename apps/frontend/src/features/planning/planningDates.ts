/**
 * Calendar-day helpers for menus. Menu days are plain `YYYY-MM-DD` dates in the owner's calendar;
 * they are computed from the browser's local date because the server does not know the owner's
 * time zone.
 */

const pad = (value: number) => String(value).padStart(2, "0");

/** The browser's local calendar day as `YYYY-MM-DD`. */
export function localIsoDate(date: Date = new Date()): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Adds whole days to a `YYYY-MM-DD` date. */
export function addDays(iso: string, days: number): string {
  const [year, month, day] = iso.split("-").map(Number);
  return localIsoDate(new Date(year, month - 1, day + days));
}

/** Every day from `start` to `end`, inclusive (at most 31 days). */
export function daysBetween(start: string, end: string): string[] {
  const days: string[] = [];
  for (let day = start; day <= end && days.length < 31; day = addDays(day, 1))
    days.push(day);
  return days;
}

/** Weekday and day, for example "seg., 2 de nov.", in the interface locale. */
export function formatMenuDay(iso: string, locale: string): string {
  const [year, month, day] = iso.split("-").map(Number);
  return new Intl.DateTimeFormat(locale, {
    weekday: "short",
    day: "numeric",
    month: "short",
  }).format(new Date(year, month - 1, day));
}

/** A period such as "2 de nov. – 8 de nov.", in the interface locale. */
export function formatMenuPeriod(
  start: string,
  end: string,
  locale: string,
): string {
  const format = (iso: string) => {
    const [year, month, day] = iso.split("-").map(Number);
    return new Intl.DateTimeFormat(locale, {
      day: "numeric",
      month: "short",
    }).format(new Date(year, month - 1, day));
  };
  return start === end ? format(start) : `${format(start)} – ${format(end)}`;
}
