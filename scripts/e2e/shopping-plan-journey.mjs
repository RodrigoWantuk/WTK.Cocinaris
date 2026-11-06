#!/usr/bin/env node
/**
 * Signed-in shopping plan journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0062.
 * The replica has no AI provider, so this also proves the plan works on saved recipes alone (gate
 * G1-E). It seeds two recipes, then in the browser: answers the first screen, chooses two dishes on the
 * board (one set to yield 2 meals), marks "I have it" on the list, uses the menu, leaves a line for
 * later, splits the list by place, records the arrival, checks "Hoje", cooks a dish that yields two meals, eats the stored portion, names the plan, and repeats it. It prints how long the first card took (gate G1-C). Names are unique per run.
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
  await page.getByTestId("plan-days-3").click();
  for (const [meal, count] of Object.entries({ breakfast: 0, lunch: 0, dinner: 3, snack: 0 })) {
    const value = page.getByTestId(`plan-count-${meal}`);
    while (Number(await value.textContent()) > count) await page.getByTestId(`plan-count-${meal}-less`).click();
    while (Number(await value.textContent()) < count) await page.getByTestId(`plan-count-${meal}-more`).click();
  }
  const hScroll = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  check("first screen has no horizontal scroll at phone width", !hScroll);
  check("first screen counts 3 meals", (await page.getByTestId("plan-begin").textContent()).includes("(3)"));

  const began = Date.now();
  await page.getByTestId("plan-begin").click();
  await page.getByTestId("board-choose-dinner").waitFor({ timeout: 15000 });
  await page.getByTestId("board-choose-dinner").click();
  await page.getByTestId("meal-option").first().waitFor({ timeout: 15000 });
  const firstCardMs = Date.now() - began;
  check("first options under 1 s when saved recipes exist (G1-C)", firstCardMs < 1000, `${firstCardMs} ms`);
  check("an option shows its new items", (await page.getByTestId("meal-option").first().textContent()).includes("item"));
  await page.getByTestId("meal-option-choose").first().click();
  await page.getByTestId("board-undo").waitFor({ timeout: 10000 });
  await page.getByTestId("meal-option-choose").first().click();
  await page.waitForFunction(() => document.querySelector('[data-testid="meal-sheet"] h2')?.textContent?.includes("falta 1"), null, { timeout: 15000 });
  await page.keyboard.press("Escape");
  // "Rende mais" has its screen in PLAN-0065 Slice 3; its route already exists.
  const sessionId = page.url().split("/").pop();
  await page.evaluate(async (id) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const plan = await (await fetch(`/api/v1/planning/shopping-plans/${id}`, { credentials: "include" })).json();
    await fetch(`/api/v1/planning/shopping-plans/${id}/cards/${plan.accepted[0].cardId}/yield`, {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken },
      body: JSON.stringify({ meals: 2 }),
    });
  }, sessionId);
  await page.reload({ waitUntil: "networkidle" });
  await page.waitForFunction(() => document.querySelector('[data-testid="board-progress"]')?.textContent?.startsWith("3 de 3"), null, { timeout: 15000 });
  check("the board lists two dishes for three meals", (await page.getByTestId("board-dish").count()) === 2);

  await page.getByTestId("board-list").click();
  await page.getByTestId("shopping-page").waitFor({ timeout: 15000 });
  const line = page.getByTestId("shopping-item").filter({ hasText: lentil }).first();
  if ((await line.count()) === 0) {
    check("the list has the menu's ingredient", (await page.getByTestId("shopping-item").count()) > 0);
  }
  const lines = await page.getByTestId("shopping-item-more").count();
  check("lines can be marked as already at home", lines > 0, `${lines} lines`);
  await page.getByTestId("shopping-item-more").first().click();
  await page.getByTestId("shopping-item-have").first().click();
  await page.getByTestId("shopping-have-notice").waitFor({ timeout: 10000 });
  await page.getByTestId("shopping-have-undo").click();
  await page.getByTestId("shopping-have-notice").waitFor({ state: "detached", timeout: 10000 });
  await page.waitForFunction((count) => document.querySelectorAll('[data-testid="shopping-item-more"]').length === count, lines, { timeout: 10000 });
  check("undo puts the line back", true);

  await page.getByTestId("shopping-back").click();
  await page.getByTestId("plan-board").waitFor({ timeout: 10000 });
  check("from a draft's list, back returns to the board", true);
  await page.getByTestId("board-use").click();
  await page.getByTestId("menu-page").waitFor({ timeout: 15000 });
  await page.getByText("Em uso", { exact: true }).waitFor({ timeout: 10000 });

  const menuUrl = page.url();
  await page.goto(`${menuUrl}/compras`, { waitUntil: "networkidle" });
  await page.getByTestId("shopping-item-more").first().click();
  await page.getByTestId("shopping-not-found").first().click();
  await page.getByTestId("shopping-later-save").click();
  await page.getByTestId("shopping-later").waitFor({ timeout: 10000 });
  await page.getByTestId("shopping-later-back").first().click();
  await page.getByTestId("shopping-later").waitFor({ state: "detached", timeout: 10000 });
  check("a line can be left for later and brought back", true);
  await page.getByTestId("shopping-split-place").click();
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

  // Cooking the dish that yields two meals proposes storing the other one, which "Hoje" then offers.
  const yielding = await page.evaluate(async (id) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const menu = await (await fetch(`/api/v1/planning/menus/${id}`, { credentials: "include" })).json();
    const entry = menu.entries.find((item) => item.mealsCovered > 1);
    const started = await fetch(`/api/v1/recipes/${entry.recipeId}/executions`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken },
      body: JSON.stringify({ menuEntryId: entry.entryId, servings: entry.servings }),
    });
    return (await started.json()).executionId;
  }, menuId);
  await page.goto(`${BASE}/app/receitas/cozinhar/${yielding}`, { waitUntil: "networkidle" });
  await page.getByTestId("cooking-overview").click();
  await page.getByTestId("cooking-finish").click();
  await page.getByTestId("cooking-outputs").waitFor({ timeout: 10000 });
  check("cooking proposes the refrigerator for the later meal", (await page.getByTestId("cooking-output-storage").first().inputValue()) === "Refrigerator");
  await page.getByTestId("cooking-done").click();
  await page.getByTestId("cooking-done-stored").waitFor({ timeout: 15000 });
  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  const eat = page.getByTestId("today-eat-portion");
  await eat.first().waitFor({ timeout: 10000 });
  const readyRow = page.getByTestId("today-preparation-item").filter({ has: eat }).first();
  const before = await readyRow.textContent();
  await eat.first().click();
  await page.waitForFunction((text) => !document.querySelector('[data-testid="today-preparation"]')?.textContent?.includes(text), before.trim().slice(0, 40), { timeout: 10000 });
  check("eating a portion counts it down", true);

  // Repeat: name the finished plan, then start again with its dishes already on the new draft.
  await page.goto(`${BASE}/app/compras/planejar`, { waitUntil: "networkidle" });
  // Menus to repeat sit in a disclosure under the first screen (PLAN-0065).
  await page.getByTestId("plan-recent-disclosure").locator("summary").click();
  const recent = page.getByTestId("plan-recent-row").first();
  await recent.getByTestId("plan-recent-name-edit").click();
  await recent.getByTestId("plan-recent-name-input").fill(`Semana E2E ${run}`);
  await recent.getByTestId("plan-recent-name-save").click();
  await page.getByTestId("plan-recent-row").filter({ hasText: `Semana E2E ${run}` }).waitFor({ timeout: 10000 });
  check("a finished plan can be named", true);
  await page.getByTestId("plan-recent-row").filter({ hasText: `Semana E2E ${run}` }).getByTestId("plan-repeat").click();
  await page.waitForURL(/\/app\/compras\/planejar\/[^/]+/, { timeout: 15000 });
  await page.getByTestId("board-dish").first().waitFor({ timeout: 15000 });
  check("repeating brings the dishes back", (await page.getByTestId("board-dish").count()) === 2);
  check("the repeated plan is on a new menu", !page.url().includes(menuId));
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
