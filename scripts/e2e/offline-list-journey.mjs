#!/usr/bin/env node
/**
 * Offline shopping list journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0062
 * gate G3. It seeds an accepted menu with two dishes, then goes offline: the application opens from
 * the service worker, the saved list appears, only marks work, and they reach the server when the
 * connection returns. It also asserts that no API response is in cache storage, that a mark on a line
 * removed meanwhile is reported, that a 401 closes the offline list, that logout erases the device,
 * and that a second owner sees nothing of the first. Names are unique per run.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<fixture> COCINARIS_SMOKE_PASSWORD_B=<fixture> \
 *          node scripts/e2e/offline-list-journey.mjs
 * Never prints passwords or cookies.
 */
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium } = createRequire(path.join(repo, "apps/frontend/package.json"))("playwright");
const BASE = "https://localhost:7443";
const passwordA = process.env.COCINARIS_SMOKE_PASSWORD_A;
const passwordB = process.env.COCINARIS_SMOKE_PASSWORD_B;
if (!passwordA || !passwordB) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A and COCINARIS_SMOKE_PASSWORD_B are required.\n");
  process.exit(2);
}

const results = [];
const check = (name, ok, detail = "") => results.push({ name, ok, detail });

// A service worker refuses a certificate error even with ignoreHTTPSErrors, and the replica's
// certificate is local.
const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 844 } });
const page = await context.newPage();

async function signIn(username, password) {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  // The provider may still hold its own session after a logout, and then signs in with no form.
  const app = /localhost:7443\/app\//;
  await Promise.race([page.waitForSelector("#kc-form-login", { timeout: 30000 }), page.waitForURL(app, { timeout: 30000 })]);
  if (app.test(page.url())) return;
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(app, { timeout: 30000 }), page.click("#kc-login")]);
}

/** Everything in cache storage; the worker may hold assets only. */
const cachedUrls = () => page.evaluate(async () => {
  const urls = [];
  for (const name of await caches.keys()) {
    for (const request of await (await caches.open(name)).keys()) urls.push(new URL(request.url).pathname);
  }
  return urls;
});

/** Keys in the offline database, or an empty list when it does not exist. */
const deviceKeys = () => page.evaluate(() => new Promise((resolve, reject) => {
  const open = indexedDB.open("cocinaris-offline");
  open.onerror = () => reject(open.error);
  open.onsuccess = () => {
    const db = open.result;
    if (!db.objectStoreNames.contains("kv")) return resolve([]);
    const keys = db.transaction("kv").objectStore("kv").getAllKeys();
    keys.onsuccess = () => resolve(keys.result.map(String));
    keys.onerror = () => reject(keys.error);
  };
}));

const workerControls = () => page.waitForFunction(async () => {
  const registration = await navigator.serviceWorker.ready;
  return Boolean(registration.active && navigator.serviceWorker.controller);
}, null, { timeout: 30000 });

