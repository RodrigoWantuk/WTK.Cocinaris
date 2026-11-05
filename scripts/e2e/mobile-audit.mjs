#!/usr/bin/env node
/**
 * Phone layout gate for the planning screens, against the proxy replica (scripts/e2e/proxy-replica.sh),
 * PLAN-0065 Slice 0. It seeds a plan with long names and walks each screen at 320, 360, and 390 px with a
 * touch screen, and fails on what breaks a screen on a phone: sideways scrolling, text or controls lying
 * on each other, clipped text, and controls under 44 px.
 *
 * It runs on Chromium always, and on WebKit with COCINARIS_AUDIT_WEBKIT=1 when that browser is installed.
 * WebKit approximates Safari, and neither replaces a real phone. Screenshots go to artifacts/mobile-audit.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/mobile-audit.mjs [username]
 * Never prints passwords or cookies.
 */
import { mkdirSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium, webkit, devices } = createRequire(path.join(repo, "apps/frontend/package.json"))("playwright");
const BASE = "https://localhost:7443";
const OUT = path.join(repo, "artifacts/mobile-audit");
const WIDTHS = [320, 360, 390];
const username = process.argv[2] ?? "inventory-user-b";
const password = process.env.COCINARIS_SMOKE_PASSWORD_A;
if (!password) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A is required.\n");
  process.exit(2);
}
mkdirSync(OUT, { recursive: true });

/** Measures the open screen in the page. Everything it reports is a defect. */
const measure = () => {
  const main = document.querySelector("main") ?? document.body;
  const root = document.documentElement;
  const problems = [];
  if (root.scrollWidth > root.clientWidth) problems.push(`sideways scroll: ${root.scrollWidth} > ${root.clientWidth}`);
  const visible = (node) => {
    const box = node.getBoundingClientRect();
    const style = getComputedStyle(node);
    if (box.width < 2 || box.height < 2 || style.visibility === "hidden" || style.display === "none") return false;
    if (node.closest("details:not([open])") && !node.closest("summary")) return false;
    return !node.closest(".sr-only");
  };
  const label = (node) => (node.dataset?.testid || (node.textContent ?? "").trim() || node.tagName).slice(0, 34);

  // Off-screen.
  for (const node of main.querySelectorAll("*")) {
    if (!visible(node) || node.closest("[data-audit-scrolls]")) continue;
    const box = node.getBoundingClientRect();
    if (box.right > root.clientWidth + 1 || box.left < -1) problems.push(`off screen: ${label(node)}`);
  }

  // Small targets. Text links inside a sentence and native marks inside a large label are fine.
  for (const node of main.querySelectorAll("a, button, input, select, textarea, summary")) {
    if (!visible(node) || node.disabled) continue;
    if (node.matches("input[type=checkbox], input[type=radio]")) {
      if (node.closest("label") && node.closest("label").getBoundingClientRect().height >= 44) continue;
    }
    if (node.matches("a") && getComputedStyle(node).display === "inline") continue;
    const height = node.getBoundingClientRect().height;
    if (height < 43.5) problems.push(`target ${Math.round(height)} px: ${label(node)}`);
  }

  // Clipped text. A line meant to end in an ellipsis is allowed.
  for (const node of main.querySelectorAll("h1, h2, h3, p, span, a, button, label")) {
    if (!visible(node)) continue;
    const style = getComputedStyle(node);
    if (style.textOverflow === "ellipsis" || style.display === "inline") continue;
    if (node.scrollWidth > node.clientWidth + 1 && style.overflowX !== "visible") problems.push(`clipped: ${label(node)}`);
  }

  // Text or controls lying on each other, as "800 g" under "Já tenho".
  const leaves = [...main.querySelectorAll("a, button, input, select, textarea, h1, h2, h3, p, span, label, li > *")]
    .filter(visible)
    .filter((node) => node.matches("a, button, input, select, textarea") || [...node.childNodes].some((child) => child.nodeType === 3 && child.textContent.trim()));
  const textBox = (node) => {
    if (node.matches("input, select, textarea")) return node.getBoundingClientRect();
    if (node.matches("a, button")) return node.getBoundingClientRect();
    // A line ending in an ellipsis is cut at its own box, whatever length its text would have.
    if (getComputedStyle(node).textOverflow === "ellipsis") return node.getBoundingClientRect();
    const range = document.createRange();
    range.selectNodeContents(node);
    return range.getBoundingClientRect();
  };
  const boxes = leaves.map((node) => ({ node, box: textBox(node) }));
  for (let i = 0; i < boxes.length; i += 1) {
    for (let j = i + 1; j < boxes.length; j += 1) {
      const a = boxes[i];
      const b = boxes[j];
      if (a.node.contains(b.node) || b.node.contains(a.node)) continue;
      const wide = Math.min(a.box.right, b.box.right) - Math.max(a.box.left, b.box.left);
      const high = Math.min(a.box.bottom, b.box.bottom) - Math.max(a.box.top, b.box.top);
      if (wide > 2 && high > 2) problems.push(`overlap: ${label(a.node)} / ${label(b.node)}`);
    }
  }
  return [...new Set(problems)];
};

