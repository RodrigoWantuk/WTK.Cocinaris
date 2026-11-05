#!/usr/bin/env node
/**
 * Signed-in preparation session journey against the proxy replica (scripts/e2e/proxy-replica.sh),
 * PLAN-0062 Slice 6. It seeds two recipes and an accepted plan through the API, then in the browser:
 * opens the session from the menu, starts it, leaves a passive part running, reloads and resumes with
 * the timer still counting, finishes both dishes on their cooking screens, and checks the pantry and
 * that the session no longer offers itself. Names are unique per run.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/session-route-journey.mjs [username]
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
  const stew = `Feijão E2E ${run}`;
  const salad = `Salada E2E ${run}`;
  const { sessionId, menuId } = await page.evaluate(async ({ stew, salad, run }) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const post = async (url, body) => {
      const response = await fetch(url, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify(body),
      });
      if (response.status === 429) throw new Error(`Rate limited on ${url}; wait a minute between runs.`);
      return response.json();
    };
    // A plan offers a limited number of saved recipes, so an earlier run's dishes would crowd these out.
    const earlier = await (await fetch("/api/v1/recipes?search=E2E", { credentials: "include" })).json();
    for (const item of earlier.items ?? earlier) await post(`/api/v1/recipes/${item.recipeId}/archive`, { archived: true });
    const recipes = [
      [stew, [
        { name: "Refogar", instructions: "Refogue a cebola.", activeMinutes: 1, passiveMinutes: 45 },
        { name: "Temperar", instructions: "Acerte o sal.", activeMinutes: 1, passiveMinutes: 0 },
      ]],
      [salad, [{ name: "Picar", instructions: "Pique os legumes.", activeMinutes: 1, passiveMinutes: 0 }]],
    ];
    for (const [name, stages] of recipes) {
      await post("/api/v1/recipes", { content: {
        name, mealTypes: ["dinner"], servings: 2,
        ingredients: [{ displayName: `Item ${name}`, quantity: 200, unit: "g", optional: false }],
        stages,
      } });
    }
    const base = "/api/v1/planning/shopping-plans";
    const start = await (await fetch(`${base}/start`, { credentials: "include" })).json();
    if (start.current) await post(`${base}/${start.current.sessionId}/abandon`, {});
    const now = new Date();
    const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    const plan = await post(base, { today, days: 3, mealTypes: ["dinner"], mealsOut: 0, cookingStyle: "mix", priority: "short", prepDate: today, prepMinutes: 120, people: 2 });
    const cards = plan.queue.filter((card) => card.name === stew || card.name === salad);
    if (cards.length !== 2) throw new Error(`Expected both seeded dishes in the queue, found ${cards.length}.`);
    for (const card of cards) {
      await post(`${base}/${plan.sessionId}/cards/${card.cardId}/decision`, { decision: "accept", mode: "yield", repeats: 2 });
    }
    await post(`${base}/${plan.sessionId}/acceptance`, {});
    return { sessionId: plan.sessionId, menuId: plan.menuId, cards: cards.length, run };
  }, { stew, salad, run });

  await page.goto(`${BASE}/app/cardapio/${menuId}`, { waitUntil: "networkidle" });
  await page.getByTestId("session-route-link").click();
  await page.getByTestId("plan-route-overview").waitFor({ timeout: 15000 });
  const planned = await page.getByTestId("plan-route-overview-task").count();
  check("overview lists the tasks of both dishes", planned === 3, `tasks=${planned}`);
  check("overview warns about burners and oven", (await page.getByText("bocas e o forno").count()) === 1);

  await page.getByTestId("plan-route-start").click();
  await page.getByTestId("plan-route-task").waitFor({ timeout: 15000 });
  const first = await page.getByTestId("plan-route-task-name").innerText();
  check("the longest chain comes first", first === "Refogar", first);
  const meanwhile = await page.getByTestId("plan-route-next").innerText();
  check("it says what to do meanwhile", meanwhile.startsWith("Enquanto isso") && meanwhile.includes(salad), meanwhile);
  await page.getByTestId("plan-route-done").click();
  const timer = page.getByTestId("plan-route-timer");
  await timer.waitFor({ timeout: 5000 });
  check("the passive part keeps running", (await timer.innerText()).includes("Refogar"));
  check("the next task opens while it runs", (await page.getByTestId("plan-route-task-name").innerText()) === "Picar");
  check("focus moves to the new task", await page.evaluate(() => document.activeElement?.getAttribute("data-testid") === "plan-route-task-name"));
  await page.setViewportSize({ width: 320, height: 700 });
  check("no sideways scroll at 320 px", await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth));
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);

  await page.reload({ waitUntil: "networkidle" });
  await page.getByTestId("plan-route-task").waitFor({ timeout: 15000 });
  check("a reload resumes on the same task", (await page.getByTestId("plan-route-task-name").innerText()) === "Picar");
  const clock = await page.getByTestId("plan-route-timer").innerText();
  check("the timer survives the reload", /4[34]:\d\d/.test(clock), clock.replace(/\s+/g, " "));

  await page.getByTestId("plan-route-done").click();
  await page.getByTestId("plan-route-waiting").waitFor({ timeout: 5000 });
  check("a task that depends on the wait says so", (await page.getByTestId("plan-route-waiting").innerText()).includes("Refogar"));
  await page.getByTestId("plan-route-done").click();

  for (let dish = 0; dish < 2; dish++) {
    await page.getByTestId("plan-route-finish-next").click();
    await page.getByTestId("cooking-finish-screen").waitFor({ timeout: 15000 });
    await page.getByTestId("cooking-done").click();
    await page.getByTestId("cooking-back-to-route").click();
    await page.getByTestId("plan-route-finish").waitFor({ timeout: 15000 });
  }
  await page.getByTestId("plan-route-complete").waitFor({ timeout: 15000 });
  const finished = await page.getByTestId("plan-route-finish-dish").evaluateAll((rows) => rows.map((row) => row.getAttribute("data-status")));
  check("both dishes end cooked", finished.length === 2 && finished.every((status) => status === "cooked"), finished.join());

  const lots = await page.evaluate(async ({ stew, salad }) => {
    const { items } = await (await fetch("/api/v1/inventory/lots?pageSize=50", { credentials: "include" })).json();
    return items.filter((lot) => lot.productName === stew || lot.productName === salad).length;
  }, { stew, salad });
  check("finishing stored the portions in the pantry", lots >= 2, `lots=${lots}`);

  await page.goto(`${BASE}/app/cardapio/${menuId}`, { waitUntil: "networkidle" });
  await page.getByTestId("menu-shopping-link").waitFor({ timeout: 15000 });
  check("the finished session stops offering itself", (await page.getByTestId("session-route-link").count()) === 0);
  await page.goto(`${BASE}/app/compras/planejar/${sessionId}/preparo`, { waitUntil: "networkidle" });
  await page.getByTestId("plan-route-complete").waitFor({ timeout: 15000 });
  check("it still opens as finished", true);
} catch (error) {
  const screen = await page.locator("main").innerText().catch(() => "");
  check("journey completed", false, `${String(error).slice(0, 300)} at ${page.url()}: ${screen.slice(0, 400).replace(/\s+/g, " ")}`);
} finally {
  await browser.close();
}

let failed = 0;
for (const { name, ok, detail } of results) {
  if (!ok) failed++;
  process.stdout.write(`${ok ? "PASS" : "FAIL"} ${name}${detail ? ` (${detail})` : ""}\n`);
}
process.exit(failed ? 1 : 0);
