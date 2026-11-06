#!/usr/bin/env node
/**
 * Menu board journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0065 Slice 2 gate S2.
 * The replica has no AI provider, so it also proves the board works on saved recipes alone, and that an
 * AI failure is said once and leaves the options usable.
 *
 * In the browser, at 360 px: seven snacks within fourteen days with no dates, from saved recipes, counting
 * the taps on the board; a typed search that finds a saved recipe; undo; a mixed menu of 3 lunches and 2
 * dinners; keyboard only (open, choose, Escape, focus back); Slice 3's "Completar", a dish's day and
 * "Rende mais" (asking before passing the count), the group counter, "Distribuir nos dias", and
 * "Trocar"; and the board's title in en and es.
 * Names are unique per run.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/planning-board-journey.mjs [username]
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
const run = Date.now();

async function signIn(page) {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
}

/** Runs in the page: fetch with the session's CSRF token. */
async function api(page, calls) {
  return page.evaluate(async (list) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const out = [];
    for (const call of list) {
      const response = await fetch(call.url, {
        method: call.method ?? "GET",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
        body: call.body ? JSON.stringify(call.body) : undefined,
      });
      out.push(response.status === 204 ? null : await response.json().catch(() => null));
    }
    return out;
  }, calls);
}

async function abandonCurrent(page) {
  const [start] = await api(page, [{ url: "/api/v1/planning/shopping-plans/start" }]);
  if (start?.current) await api(page, [{ url: `/api/v1/planning/shopping-plans/${start.current.sessionId}/abandon`, method: "POST", body: {} }]);
}

/** Sets the four counters on the first screen. */
async function setCounts(page, wanted) {
  for (const [meal, count] of Object.entries(wanted)) {
    const value = page.getByTestId(`plan-count-${meal}`);
    while (Number(await value.textContent()) > count) await page.getByTestId(`plan-count-${meal}-less`).click();
    while (Number(await value.textContent()) < count) await page.getByTestId(`plan-count-${meal}-more`).click();
  }
}

