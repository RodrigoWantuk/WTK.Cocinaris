#!/usr/bin/env node
/**
 * Accessibility, theme, language, and zoom pass over the planning screens, against the proxy replica
 * (scripts/e2e/proxy-replica.sh), PLAN-0065 Slice 5. It seeds a menu in use and a menu being built,
 * then opens the board, the options sheet, the menu in use, a dish's sheet, the list, and the hub:
 *
 * - axe-core (WCAG 2.0, 2.1, and 2.2 A and AA rules) in pt-BR with light and dark themes, and in en and
 *   es; a serious or critical violation fails the pass;
 * - in every language and theme, no copy key may show instead of words;
 * - 200% zoom, emulated as a 640 × 360 CSS viewport at a device scale of 2 (a 1280 × 720 window at
 *   200%): no sideways scrolling.
 *
 * Automated checks do not replace VoiceOver or TalkBack on a phone, or a person looking at the screens.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/planning-a11y-audit.mjs [username]
 * Never prints passwords or cookies.
 */
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const require = createRequire(path.join(repo, "apps/frontend/package.json"));
const { chromium } = require("playwright");
const AXE = require.resolve("axe-core/axe.min.js");
const BASE = "https://localhost:7443";
const OUT = path.join(repo, "artifacts/planning-a11y");
const username = process.argv[2] ?? "inventory-user-b";
const password = process.env.COCINARIS_SMOKE_PASSWORD_A;
if (!password) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A is required.\n");
  process.exit(2);
}
mkdirSync(OUT, { recursive: true });

const failures = [];
const notes = [];
let checked = 0;

/** A copy key on screen means a missing translation. */
const rawKeys = () => {
  const found = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent?.trim() ?? "";
    if (/^(menuBuild|menuUse|shoppingPlan|planning|planningHub|shopping|today|offline|arrival)\.[\w.]+$/.test(text)) found.add(text);
  }
  return [...found];
};

async function signIn(page) {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
}

