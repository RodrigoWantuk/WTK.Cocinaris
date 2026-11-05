// Replaces sw.js when the active release has no service worker (ADR-0012). Without it, a browser
// that once installed the worker would keep serving the cached application forever, because the
// update check for /sw.js receives index.html and fails. This worker clears the asset caches,
// removes itself, and reloads open pages. It never touches IndexedDB.
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      for (const name of await caches.keys()) await caches.delete(name);
      await self.registration.unregister();
      for (const client of await self.clients.matchAll({ type: "window" })) {
        client.navigate(client.url);
      }
    })(),
  );
});