const failures = [];
let checked = 0;

async function audit(browserName, page, width, name) {
  await page.waitForTimeout(250);
  const problems = await page.evaluate(measure);
  checked += 1;
  await page.screenshot({ path: path.join(OUT, `${browserName}-${width}-${name}.png`), fullPage: true });
  for (const problem of problems) failures.push(`${browserName} ${width} px ${name}: ${problem}`);
  console.log(`[${problems.length === 0 ? "Passed" : "Failed"}] ${browserName} ${width} px ${name}${problems.length ? ` — ${problems.length} problems` : ""}`);
}

async function seed(page) {
  return page.evaluate(async () => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) => {
      const response = await fetch(url, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify(body),
      });
      if (response.status === 429) throw new Error(`Rate limited on ${url}; wait a minute between runs.`);
      return response.json().catch(() => ({}));
    };
    const base = "/api/v1/planning/shopping-plans";
    const start = await (await fetch(`${base}/start`, { credentials: "include" })).json();
    if (start.current) await post(`${base}/${start.current.sessionId}/abandon`, {});
    for (const menu of await (await fetch("/api/v1/planning/menus", { credentials: "include" })).json()) {
      await post(`/api/v1/planning/menus/${menu.menuId}/archive`, {});
    }
    const earlier = await (await fetch("/api/v1/recipes?search=Mobile", { credentials: "include" })).json();
    if ((earlier.items ?? earlier).length < 4) {
      const recipes = [
        ["Mobile Escondidinho de frango com purê de mandioquinha e queijo coalho gratinado", [["Peito de frango desfiado e temperado", 600, "g"], ["Mandioquinha", 800, "g"], ["Queijo coalho", 200, "g"]]],
        ["Mobile Feijoada leve", [["Feijão preto", 500, "g"], ["Linguiça calabresa defumada artesanal", 300, "g"]]],
        ["Mobile Salada de grão-de-bico", [["Grão-de-bico", 300, "g"], ["Tomate", 4, "unit"]]],
        ["Mobile Macarrão ao sugo", [["Macarrão", 500, "g"], ["Manjericão", 1, "unit"]]],
      ];
      for (const [name, items] of recipes) {
        await post("/api/v1/recipes", {
          content: {
            name,
            mealTypes: ["dinner"],
            servings: 2,
            ingredients: items.map(([displayName, quantity, unit]) => ({ displayName, quantity, unit, optional: false })),
            stages: [{ name: "Preparar", instructions: "Prepare tudo.", activeMinutes: 15, passiveMinutes: 0 }],
          },
        });
      }
    }
  });
}