/** One menu in use and one being built, from the person's own recipes, with no AI. */
async function seed(page) {
  return page.evaluate(async () => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const get = async (url) => {
      try {
        return await (await fetch(url, { credentials: "include" })).json();
      } catch (error) {
        throw new Error(`GET ${url}: ${error}`);
      }
    };
    const post = async (url, body) => {
      const response = await fetch(url, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify(body),
      }).catch((error) => {
        throw new Error(`POST ${url}: ${error}`);
      });
      if (response.status === 429) throw new Error(`Rate limited on ${url}; wait a minute between runs.`);
      return response.json().catch(() => ({}));
    };
    const base = "/api/v1/planning/shopping-plans";
    const start = await get(`${base}/start`);
    if (start.current) await post(`${base}/${start.current.sessionId}/abandon`, {});
    for (const menu of await get("/api/v1/planning/menus")) {
      await post(`/api/v1/planning/menus/${menu.menuId}/archive`, {});
    }
    const found = await get("/api/v1/recipes?search=A11y");
    let recipes = (found.items ?? found).map((item) => item.recipeId);
    if (recipes.length < 3) {
      for (const name of ["A11y Feijoada leve", "A11y Salada de grão-de-bico", "A11y Macarrão ao sugo"]) {
        const made = await post("/api/v1/recipes", {
          content: {
            name,
            mealTypes: ["dinner"],
            servings: 2,
            ingredients: [{ displayName: `${name} base`, quantity: 300, unit: "g", optional: false }],
            stages: [{ name: "Preparar", instructions: "Prepare tudo.", activeMinutes: 15, passiveMinutes: 0 }],
          },
        });
        recipes.push(made.recipeId);
      }
      recipes = recipes.filter(Boolean);
    }
    const today = new Date();
    const iso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(today.getDate()).padStart(2, "0")}`;
    const plan = async (dinners, chosen) => {
      const made = await post(base, { today: iso, days: 5, mealCounts: { breakfast: 0, lunch: 0, dinner: dinners, snack: 0 }, people: 2 });
      for (const recipeId of recipes.slice(0, chosen)) await post(`${base}/${made.sessionId}/recipes/${recipeId}/choice`, { mealType: "dinner" });
      return made;
    };
    const inUse = await plan(3, 2);
    await post(`${base}/${inUse.sessionId}/acceptance`, {});
    const building = await plan(2, 1);
    return { menuId: inUse.menuId, sessionId: building.sessionId };
  });
}

/** The screens, each opened from scratch so a sheet never leaks into the next. */
const screens = (seeded) => [
  { name: "board", path: `/app/compras/planejar/${seeded.sessionId}`, ready: "plan-board" },
  { name: "board-sheet", path: `/app/compras/planejar/${seeded.sessionId}`, ready: "plan-board", open: "board-choose-dinner", sheet: "meal-sheet" },
  { name: "menu", path: `/app/cardapio/${seeded.menuId}`, ready: "menu-page" },
  { name: "menu-dish", path: `/app/cardapio/${seeded.menuId}`, ready: "menu-page", open: "menu-entry-open", sheet: "menu-entry-sheet" },
  { name: "list", path: `/app/cardapio/${seeded.menuId}/compras`, ready: "shopping-page" },
  { name: "hub", path: "/app/compras", ready: "planning-hub" },
];

async function open(page, screen) {
  await page.goto(`${BASE}${screen.path}`, { waitUntil: "networkidle" });
  await page.getByTestId(screen.ready).waitFor({ timeout: 20000 });
  if (screen.open) {
    await page.getByTestId(screen.open).first().click();
    await page.getByTestId(screen.sheet).waitFor({ timeout: 20000 });
    // The sheet slides in; axe reads colors after it settles.
    await page.waitForTimeout(600);
  }
}

async function axe(page) {
  await page.addScriptTag({ path: AXE });
  return page.evaluate(async () => {
    const result = await window.axe.run(document, {
      runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
      resultTypes: ["violations"],
    });
    return result.violations.map((item) => ({ id: item.id, impact: item.impact, count: item.nodes.length, target: String(item.nodes[0]?.target?.[0] ?? "").slice(0, 80) }));
  });
}

const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
let crashed = false;
let seeded = null;
try {
  const first = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 844 }, hasTouch: true });
  const page = await first.newPage();
  await signIn(page);
  // The app finishes its own start-up first, so no navigation interrupts the seeding requests.
  await page.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });
  seeded = await seed(page);
  const cookies = await first.cookies();
  await first.close();

  const passes = [
    { locale: "pt-BR", scheme: "light", axe: true },
    { locale: "pt-BR", scheme: "dark", axe: true },
    { locale: "en-US", scheme: "light", axe: true },
    { locale: "es-ES", scheme: "dark", axe: true },
  ];
  for (const pass of passes) {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: pass.locale, colorScheme: pass.scheme, viewport: { width: 390, height: 844 }, hasTouch: true });
    await context.addCookies(cookies);
    const view = await context.newPage();
    view.setDefaultTimeout(20000);
    for (const screen of screens(seeded)) {
      const label = `${pass.locale} ${pass.scheme} ${screen.name}`;
      await open(view, screen);
      checked += 1;
      const keys = await view.evaluate(rawKeys);
      for (const key of keys) failures.push(`${label}: copy key on screen: ${key}`);
      if (pass.axe) {
        for (const violation of await axe(view)) {
          const line = `${label}: ${violation.impact} ${violation.id} ×${violation.count} (${violation.target})`;
          (violation.impact === "serious" || violation.impact === "critical" ? failures : notes).push(line);
        }
      }
      await view.screenshot({ path: path.join(OUT, `${pass.locale}-${pass.scheme}-${screen.name}.png`), fullPage: true });
      console.log(`[checked] ${label}`);
    }
    await context.close();
  }

  // 200% zoom of a 1280 × 720 window.
  const zoomed = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 640, height: 360 }, deviceScaleFactor: 2 });
  await zoomed.addCookies(cookies);
  const view = await zoomed.newPage();
  view.setDefaultTimeout(20000);
  for (const screen of screens(seeded)) {
    await open(view, screen);
    checked += 1;
    const overflow = await view.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (overflow > 0) failures.push(`200% ${screen.name}: sideways scroll of ${overflow} px`);
    await view.screenshot({ path: path.join(OUT, `zoom200-${screen.name}.png`), fullPage: true });
    console.log(`[checked] 200% ${screen.name}`);
  }
  await zoomed.close();
  // Other journeys of this user expect no menu being built.
  const cleanup = await browser.newContext({ ignoreHTTPSErrors: true });
  await cleanup.addCookies(cookies);
  const last = await cleanup.newPage();
  await last.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });
  await last.evaluate(async (sessionId) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    await fetch(`/api/v1/planning/shopping-plans/${sessionId}/abandon`, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken }, body: "{}" });
  }, seeded.sessionId);
  await cleanup.close();
} catch (error) {
  crashed = true;
  console.log(`[Failed] walk — ${String(error).split("\n")[0]}`);
} finally {
  await browser.close();
}

for (const note of notes) console.log(`  note: ${note}`);
for (const failure of failures) console.log(`  ${failure}`);
console.log(`${checked} screens checked, ${failures.length} problems, ${notes.length} minor notes.`);
process.exit(failures.length === 0 && !crashed ? 0 : 1);
