#!/usr/bin/env node
/**
 * Notification choice against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0062 Slice 7. The
 * replica must run with disposable keys in Planning__WebPush__*. The browser's push service is replaced
 * by a stand-in, because a headless browser has none: this covers the screen, the permission moment,
 * the API, the database, and logout cleanup, and not delivery. A real phone remains the check for that.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/push-opt-in-journey.mjs [username]
 * Never prints passwords, cookies, or keys.
 */
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium } = createRequire(path.join(repo, "apps/frontend/package.json"))("playwright");
const BASE = "https://localhost:7443";
const username = process.argv[2] ?? "inventory-user-a";
const password = process.env.COCINARIS_SMOKE_PASSWORD_A;
if (!password) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A is required.\n");
  process.exit(2);
}

const results = [];
const check = (name, ok, detail = "") => {
  results.push({ name, ok, detail });
  console.log(`[${ok ? "Passed" : "Failed"}] ${name}${detail ? ` — ${detail}` : ""}`);
};
const endpoint = `https://fcm.googleapis.com/fcm/send/e2e-${Date.now()}`;
const rows = () =>
  Number(
    execFileSync("docker", ["exec", "cocinaris-oa-postgres-1", "psql", "-U", "cocinaris_dev", "-d", "cocinaris", "-tAc", `SELECT count(*) FROM planning.push_subscriptions WHERE "Endpoint" = '${endpoint}'`], { encoding: "utf8" }).trim(),
  );

// A service worker refuses a certificate error, and the replica uses a local certificate.
const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 800 } });
await context.addInitScript((address) => {
  const encode = (bytes) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  const point = new Uint8Array(65);
  point[0] = 4;
  const auth = new Uint8Array(16);
  let current = null;
  // A headless browser answers "denied" by itself, so the permission is part of the stand-in too,
  // and the moment of asking is counted.
  window.__permissionRequests = 0;
  let permission = "default";
  Object.defineProperty(Notification, "permission", { get: () => permission });
  Notification.requestPermission = async () => {
    window.__permissionRequests += 1;
    permission = "granted";
    return permission;
  };
  PushManager.prototype.getSubscription = async () => current;
  PushManager.prototype.subscribe = async () => {
    current = {
      endpoint: address,
      toJSON: () => ({ endpoint: address, keys: { p256dh: encode(point), auth: encode(auth) } }),
      unsubscribe: async () => {
        current = null;
        return true;
      },
    };
    return current;
  };
}, endpoint);
const page = await context.newPage();
page.setDefaultTimeout(20000);
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
  check("the worker registers after sign-in", await page.evaluate(() => Promise.race([navigator.serviceWorker.ready.then(() => true), new Promise((done) => setTimeout(() => done(false), 15000))])));

  await page.goto(`${BASE}/app/perfil`, { waitUntil: "domcontentloaded" });
  const section = page.getByTestId("push-opt-in-settings");
  await section.waitFor({ timeout: 20000 });
  check("the profile offers notifications when the server has keys", true);
  check("opening the page does not ask for permission", (await page.evaluate(() => window.__permissionRequests)) === 0);
  check("the page says what the notification contains and about iPhone", /não diz o que é/.test(await section.innerText()) && /iPhone/.test(await section.innerText()));

  await page.setViewportSize({ width: 320, height: 700 });
  check("320 px has no horizontal scroll", await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth));
  await page.setViewportSize({ width: 390, height: 800 });

  await page.getByTestId("push-turn-on").click();
  await page.getByTestId("push-on").waitFor({ timeout: 10000 });
  check("permission is asked once, after the tap", (await page.evaluate(() => window.__permissionRequests)) === 1);
  check("turning on saves one row on the server", rows() === 1, `rows=${rows()}`);

  await page.getByTestId("push-turn-off").click();
  await page.getByTestId("push-turn-on").waitFor({ timeout: 10000 });
  check("turning off removes it", rows() === 0, `rows=${rows()}`);

  await page.getByTestId("push-turn-on").click();
  await page.getByTestId("push-on").waitFor({ timeout: 10000 });
  const started = Date.now();
  const logout = await page.evaluate(async () => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    return session.csrfToken;
  });
  check("a session exists before the logout", Boolean(logout));
  await page.getByTestId("production-account-menu").click({ timeout: 5000 });
  await page.getByTestId(process.env.COCINARIS_LOGOUT_TESTID ?? "production-logout").click({ timeout: 10000 });
  await page.waitForURL((url) => !url.pathname.startsWith("/app"), { timeout: 20000 });
  check("a logout removes this browser's subscription", rows() === 0, `rows=${rows()}, ${Date.now() - started} ms`);
} catch (error) {
  check("journey finished", false, String(error).split("\n")[0]);
  const text = await page.getByTestId("push-opt-in-settings").innerText({ timeout: 1000 }).catch(() => "(section absent)");
  console.log(`Section at failure: ${text.replace(/\s+/g, " ")}`);
} finally {
  await browser.close();
}

const failed = results.filter((item) => !item.ok).length;
console.log(failed === 0 ? `All ${results.length} checks passed.` : `${failed} of ${results.length} checks failed.`);
process.exit(failed === 0 ? 0 : 1);