async function walk(browserName, browserType, profile) {
  const browser = await browserType.launch(browserName === "chromium" ? { args: ["--ignore-certificate-errors"] } : {});
  try {
    for (const width of WIDTHS) {
      const context = await browser.newContext({ ...profile, ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width, height: 720 } });
      const page = await context.newPage();
      page.setDefaultTimeout(20000);
      await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
      await page.getByTestId("production-login").click();
      await page.waitForSelector("#kc-form-login", { timeout: 30000 });
      await page.fill("#username", username);
      await page.fill("#password", password);
      await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
      await seed(page);

      await page.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });
      await audit(browserName, page, width, "hub-empty");
      await page.getByTestId("planning-hub-start").click();
      await page.getByTestId("plan-setup").waitFor();
      await audit(browserName, page, width, "setup");
      await page.getByTestId("plan-days-5").click();
      await page.getByTestId("plan-meal-lunch").click().catch(() => undefined);
      await page.getByTestId("plan-begin").click();
      await page.getByTestId("plan-card-name").waitFor();
      await page.waitForTimeout(800);
      await audit(browserName, page, width, "cards");
      await page.getByTestId("plan-yes-twice").click();
      await audit(browserName, page, width, "cards-repeat");
      await page.getByTestId("plan-yes-twice").click();
      await page.getByTestId("plan-no").click();
      await page.getByTestId("plan-reasons").waitFor();
      await audit(browserName, page, width, "cards-reasons");
      await page.getByTestId("plan-more").click();
      await audit(browserName, page, width, "cards-more");
      await page.getByTestId("plan-more").click();
      await page.getByTestId("plan-yes").click();
      await page.waitForTimeout(500);
      await page.getByTestId("plan-yes").click();
      await page.waitForTimeout(500);
      await page.getByTestId("plan-enough").click();
      await page.getByTestId("plan-review-entry").first().waitFor();
      await page.waitForTimeout(500);
      await audit(browserName, page, width, "review");
      await page.getByTestId("plan-use").click();
      await page.getByTestId("plan-use-confirm").waitFor();
      await audit(browserName, page, width, "review-confirm");
      await page.getByTestId("plan-use-anyway").click();
      await page.getByTestId("menu-page").waitFor();
      await page.waitForTimeout(500);
      await audit(browserName, page, width, "menu");
      await page.goto(`${page.url()}/compras`, { waitUntil: "networkidle" });
      await page.getByTestId("shopping-item").first().waitFor();
      await audit(browserName, page, width, "list");
      const line = page.getByTestId("shopping-item").first();
      const height = (await line.boundingBox()).height;
      if (height > 90) failures.push(`${browserName} ${width} px list: a line is ${Math.round(height)} px high`);
      await line.getByTestId("shopping-item-more").click();
      await audit(browserName, page, width, "list-actions");
      await line.getByTestId("shopping-item-buy").click();
      await audit(browserName, page, width, "list-buy");
      await line.getByTestId("shopping-item-buy").click();
      await line.getByTestId("shopping-not-found").click();
      await audit(browserName, page, width, "list-not-found");
      await line.getByTestId("shopping-not-found").click();
      await line.getByTestId("shopping-item-check").check();
      await page.getByTestId("shopping-arrival").first().waitFor();
      await page.getByTestId("shopping-split").check();
      await audit(browserName, page, width, "list-by-place");
      await page.getByTestId("shopping-arrival").first().click();
      await page.waitForTimeout(800);
      await audit(browserName, page, width, "arrival");
      await page.goto(`${BASE}/app/compras`, { waitUntil: "networkidle" });
      await page.getByTestId("planning-hub").waitFor();
      await audit(browserName, page, width, "hub-in-use");
      await context.close();
      // The `mutation` limit is 60 a minute per person, and each pass uses most of it.
      if (width !== WIDTHS.at(-1)) await new Promise((done) => setTimeout(done, 45000));
    }
  } finally {
    await browser.close();
  }
}

let crashed = false;
try {
  await walk("chromium", chromium, devices["Pixel 7"]);
  if (process.env.COCINARIS_AUDIT_WEBKIT === "1") await walk("webkit", webkit, devices["iPhone 13"]);
} catch (error) {
  crashed = true;
  console.log(`[Failed] walk — ${String(error).split("\n")[0]}`);
}

for (const failure of failures) console.log(`  ${failure}`);
console.log(`${checked} screens checked, ${failures.length} problems.`);
process.exit(failures.length === 0 && !crashed ? 0 : 1);
