#!/usr/bin/env node
// Checks the built service worker in a real browser (ADR-0013): it imports push-sw.js, and a push
// event calls showNotification with the generic sentence in the language sent. Delivery goes through
// the browser's debugging protocol, so no push service or key is involved, and the headless browser
// listed no notifications. A real phone remains the check for delivery and display.
//
// Usage, from apps/frontend after a production build: node ../../scripts/e2e/push-worker-check.mjs
import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { createRequire } from "node:module";
import { extname, join, resolve } from "node:path";

const require = createRequire(join(process.cwd(), "package.json"));
const { chromium } = require("playwright");

const root = resolve(process.cwd(), "build");
const types = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".png": "image/png", ".svg": "image/svg+xml", ".webmanifest": "application/manifest+json" };
const server = createServer((request, response) => {
  const path = new URL(request.url, "http://x").pathname;
  const candidate = join(root, path);
  const file = candidate.startsWith(root) && existsSync(candidate) && statSync(candidate).isFile() ? candidate : join(root, "index.html");
  response.writeHead(200, { "content-type": types[extname(file)] ?? "application/octet-stream" });
  createReadStream(file).pipe(response);
});
await new Promise((done) => server.listen(0, "127.0.0.1", done));
const origin = `http://127.0.0.1:${server.address().port}`;

const results = [];
const check = (name, passed, detail = "") => {
  results.push(passed);
  console.log(`[${passed ? "Passed" : "Failed"}] ${name}${detail ? ` — ${detail}` : ""}`);
};

const browser = await chromium.launch();
try {
  const context = await browser.newContext({ baseURL: origin });
  await context.grantPermissions(["notifications"], { origin });
  const page = await context.newPage();
  await page.goto("/", { waitUntil: "load" });
  const scope = await page.evaluate(async () => (await navigator.serviceWorker.ready).scope);
  check("the worker registers", scope === `${origin}/`, scope);

  const worker = context.serviceWorkers()[0] ?? (await context.waitForEvent("serviceworker"));
  const client = await context.newCDPSession(page);
  await client.send("ServiceWorker.enable");
  const versionId = await new Promise((done) => {
    client.on("ServiceWorker.workerVersionUpdated", ({ versions }) => {
      const running = versions.find((item) => item.runningStatus === "running");
      if (running) done(running);
    });
  });

  // headless_shell listed no notifications when tried, so the call itself is recorded inside the worker.
  // The newest call stands for what a person would see, because every call uses the same tag.
  await worker.evaluate(() => {
    self.__shown = [];
    self.registration.showNotification = async (title, options) => {
      self.__shown.push({ title, body: options.body, tag: options.tag, lang: options.lang, icon: options.icon });
    };
  });
  const deliver = async (data) => {
    await worker.evaluate(() => {
      self.__shown = [];
    });
    await client.send("ServiceWorker.deliverPushMessage", { origin, registrationId: versionId.registrationId, data });
    await page.waitForTimeout(500);
    return worker.evaluate(() => self.__shown);
  };

  const spanish = await deliver(JSON.stringify({ type: "plan_reminder", language: "es" }));
  check("a push shows the generic sentence in the language sent", spanish.length === 1 && spanish[0].title === "Cocinaris" && spanish[0].body === "Tienes algo de tu plan para resolver hoy", JSON.stringify(spanish));

  const repeated = await deliver(JSON.stringify({ type: "plan_reminder", language: "pt-BR" }));
  check("a newer notification uses the tag that replaces an unread one", repeated.length === 1 && repeated[0].tag === "plan-reminder" && repeated[0].body === "Você tem algo do seu plano para resolver hoje", JSON.stringify(repeated));

  const hostile = await deliver(JSON.stringify({ language: "xx", body: "Comprar remédio", url: "https://evil.example" }));
  check("text in a message never reaches the screen", hostile.length === 1 && !JSON.stringify(hostile).includes("remédio"), JSON.stringify(hostile));

  const broken = await deliver("not json");
  check("a broken message still shows the generic notification", broken.length === 1 && broken[0].body === "Você tem algo do seu plano para resolver hoje");

  const cached = await page.evaluate(async () => {
    const urls = [];
    for (const name of await caches.keys()) {
      for (const request of await (await caches.open(name)).keys()) urls.push(new URL(request.url).pathname);
    }
    return urls;
  });
  check("no API response is in the worker's cache", !cached.some((path) => path.startsWith("/api")), `${cached.length} entries`);
  void worker;
} finally {
  await browser.close();
  server.close();
}

const failed = results.filter((passed) => !passed).length;
console.log(failed === 0 ? `All ${results.length} checks passed.` : `${failed} of ${results.length} checks failed.`);
process.exit(failed === 0 ? 0 : 1);
