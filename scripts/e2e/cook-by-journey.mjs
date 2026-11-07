#!/usr/bin/env node
/**
 * "Faça até" on "Hoje", against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0062 Slice 4.
 * It seeds a menu in use with one dish that has no day and reserves a refrigerated lot printed for
 * tomorrow, then checks the reminder on "Hoje", that "Trocar de prato" opens the swap on the menu, and
 * that "Deixar como está" hides it. With the storage table off, "Congelar hoje" is not offered.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/cook-by-journey.mjs [username]
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

const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 360, height: 740 } })).newPage();
page.setDefaultTimeout(20000);
// A failed request explains a failed step without opening the logs.
page.on("response", async (response) => {
  if (response.url().includes("/api/") && response.status() >= 400 && response.status() !== 401)
    console.log(`  response ${response.status()} ${new URL(response.url()).pathname}: ${(await response.text().catch(() => "")).slice(0, 160)}`);
});
page.on("requestfailed", (request) => {
  if (request.url().includes("/api/")) console.log(`  request failed ${new URL(request.url()).pathname}: ${request.failure()?.errorText}`);
});
page.on("console", (message) => {
  if (message.type() === "error") console.log(`  console: ${message.text().slice(0, 200)}`);
});
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
  await page.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });

  const run = Date.now();
  const fish = `Peixe E2E ${run}`;
  const dish = `Moqueca E2E ${run}`;
  const seeded = await page.evaluate(async ({ fish, dish }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) => (await fetch(url, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body ?? {}),
    })).json();
    const pad = (value) => String(value).padStart(2, "0");
    const iso = (date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    const today = new Date();
    const tomorrow = new Date(Date.now() + 86400000);
    await post("/api/v1/inventory/lots", { productName: fish, quantity: { measuredValue: 300, unit: "Gram" }, storageLocation: "Refrigerator", printedExpirationDate: iso(tomorrow) });
    const recipe = await post("/api/v1/recipes", { content: {
      name: dish, mealTypes: ["dinner"], servings: 2,
      ingredients: [{ displayName: fish, quantity: 300, unit: "g", optional: false }],
      stages: [{ name: "Cozinhar", instructions: "Cozinhe o peixe.", activeMinutes: 20, passiveMinutes: 10 }],
    } });
    const menu = await post("/api/v1/planning/menus", { name: `Faça até ${dish}`, startDate: iso(today), endDate: iso(new Date(Date.now() + 5 * 86400000)) });
    const added = await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: recipe.recipeId, mealType: "dinner" });
    await post(`/api/v1/planning/menus/${menu.menuId}/acceptance`);
    return { menuId: menu.menuId, entryId: added.entries[0].entryId };
  }, { fish, dish });

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  const row = page.getByTestId("today-preparation-item").filter({ hasText: dish });
  await row.waitFor({ timeout: 15000 });
  const text = (await row.textContent()) ?? "";
  check("Hoje says by when to cook the dish with no day", text.includes(`Fazer ${dish} até amanhã`), text.slice(0, 120));
  check("it says why, and that it is an estimate", text.includes(fish) && text.includes("estimativa"));
  check("with the storage table off, nothing offers to freeze", (await row.getByTestId("today-cook-by-freeze").count()) === 0);
  await row.getByTestId("today-cook-by-swap").click();
  await page.getByTestId("meal-sheet").waitFor({ timeout: 15000 });
  check("Trocar de prato opens the dinner options on the menu", page.url().endsWith(`/app/cardapio/${seeded.menuId}`));
  await page.keyboard.press("Escape");
  await page.getByTestId("menu-swap-undo-action").click();
  await page.waitForFunction((name) => [...document.querySelectorAll('[data-testid="menu-entry"]')].some((node) => node.textContent?.includes(name)), dish, { timeout: 15000 });
  check("the swap can be undone", true);

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  await row.waitFor({ timeout: 15000 });
  await row.getByTestId("today-cook-by-leave").click();
  await row.waitFor({ state: "detached", timeout: 15000 });
  check("Deixar como está hides it", true);
  await page.reload({ waitUntil: "networkidle" });
  check("and it stays hidden", (await page.getByTestId("today-preparation-item").filter({ hasText: dish }).count()) === 0);

  await page.evaluate(async (menuId) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    await fetch(`/api/v1/planning/menus/${menuId}/archive`, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken }, body: "{}" });
  }, seeded.menuId);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