const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 360, height: 740 }, hasTouch: true });
const page = await context.newPage();
page.setDefaultTimeout(20000);
let taps = 0;
const tap = async (locator) => {
  taps += 1;
  await locator.click();
};
try {
  await signIn(page);
  const snacks = Array.from({ length: 8 }, (_, index) => `Lanche E2E ${run} ${index + 1}`);
  const mains = Array.from({ length: 5 }, (_, index) => `Almoco E2E ${run} ${index + 1}`);
  await abandonCurrent(page);
  await api(page, [
    ...snacks.map((name) => ({
      url: "/api/v1/recipes",
      method: "POST",
      body: { content: { name, mealTypes: ["snack"], servings: 1, ingredients: [{ displayName: `Polvilho ${run}`, quantity: 100, unit: "g", optional: false }], stages: [{ name: "Assar", instructions: "Asse.", activeMinutes: 10, passiveMinutes: 15 }] } },
    })),
    ...mains.map((name) => ({
      url: "/api/v1/recipes",
      method: "POST",
      body: { content: { name, mealTypes: ["lunch", "dinner"], servings: 2, ingredients: [{ displayName: `Arroz ${run}`, quantity: 200, unit: "g", optional: false }], stages: [{ name: "Cozinhar", instructions: "Cozinhe.", activeMinutes: 15, passiveMinutes: 10 }] } },
    })),
  ]);

  // First screen: seven snacks within fourteen days, nothing about cooking.
  await page.goto(`${BASE}/app/compras/planejar`, { waitUntil: "networkidle" });
  await page.getByTestId("plan-setup").waitFor();
  check("first screen asks nothing about cooking", (await page.getByTestId("plan-style").count()) === 0 && (await page.getByTestId("plan-priority").count()) === 0);
  await page.getByTestId("plan-days-14").click();
  await setCounts(page, { breakfast: 0, lunch: 0, dinner: 0, snack: 7 });
  // The tab bar covers the bottom of the screen, so the button must end above it.
  const fits = await page.evaluate(() => document.querySelector('[data-testid="plan-begin"]').getBoundingClientRect().bottom <= document.querySelector('[data-testid="production-primary-nav"]').getBoundingClientRect().top);
  check("the first screen's main action is visible without scrolling at 360 × 740", fits);
  check("the main action counts the meals", (await page.getByTestId("plan-begin").textContent()).includes("(7)"));
  await page.getByTestId("plan-begin").click();
  await page.getByTestId("plan-board").waitFor();
  check("the tab bar is hidden while building on a phone", !(await page.getByTestId("production-primary-nav").isVisible()));
  check("the board has only snacks", (await page.locator('[data-testid^="board-group-"]').count()) === 1 && (await page.getByTestId("board-group-snack").count()) === 1);
  check("the period says there is no fixed day", (await page.getByTestId("board-period").textContent()).includes("sem dia fixo"));

  // The board: one sheet, seven choices, one of them by typing.
  await tap(page.getByTestId("board-choose-snack"));
  const sheet = page.getByTestId("meal-sheet");
  await sheet.getByTestId("meal-option").first().waitFor();
  await sheet.getByTestId("meal-sheet-text").fill(snacks[7]);
  const saved = sheet.getByTestId("meal-sheet-saved").filter({ hasText: snacks[7] });
  await saved.waitFor();
  check("typing finds the person's own recipe at once", true);
  await tap(saved);
  await sheet.getByTestId("board-undo").waitFor();
  await tap(sheet.getByTestId("board-undo-action"));
  await page.waitForFunction(() => document.querySelector('[data-testid="meal-sheet"] h2')?.textContent?.includes("faltam 7"), null, { timeout: 15000 });
  check("undo gives the meal back", true);
  await sheet.getByTestId("meal-sheet-text").fill("");
  await tap(saved.or(sheet.getByTestId("meal-option-choose").first()).first());
  for (let chosen = 1; chosen < 7; chosen += 1) {
    await page.waitForFunction((left) => {
      const title = document.querySelector('[data-testid="meal-sheet"] h2')?.textContent ?? "";
      return title.includes(left === 1 ? "falta 1" : `faltam ${left}`);
    }, 7 - chosen, { timeout: 15000 });
    await tap(sheet.getByTestId("meal-option-choose").first());
  }
  await sheet.waitFor({ state: "detached", timeout: 15000 });
  check("the sheet closes on its own when the snacks are complete", true);
  await page.waitForFunction(() => document.querySelector('[data-testid="board-progress"]')?.textContent?.startsWith("7 de 7"), null, { timeout: 15000 });
  const dishes = await page.getByTestId("board-dish").count();
  check("seven snacks are on the board", dishes === 7, `${dishes} dishes`);
  const dated = await page.getByTestId("board-dish").filter({ hasText: /\d{1,2} de \w+/ }).count();
  check("no dish has a day it was not given", dated === 0);
  await tap(page.getByTestId("board-use"));
  await page.getByTestId("menu-page").waitFor({ timeout: 20000 });
  check("seven snacks with no dates take at most 12 taps on the board", taps <= 12, `${taps} taps`);

  // A mixed menu: 3 lunches and 2 dinners, counted apart.
  await page.goto(`${BASE}/app/compras/planejar`, { waitUntil: "networkidle" });
  await page.getByTestId("plan-setup").waitFor();
  await page.getByTestId("plan-days-7").click();
  await setCounts(page, { breakfast: 0, lunch: 3, dinner: 2, snack: 0 });
  await page.getByTestId("plan-begin").click();
  await page.getByTestId("plan-board").waitFor();
  const headings = await page.locator('[data-testid^="board-group-"] h2').allTextContents();
  check("lunch and dinner are counted apart", headings.some((text) => text.includes("Almoços") && text.includes("0 de 3")) && headings.some((text) => text.includes("Jantares") && text.includes("0 de 2")), headings.join(" | "));

  // Keyboard only: open with Enter, choose with Enter, close with Escape, focus comes back.
  await page.getByTestId("board-choose-dinner").focus();
  await page.keyboard.press("Enter");
  await sheet.getByTestId("meal-option-choose").first().waitFor();
  // The replica has no AI: asking for more says so once, and the options stay.
  await sheet.getByTestId("meal-sheet-more").click();
  await sheet.getByTestId("meal-sheet-failure").waitFor({ timeout: 20000 }).catch(() => undefined);
  check("without AI, the sheet says so once and keeps the options", (await sheet.getByTestId("meal-sheet-failure").count()) === 1 && (await sheet.getByTestId("meal-option").count()) > 0);
  await sheet.getByTestId("meal-option-choose").first().focus();
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => document.querySelector('[data-testid="meal-sheet"] h2')?.textContent?.includes("falta 1"), null, { timeout: 15000 });
  await page.keyboard.press("Escape");
  await sheet.waitFor({ state: "detached" });
  const focused = await page.evaluate(() => document.activeElement?.id ?? "");
  check("keyboard: choose, close, and focus returns to the row", focused === "board-choose-dinner", focused);
  const after = await page.locator('[data-testid="board-group-dinner"] h2').textContent();
  check("a dinner counts as a dinner", after.includes("1 de 2"), after);

  // Slice 3: "Completar" proposes one dish per missing meal and changes nothing until confirmed.
  const heading = (meal) => page.locator(`[data-testid="board-group-${meal}"] h2`).textContent();
  await page.getByTestId("board-complete").click();
  const complete = page.getByTestId("complete-sheet");
  await complete.getByTestId("complete-row").first().waitFor();
  const rows = await complete.getByTestId("complete-row").count();
  check("Completar proposes a dish for each of the 4 missing meals", rows === 4, `${rows} rows`);
  check("nothing is chosen before confirming", (await page.getByTestId("board-progress").textContent()).startsWith("1 de 5"));
  await complete.getByTestId("complete-confirm").click();
  await complete.waitFor({ state: "detached", timeout: 15000 });
  await page.waitForFunction(() => document.querySelector('[data-testid="board-progress"]')?.textContent?.startsWith("5 de 5"), null, { timeout: 15000 });
  check("one confirmation fills the menu and says so", (await page.getByTestId("board-notice").textContent()).includes("4 pratos"));

  // A dish's actions: a day, and covering more meals than wanted only after saying yes.
  await page.getByTestId("board-group-lunch").getByTestId("board-dish").first().click();
  const actions = page.getByTestId("board-dish-sheet");
  await actions.locator('[data-testid^="board-dish-day-2"]').nth(1).click();
  await actions.locator('[data-testid^="board-dish-day-2"][aria-pressed="true"]').waitFor({ timeout: 15000 });
  await actions.getByTestId("board-dish-yield-2").click();
  const raise = await actions.getByTestId("board-dish-raise").textContent();
  check("Rende mais asks before passing what was wanted", raise.includes("Você pediu 3") && raise.includes("ficam 4"), raise);
  await actions.getByTestId("board-dish-raise-confirm").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="board-group-lunch"] h2')?.textContent?.includes("4 de 4"), null, { timeout: 15000 });
  check("after yes, the dish covers two lunches and the count follows", true);
  await page.keyboard.press("Escape");
  await actions.waitFor({ state: "detached" });
  check("a dish given a day shows it on the board", (await page.getByTestId("board-dish").filter({ hasText: /\d{1,2} de \w+/ }).count()) === 1);

  // The group counter: one more dinner opens one meal again.
  await page.getByTestId("board-count-dinner-more").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="board-group-dinner"] h2')?.textContent?.includes("2 de 3"), null, { timeout: 15000 });
  // The heading moves at once; the bar follows when the server has saved the count.
  await page.getByTestId("board-complete").waitFor({ timeout: 15000 });
  check("the group counter changes what is wanted", true, await heading("dinner"));

  // "Distribuir nos dias" dates every dish, and "Tirar os dias" takes the days away.
  const dishCount = await page.getByTestId("board-dish").count();
  await page.getByTestId("board-spread-action").click();
  await page.waitForFunction((total) => [...document.querySelectorAll('[data-testid="board-dish"]')].filter((dish) => /\d{1,2} de \w+/.test(dish.textContent ?? "")).length === total, dishCount, { timeout: 15000 });
  check("spreading gives every dish a day", true);
  await page.getByTestId("board-spread-clear").click();
  await page.waitForFunction(() => ![...document.querySelectorAll('[data-testid="board-dish"]')].some((dish) => /\d{1,2} de \w+/.test(dish.textContent ?? "")), null, { timeout: 15000 });
  check("and the days can be taken away again", true);

  // "Trocar" removes the dish and opens its meal's options at once.
  await page.getByTestId("board-group-dinner").getByTestId("board-dish").first().click();
  await actions.getByTestId("board-dish-swap").click();
  await sheet.waitFor();
  check("Trocar opens the dinner options", (await sheet.locator("h2").textContent()).includes("Jantar"), await sheet.locator("h2").textContent());
  await page.keyboard.press("Escape");
  await sheet.waitFor({ state: "detached" });

  // Other languages read the board in their own words.
  const sessionId = page.url().split("/").pop();
  for (const [locale, title] of [["en-US", "Your menu"], ["es-ES", "Tu menú"]]) {
    const other = await browser.newContext({ ignoreHTTPSErrors: true, locale, viewport: { width: 360, height: 740 } });
    await other.addCookies(await context.cookies());
    const otherPage = await other.newPage();
    await otherPage.goto(`${BASE}/app/compras/planejar/${sessionId}`, { waitUntil: "networkidle" });
    const heading = await otherPage.getByTestId("plan-board").locator("h1").textContent().catch(() => "");
    check(`the board in ${locale}`, heading.includes(title), heading);
    await other.close();
  }
  await abandonCurrent(page);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
