#!/usr/bin/env node
/**
 * Signed-in menu and shopping journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0029.
 * Seeds a pantry lot and a recipe through the API, then in the browser: creates a menu, adds the
 * recipe to today and tomorrow, accepts the menu, finds today's meal on "Hoje", checks the shopping
 * list, records a purchase, cooks the meal from the menu, and sees it marked as cooked.
 * Names are unique per run, so earlier runs never affect the checks.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/planning-journey.mjs [username]
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

const pad = (value) => String(value).padStart(2, "0");
const iso = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
const today = iso(new Date());
const tomorrow = iso(new Date(Date.now() + 86400000));

const results = [];
const check = (name, ok, detail = "") => results.push({ name, ok, detail });

const browser = await chromium.launch();
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR" })).newPage();
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const run = Date.now();
  const beans = `Feijão E2E ${run}`;
  const recipeName = `Feijoada E2E ${run}`;
  await page.evaluate(async ({ beans, recipeName }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = (url, body) => fetch(url, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body),
    });
    await post("/api/v1/inventory/lots", { productName: beans, quantity: { measuredValue: 300, unit: "Gram" }, storageLocation: "Pantry" });
    await post("/api/v1/recipes", { content: {
      name: recipeName, mealTypes: ["dinner"], servings: 2,
      ingredients: [{ displayName: beans, quantity: 500, unit: "g", optional: false }],
      stages: [{ name: "Cozinhar", instructions: "Cozinhe o feijão.", activeMinutes: 10, passiveMinutes: 30 }],
    } });
  }, { beans, recipeName });

  await page.goto(`${BASE}/app/cardapio`, { waitUntil: "networkidle" });
  check("nav shows Cardápio", (await page.getByTestId("production-nav-cardapio").count()) === 1);
  await page.getByTestId("menu-create-name").fill(`Semana E2E ${run}`);
  await page.getByTestId("menu-create").click();
  await page.getByTestId("menu-page").waitFor({ timeout: 15000 });

  for (const day of [today, tomorrow]) {
    const group = page.getByTestId(`menu-day-${day}`);
    await group.getByTestId(`menu-add-${day}`).click();
    await group.getByRole("searchbox").fill(recipeName);
    await group.getByLabel(recipeName).check();
    await group.getByTestId("menu-meal-select").selectOption("dinner");
    await group.getByTestId("menu-add-submit").click();
    await group.getByTestId("menu-entry").waitFor({ timeout: 10000 });
  }
  check("two dinners planned", (await page.getByTestId("menu-entry").count()) === 2);
  await page.getByTestId("menu-accept").click();
  await page.getByText("Em uso", { exact: true }).waitFor({ timeout: 10000 });
  const menuUrl = page.url();

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  const todayEntry = page.getByTestId("today-menu-entry").filter({ hasText: recipeName });
  check("today's dinner is on Hoje", (await todayEntry.count()) === 1);

  await page.goto(`${menuUrl}/compras`, { waitUntil: "networkidle" });
  const line = page.getByTestId("shopping-required").getByTestId("shopping-item").filter({ hasText: beans });
  await line.waitFor({ timeout: 10000 });
  check("shopping list asks for 700 g", (await line.textContent()).includes("700 g"), (await line.textContent()).slice(0, 80));
  await line.getByTestId("shopping-item-buy").click();
  await line.getByTestId("shopping-buy-quantity").fill("1000");
  await line.getByTestId("shopping-buy-confirm").click();
  await page.getByTestId("shopping-available").getByText(beans).waitFor({ timeout: 10000 });
  check("purchase covers the menu", true);

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  await todayEntry.getByTestId("today-menu-cook").click();
  await page.waitForURL(/\/app\/receitas\/cozinhar\//, { timeout: 15000 });
  await page.getByTestId("cooking-finish").click();
  const include = page.getByTestId("cooking-review-line").first().getByRole("checkbox");
  if (await include.isChecked()) await include.uncheck();
  await page.getByTestId("cooking-confirm").click();
  await page.getByTestId("cooking-finished").waitFor({ timeout: 15000 });

  await page.goto(menuUrl, { waitUntil: "networkidle" });
  const cooked = await page.getByTestId(`menu-day-${today}`).getByTestId("menu-entry").textContent();
  check("today's meal is marked cooked", cooked.includes("Feita"), cooked.slice(0, 80));
  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  check("cooked meal left Hoje", (await page.getByTestId("today-menu-entry").filter({ hasText: recipeName }).count()) === 0);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
