#!/usr/bin/env node
/**
 * Signed-in reservation and protection journey against the proxy replica (scripts/e2e/proxy-replica.sh),
 * PLAN-0029 Phase 3. Seeds a 500 g lot, two recipes that need 300 g each, and an accepted menu with
 * both, through the API. Then in the browser: the menu shows what the second meal lacks, the lot
 * page lists the reservation and protects 100 g, the menu shows the larger shortfall, and cooking
 * the second recipe off the menu warns that it would leave a planned meal short.
 * Names are unique per run.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/reservations-journey.mjs [username]
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
const day1 = iso(new Date(Date.now() + 86400000));
const day2 = iso(new Date(Date.now() + 2 * 86400000));

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
  const rice = `Arroz E2E ${run}`;
  const seeded = await page.evaluate(async ({ rice, run, day1, day2 }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) => (await fetch(url, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body ?? {}),
    })).json();
    const recipe = (name) => post("/api/v1/recipes", { content: {
      name, mealTypes: ["dinner"], servings: 2,
      ingredients: [{ displayName: rice, quantity: 300, unit: "g", optional: false }],
      stages: [{ name: "Cozinhar", instructions: "Cozinhe o arroz.", activeMinutes: 10, passiveMinutes: 20 }],
    } });
    const lot = await post("/api/v1/inventory/lots", { productName: rice, quantity: { measuredValue: 500, unit: "Gram" }, storageLocation: "Pantry" });
    const risotto = await recipe(`Risoto E2E ${run}`);
    const pudding = await recipe(`Arroz doce E2E ${run}`);
    const menu = await post("/api/v1/planning/menus", { name: `Reservas E2E ${run}`, startDate: day1, endDate: day2 });
    await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: risotto.recipeId, plannedDate: day1, mealType: "dinner" });
    await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: pudding.recipeId, plannedDate: day2, mealType: "dinner" });
    await post(`/api/v1/planning/menus/${menu.menuId}/acceptance`);
    return { lotId: lot.lotId, menuId: menu.menuId, puddingId: pudding.recipeId };
  }, { rice, run, day1, day2 });

  const readiness = async (day) => (await page.getByTestId(`menu-day-${day}`).getByTestId("menu-entry-readiness").textContent()) ?? "";
  await page.goto(`${BASE}/app/cardapio/${seeded.menuId}`, { waitUntil: "networkidle" });
  check("first meal is covered", (await readiness(day1)).includes("reservados"), await readiness(day1));
  check("second meal lacks 100 g", (await readiness(day2)).includes("100 g"), await readiness(day2));

  await page.goto(`${BASE}/app/despensa/${seeded.lotId}`, { waitUntil: "networkidle" });
  await page.getByTestId("lot-commitments-meal").first().waitFor({ timeout: 10000 });
  check("lot page lists the reservations", (await page.getByTestId("lot-commitments-meal").count()) === 2);
  await page.getByTestId("lot-protect-minimum").check();
  await page.getByTestId("lot-protect-quantity").fill("100");
  await page.getByTestId("lot-protect-save").click();
  await page.getByText("100 g livres", { exact: false }).waitFor({ timeout: 10000 }).catch(() => undefined);

  await page.goto(`${BASE}/app/cardapio/${seeded.menuId}`, { waitUntil: "networkidle" });
  check("protection widens the shortfall to 200 g", (await readiness(day2)).includes("200 g"), await readiness(day2));

  await page.goto(`${BASE}/app/receitas/${seeded.puddingId}`, { waitUntil: "networkidle" });
  await page.getByTestId("recipes-detail-cook").click();
  await page.waitForURL(/\/app\/receitas\/cozinhar\//, { timeout: 15000 });
  await page.getByTestId("cooking-finish").click();
  const line = page.getByTestId("cooking-review-line").first();
  const include = line.getByRole("checkbox");
  if (!(await include.isChecked())) await include.check();
  await line.getByRole("combobox").selectOption(seeded.lotId);
  await line.getByRole("textbox").fill("300");
  const notice = (await line.getByTestId("cooking-review-commitment").textContent()) ?? "";
  check("cooking off the menu warns about the planned meals", notice.includes("Risoto E2E") && notice.includes("sem o suficiente"), notice.slice(0, 120));
  await page.getByRole("button", { name: "Voltar para a receita" }).click();
  page.once("dialog", (dialog) => void dialog.accept());
  await page.getByTestId("cooking-abandon").click();
  await page.getByTestId("cooking-finished").waitFor({ timeout: 15000 });
  const after = await page.evaluate(async (id) => (await (await fetch(`/api/v1/inventory/lots/${id}`, { credentials: "include" })).json()).quantity.measuredValue, seeded.lotId);
  check("nothing changed physically", after === 500, `measured ${after}`);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
