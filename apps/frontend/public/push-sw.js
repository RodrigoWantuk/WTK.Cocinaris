/*
 * Plan notifications (ADR-0013), imported by the service worker the build generates.
 *
 * The server sends only a closed type and a language, so the sentence lives here and no text or
 * address from the message ever reaches the screen. Browsers require a visible notification for
 * each message, so anything unexpected still shows the same generic one.
 */
const PLAN_REMINDER_COPY = {
  "pt-BR": "Você tem algo do seu plano para resolver hoje",
  en: "You have something from your plan to take care of today",
  es: "Tienes algo de tu plan para resolver hoy",
};
const PLAN_REMINDER_URL = "/app/hoje";

function readPlanReminderLanguage(data) {
  try {
    const language = data ? data.json().language : null;
    return Object.hasOwn(PLAN_REMINDER_COPY, language) ? language : "pt-BR";
  } catch {
    return "pt-BR";
  }
}

self.addEventListener("push", (event) => {
  const language = readPlanReminderLanguage(event.data);
  event.waitUntil(
    self.registration.showNotification("Cocinaris", {
      body: PLAN_REMINDER_COPY[language],
      lang: language,
      icon: "/icons/icon-192.png",
      // A newer reminder replaces an unread one instead of piling up.
      tag: "plan-reminder",
    }),
  );
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(
    self.clients
      .matchAll({ type: "window", includeUncontrolled: true })
      .then((windows) => {
        const open = windows.find((client) => "focus" in client);
        if (!open) return self.clients.openWindow(PLAN_REMINDER_URL);
        return open
          .navigate(PLAN_REMINDER_URL)
          .then((client) => (client ?? open).focus());
      }),
  );
});