try {
  await signIn("inventory-user-a", passwordA);
  const run = Date.now();
  const kept = `Feijão E2E ${run}`;
  const later = `Arroz E2E ${run}`;
  const removed = `Coentro E2E ${run}`;
  const seed = await page.evaluate(async ({ kept, later, removed, run }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) => {
      const response = await fetch(url, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      if (!response.ok) throw new Error(`${url} answered ${response.status}`);
      return response.json();
    };
    const recipe = (name, ingredients) => post("/api/v1/recipes", { content: {
      name, mealTypes: ["dinner"], servings: 2,
      ingredients: ingredients.map((displayName) => ({ displayName, quantity: 200, unit: "g", optional: false })),
      stages: [{ name: "Cozinhar", instructions: "Cozinhe tudo.", activeMinutes: 10, passiveMinutes: 20 }],
    } });
    const first = await recipe(`Prato E2E ${run}`, [kept, later]);
    const second = await recipe(`Salada E2E ${run}`, [removed]);
    const now = new Date();
    const iso = (date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
    const today = iso(now);
    const end = iso(new Date(now.getTime() + 6 * 86400000));
    const menu = await post("/api/v1/planning/menus", { name: `Offline E2E ${run}`, startDate: today, endDate: end });
    await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: first.recipeId, plannedDate: today, mealType: "dinner", servings: 2, pinRevision: false });
    const withSecond = await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: second.recipeId, plannedDate: end, mealType: "dinner", servings: 2, pinRevision: false });
    await post(`/api/v1/planning/menus/${menu.menuId}/acceptance`);
    return { menuId: menu.menuId, today, removedEntryId: withSecond.entries.at(-1).entryId, csrf: session.csrfToken };
  }, { kept, later, removed, run });
  const listUrl = `${BASE}/app/cardapio/${seed.menuId}/compras`;
  const serverList = () => page.evaluate(async ({ menuId, today }) =>
    (await fetch(`/api/v1/planning/menus/${menuId}/shopping?from=${today}`, { credentials: "include" })).json(), seed);
  const row = (name) => page.getByTestId("shopping-item").filter({ hasText: name }).first();

  await page.goto(listUrl, { waitUntil: "networkidle" });
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  await workerControls();
  check("the worker registers and controls the page", true);
  // Saved after the list settles; waiting on the key avoids racing the first write.
  await page.waitForFunction(() => new Promise((resolve) => {
    const open = indexedDB.open("cocinaris-offline");
    open.onsuccess = () => {
      const db = open.result;
      if (!db.objectStoreNames.contains("kv")) return resolve(false);
      const keys = db.transaction("kv").objectStore("kv").getAllKeys();
      keys.onsuccess = () => resolve(keys.result.some((key) => String(key).endsWith(":lists")));
    };
  }), null, { timeout: 10000 });

  let cached = await cachedUrls();
  check("the worker precached the application", cached.includes("/index.html"), `${cached.length} files`);
  check("no API response is in cache storage (online)", !cached.some((url) => url.startsWith("/api")));

  await context.setOffline(true);
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  check("the saved list opens with no connection", (await row(kept).count()) === 1);
  check("the banner explains marks go later", (await page.getByTestId("offline-banner").textContent()).includes("enviadas depois"));
  check("purchases are disabled offline", await row(kept).getByTestId("shopping-item-buy").isDisabled());
  check("'I have it' is disabled offline", await row(kept).getByTestId("shopping-item-have").isDisabled());
  const hScroll = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  check("no horizontal scroll at phone width", !hScroll);

  await row(kept).getByTestId("shopping-item-check").check();
  await row(later).getByTestId("shopping-not-found").click();
  check("substitutes are disabled offline", await page.getByTestId("shopping-substitute").isDisabled());
  await page.getByTestId("shopping-later-save").click();
  await page.getByTestId("shopping-later").waitFor({ timeout: 5000 });
  await row(removed).getByTestId("shopping-item-check").check();

  await page.reload({ waitUntil: "domcontentloaded" });
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  check("marks survive a reload offline", await row(kept).getByTestId("shopping-item-check").isChecked());
  check("'buy later' survives a reload offline", (await page.getByTestId("shopping-later").textContent()).includes(later));

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("offline-route").waitFor({ timeout: 15000 });
  check("other routes show the banner and no data", (await page.getByTestId("offline-banner").count()) === 1);
  await page.getByTestId("offline-open-list").click();
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  check("the list opens from the hub offline", (await row(kept).count()) === 1);
  cached = await cachedUrls();
  check("no API response is in cache storage (offline)", !cached.some((url) => url.startsWith("/api")));

  // The list changes elsewhere while this device is offline. The request context is not offline.
  const removal = await context.request.delete(`${BASE}/api/v1/planning/menus/${seed.menuId}/entries/${seed.removedEntryId}`, { headers: { "X-CSRF-TOKEN": seed.csrf } });
  check("the second dish leaves the plan elsewhere", removal.ok(), String(removal.status()));

  await context.setOffline(false);
  await page.getByTestId("offline-banner").waitFor({ state: "detached", timeout: 40000 });
  await page.getByTestId("offline-conflicts").waitFor({ timeout: 15000 });
  check("a mark on a removed line is named", (await page.getByTestId("offline-conflicts").textContent()).includes(removed));
  await page.getByTestId("offline-conflicts-dismiss").click();
  const synced = await serverList();
  const line = (name) => synced.items.find((item) => item.displayName === name);
  check("the check mark reached the server", line(kept)?.checked === true);
  check("the 'buy later' mark reached the server", line(later)?.later === true);
  await page.reload({ waitUntil: "networkidle" });
  check("online again, purchases work", await row(kept).getByTestId("shopping-item-buy").isEnabled());
  check("no mark is left waiting", !(await page.getByTestId("offline-conflicts").count()));

  // Logout: the device is erased.
  await page.getByTestId("production-account-menu").click();
  await page.getByTestId("production-logout").click();
  await page.waitForURL(`${BASE}/`, { timeout: 20000 });
  await page.waitForLoadState("networkidle");
  check("logout erases the device", (await deviceKeys()).length === 0, (await deviceKeys()).join(",").replace(/[0-9a-f-]{36}/g, "<id>"));

  // An ended session: the list stops opening offline, and the marks wait for the same owner.
  await signIn("inventory-user-a", passwordA);
  await page.goto(listUrl, { waitUntil: "networkidle" });
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  await page.waitForTimeout(500);
  await context.clearCookies();
  await page.reload({ waitUntil: "networkidle" });
  await page.getByTestId("production-login").waitFor({ timeout: 15000 });
  check("a 401 goes to sign-in", true);
  await context.setOffline(true);
  await page.goto(listUrl, { waitUntil: "domcontentloaded" });
  await page.waitForFunction(() => document.body.innerText.length > 0, null, { timeout: 15000 });
  await page.waitForTimeout(1000);
  check("after a 401 the list does not open offline", (await page.getByTestId("shopping-page").count()) === 0);
  await context.setOffline(false);

  // A different owner on the same device, with no logout in between.
  await signIn("inventory-user-b", passwordB);
  await page.waitForLoadState("networkidle");
  const keys = await deviceKeys();
  check("a second owner erases the first owner's data", !keys.some((key) => key.endsWith(":lists")), keys.join(",").replace(/[0-9a-f-]{36}/g, "<id>"));
  await context.setOffline(true);
  await page.goto(`${BASE}/app/compras`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("shopping-hub-offline").waitFor({ timeout: 15000 });
  check("a second owner sees no list offline", (await page.getByText(kept).count()) === 0);
  await context.setOffline(false);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
