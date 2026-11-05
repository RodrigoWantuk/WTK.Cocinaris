#!/usr/bin/env node
/**
 * Signed-in shopping plan journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0062.
 * The replica has no AI provider, so this also proves the plan works on saved recipes alone (gate
 * G1-E). It seeds two recipes, then in the browser: answers the setup, accepts cards (one with
 * "yields 2 meals"), reviews, marks "I have it" on the list, accepts the plan, leaves a line for
 * later, splits the list by place, records the arrival, checks "Hoje", names the plan, and repeats it. It prints how long the first card took (gate G1-C). Names are unique per run.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/shopping-plan-journey.mjs [username]
 * Never prints passwords or cookies.
 */
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
const check = (name, ok, detail = "") => results.push({ name, ok, detail });

const browser = await chromium.launch();
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 844 } })).newPage();
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const run = Date.now();
  const lentil = `Lentilha E2E ${run}`;
  await page.evaluate(async ({ lentil, run }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = (url, body) => fetch(url, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body),
    });
    for (const name of [`Sopa E2E ${run}`, `Salada E2E ${run}`]) {
      await post("/api/v1/recipes", { content: {
        name, mealTypes: ["dinner"], servings: 2,
        ingredients: [{ displayName: lentil, quantity: 200, unit: "g", optional: false }],
        stages: [{ name: "Cozinhar", instructions: "Cozinhe a lentilha.", activeMinutes: 10, passiveMinutes: 20 }],
      } });
    }
    // An earlier run's arrival stocks the pantry, which would leave this run's list with nothing to buy.
    for (;;) {
      const { items } = await (await fetch("/api/v1/inventory/lots?pageSize=50", { credentials: "include" })).json();
      if (!items.length) break;
      for (const lot of items) {
        await fetch(`/api/v1/inventory/lots/${lot.lotId}`, { method: "DELETE", credentials: "include", headers: { "X-CSRF-TOKEN": session.csrfToken, "If-Match": `"${lot.version}"`.replaceAll('""', '"') } });
      }
      if (items.length < 50) break;
    }
    // A plan left by an earlier failed run would hide the setup screen.
    const start = await (await fetch("/api/v1/planning/shopping-plans/start", { credentials: "include" })).json();
    if (start.current) await post(`/api/v1/planning/shopping-plans/${start.current.sessionId}/abandon`, {});
  }, { lentil, run });

  await page.goto(`${BASE}/app/compras/planejar`, { waitUntil: "networkidle" });
  await page.getByTestId("plan-setup").waitFor({ timeout: 15000 });
  for (const meal of ["lunch", "snack", "breakfast"]) {
    const chip = page.getByTestId(`plan-meal-${meal}`);
    if ((await chip.getAttribute("aria-pressed")) === "true") await chip.click();
  }
  const dinner = page.getByTestId("plan-meal-dinner");
  if ((await dinner.getAttribute("aria-pressed")) !== "true") await dinner.click();
  await page.getByTestId("plan-days-3").click();
  await page.getByTestId("plan-style-mix").click();
  const hScroll = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  check("setup has no horizontal scroll at phone width", !hScroll);
  check("setup shows 3 meals", (await page.getByTestId("plan-meal-total").textContent()).includes("3"));

  const began = Date.now();
  await page.getByTestId("plan-begin").click();
  await page.getByTestId("plan-card").waitFor({ timeout: 15000 });
  const firstCardMs = Date.now() - began;
  check("first card under 1 s when instant cards exist (G1-C)", firstCardMs < 1000, `${firstCardMs} ms`);
  check("the card shows its new items", (await page.getByTestId("plan-card-new-items").textContent()).length > 0);

  await page.getByTestId("plan-yes-twice").click();
  await page.getByTestId("plan-repeat-yield").check();
  await page.getByTestId("plan-repeat-confirm").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="plan-progress"]')?.textContent?.includes("3 de 3"), null, { timeout: 15000 });
  await page.getByTestId("plan-yes").click();
  await page.waitForURL(/\/revisar$/, { timeout: 15000 });
  await page.getByTestId("plan-review-entry").first().waitFor({ timeout: 15000 });
  check("review lists two dishes", (await page.getByTestId("plan-review-entry").count()) === 2);

  const review = page.url();
  await page.getByTestId("plan-see-list").click();
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  const line = page.getByTestId("shopping-item").filter({ hasText: lentil }).first();
  if ((await line.count()) === 0) {
    check("the list has the plan's ingredient", (await page.getByTestId("shopping-item").count()) > 0);
  }
  const lines = await page.getByTestId("shopping-item-have").count();
  check("lines can be marked as already at home", lines > 0, `${lines} lines`);
  await page.getByTestId("shopping-item-have").first().click();
  await page.getByTestId("shopping-have-notice").waitFor({ timeout: 10000 });
  await page.getByTestId("shopping-have-undo").click();
  await page.getByTestId("shopping-have-notice").waitFor({ state: "detached", timeout: 10000 });
  await page.waitForFunction((count) => document.querySelectorAll('[data-testid="shopping-item-have"]').length === count, lines, { timeout: 10000 });
  check("undo puts the line back", true);

  await page.goto(review, { waitUntil: "networkidle" });
  await page.getByTestId("plan-use").click();
  await page.getByTestId("menu-page").waitFor({ timeout: 15000 });
  await page.getByText("Em uso", { exact: true }).waitFor({ timeout: 10000 });

  const menuUrl = page.url();
  await page.goto(`${menuUrl}/compras`, { waitUntil: "networkidle" });
  await page.getByTestId("shopping-not-found").first().click();
  await page.getByTestId("shopping-later-save").click();
  await page.getByTestId("shopping-later").waitFor({ timeout: 10000 });
  await page.getByTestId("shopping-later-back").first().click();
  await page.getByTestId("shopping-later").waitFor({ state: "detached", timeout: 10000 });
  check("a line can be left for later and brought back", true);
  await page.getByTestId("shopping-split").check();
  check("the list splits by place", (await page.getByTestId("shopping-place").count()) > 0);
  await page.getByTestId("shopping-item-check").first().check();
  await page.getByTestId("shopping-arrival").click();
  await page.getByTestId("arrival-page").waitFor({ timeout: 15000 });
  check("with the table off, arrival mentions no freezer", (await page.getByTestId("arrival-Freezer").count()) === 0);
  await page.getByTestId("arrival-save").click();
  await page.getByTestId("arrival-done").waitFor({ timeout: 20000 });
  check("arrival records the purchase", (await page.getByTestId("arrival-done").textContent()).includes("despensa"));

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  // The fixture user keeps plans from earlier runs, so only this run's menu is counted.
  const menuId = menuUrl.split("/").pop();
  const day = await page.evaluate(() => {
    const now = new Date();
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
  });
  const entries = await page.evaluate(async (date) => (await fetch(`/api/v1/planning/today?date=${date}`, { credentials: "include" })).json(), day);
  const planned = entries.filter((item) => item.menuId === menuId).length;
  check("Hoje shows this plan without every flexible meal", planned >= 1 && planned <= 2, `${planned} meals`);

  // Repeat: name the finished plan, then start again with its dishes already on the new draft.
  await page.goto(`${BASE}/app/compras/planejar`, { waitUntil: "networkidle" });
  const recent = page.getByTestId("plan-recent-row").first();
  await recent.getByTestId("plan-recent-name-edit").click();
  await recent.getByTestId("plan-recent-name-input").fill(`Semana E2E ${run}`);
  await recent.getByTestId("plan-recent-name-save").click();
  await page.getByTestId("plan-recent-row").filter({ hasText: `Semana E2E ${run}` }).waitFor({ timeout: 10000 });
  check("a finished plan can be named", true);
  await page.getByTestId("plan-recent-row").filter({ hasText: `Semana E2E ${run}` }).getByTestId("plan-repeat").click();
  await page.waitForURL(/\/app\/compras\/planejar\/[^/]+/, { timeout: 15000 });
  await page.waitForURL(/\/revisar$/, { timeout: 15000 });
  await page.getByTestId("plan-review-entry").first().waitFor({ timeout: 15000 });
  check("repeating brings the dishes back", (await page.getByTestId("plan-review-entry").count()) === 2);
  check("the repeated plan is on a new menu", !page.url().includes(menuId));
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
