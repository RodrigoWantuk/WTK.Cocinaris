#!/usr/bin/env node
/**
 * The way into planning against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0063. It seeds
 * an accepted menu with something to buy, then checks that the tab, the central button, and the home
 * tile reach one page, which leads with the next step, and that every planning page lights the tab.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/planning-entry-journey.mjs [username]
 * Never prints passwords or cookies.
 */
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium } = createRequire(path.join(repo, "apps/frontend/package.json"))("playwright");
const BASE = "https://localhost:7443";
const username = process.argv[2] ?? "inventory-user-b";
const password = process.env.COCINARIS_SMOKE_PASSWORD_A;
if (!password) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A is required.\n");
  process.exit(2);
}

const results = [];
const check = (name, ok, detail = "") => {
  results.push(ok);
  console.log(`[${ok ? "Passed" : "Failed"}] ${name}${detail ? ` — ${detail}` : ""}`);
};

// A service worker refuses a certificate error, and the replica uses a local certificate.
const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 800 } });
const page = await context.newPage();
page.setDefaultTimeout(20000);
const tabLit = () => page.getByTestId("production-nav-planejar").getAttribute("aria-current");
const hub = () => page.getByTestId("planning-hub").waitFor();
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const menuId = await page.evaluate(async () => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) =>
      (
        await fetch(url, {
          method: "POST",
          credentials: "include",
          headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
          body: JSON.stringify(body),
        })
      ).json();
    const now = new Date();
    const iso = (offset) => new Date(now.getFullYear(), now.getMonth(), now.getDate() + offset, 12).toISOString().slice(0, 10);
    const name = `Entrada E2E ${Date.now()}`;
    // The page talks about the menu in use, so an earlier run's menu would take its place.
    for (const earlier of await (await fetch("/api/v1/planning/menus", { credentials: "include" })).json()) {
      await post(`/api/v1/planning/menus/${earlier.menuId}/archive`, {});
    }
    const recipe = await post("/api/v1/recipes", {
      content: {
        name,
        mealTypes: ["dinner"],
        servings: 2,
        ingredients: [{ displayName: `Item ${name}`, quantity: 2, unit: "unit", optional: false }],
        stages: [{ name: "Fazer", instructions: "Faça.", activeMinutes: 5, passiveMinutes: 0 }],
      },
    });
    if (!recipe.recipeId) throw new Error("The recipe was not created.");
    const menu = await post("/api/v1/planning/menus", { startDate: iso(0), endDate: iso(2) });
    const filled = await post(`/api/v1/planning/menus/${menu.menuId}/entries`, { recipeId: recipe.recipeId, plannedDate: iso(1), mealType: "dinner" });
    const accepted = await post(`/api/v1/planning/menus/${menu.menuId}/acceptance`, {});
    if (!filled.menuId || accepted.status !== "accepted") throw new Error(`The menu was not accepted: ${accepted.status}.`);
    return menu.menuId;
  });
  check("seeded an accepted menu", Boolean(menuId));

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "domcontentloaded" });
  const tile = page.getByTestId("today-quick-plan");
  await tile.waitFor();
  const hinted = await page
    .waitForFunction(() => /para comprar/.test(document.querySelector('[data-testid="today-quick-plan"]')?.textContent ?? ""))
    .then(() => true, () => false);
  check("the home tile hints at the next step", hinted, (await tile.innerText()).replace(/\s+/g, " "));
  await tile.click();
  await hub();
  check("the home tile opens the planning page", new URL(page.url()).pathname === "/app/compras");

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-nav-add").click();
  await page.getByTestId("quick-action-plan-shopping").click();
  await hub();
  check("the central button opens the same page", new URL(page.url()).pathname === "/app/compras");

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "domcontentloaded" });
  const tabLabel = await page.getByTestId("production-nav-planejar").innerText();
  check("the tab is called Planejar", tabLabel.trim() === "Planejar", tabLabel.trim());
  await page.getByTestId("production-nav-planejar").click();
  await hub();
  check("the tab opens the same page, not a list", (await page.getByTestId("shopping-page").count()) === 0);
  check("the page is called Planejamento", (await page.getByRole("heading", { level: 1 }).innerText()) === "Planejamento");
  check("the tab is lit on the page", (await tabLit()) === "page");

  // The list was never opened here, and it must already be on the device for the market.
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.waitForTimeout(800);
  await page.reload({ waitUntil: "networkidle" });
  await hub();
  await page.waitForTimeout(800);
  await context.setOffline(true);
  await page.goto(`${BASE}/app/compras`, { waitUntil: "domcontentloaded" });
  const saved = await page.getByTestId("shopping-page").waitFor({ timeout: 15000 }).then(() => true, () => false);
  check("the list opens offline without ever having been opened", saved);
  await context.setOffline(false);
  await page.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });
  await hub();

  const main = page.getByTestId("planning-hub-buy");
  check("the main action is what to buy, with a count", /^Ver o que comprar \(\d+\)$/.test((await main.innerText()).trim()), (await main.innerText()).trim());
  await page.setViewportSize({ width: 320, height: 640 });
  check("320 px has no horizontal scroll", await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth));
  await page.setViewportSize({ width: 390, height: 800 });

  await main.click();
  await page.getByTestId("shopping-page").waitFor();
  check("the list is called O que comprar", (await page.getByRole("heading", { level: 1 }).innerText()) === "O que comprar");
  check("the tab stays lit on the list", (await tabLit()) === "page");

  await page.goBack();
  await hub();
  await page.getByTestId("planning-hub-menu-link").click();
  await page.getByTestId("menu-shopping-link").waitFor();
  check("the tab stays lit on the menu", (await tabLit()) === "page");
  check("the menu keeps its name", /Cardápio/.test(await page.title()), await page.title());
  check("no screen says Lista de compras", !/Lista de compras/.test(await page.locator("body").innerText()));
} catch (error) {
  check("journey finished", false, String(error).split("\n")[0]);
} finally {
  await browser.close();
}

const failed = results.filter((ok) => !ok).length;
console.log(failed === 0 ? `All ${results.length} checks passed.` : `${failed} of ${results.length} checks failed.`);
process.exit(failed === 0 ? 0 : 1);
