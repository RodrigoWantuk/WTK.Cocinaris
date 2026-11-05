#!/usr/bin/env node
/**
 * "Quero outras opções" against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0064. The replica
 * has no AI, so this covers the questions, what the browser sends, the visible wait, and recovery when
 * the request fails. What the model returns is covered by integration tests with a fake provider, and
 * by trying it on the deployed site.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/more-options-journey.mjs [username] [screenshot-file]
 * Never prints passwords or cookies.
 */
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import path from "node:path";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const { chromium } = createRequire(path.join(repo, "apps/frontend/package.json"))("playwright");
const BASE = "https://localhost:7443";
const username = process.argv[2] ?? "inventory-user-b";
const shot = process.argv[3];
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

const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
const context = await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR", viewport: { width: 390, height: 800 } });
const page = await context.newPage();
page.setDefaultTimeout(20000);
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const sessionId = await page.evaluate(async () => {
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
    const base = "/api/v1/planning/shopping-plans";
    const start = await (await fetch(`${base}/start`, { credentials: "include" })).json();
    if (start.current) await post(`${base}/${start.current.sessionId}/abandon`, {});
    for (const name of ["A", "B", "C", "D"]) {
      await post("/api/v1/recipes", {
        content: {
          name: `Opções E2E ${name} ${Date.now()}`,
          mealTypes: ["dinner"],
          servings: 2,
          ingredients: [{ displayName: `Item ${name}`, quantity: 2, unit: "unit", optional: false }],
          stages: [{ name: "Fazer", instructions: "Faça.", activeMinutes: 5, passiveMinutes: 0 }],
        },
      });
    }
    const now = new Date();
    const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
    const plan = await post(base, { today, days: 7, mealTypes: ["dinner"], mealsOut: 0, cookingStyle: "daily", priority: "short", people: 2 });
    if (!plan.sessionId) throw new Error(`The plan was not created: ${JSON.stringify(plan).slice(0, 200)}`);
    return plan.sessionId;
  });

  const requests = [];
  page.on("request", (request) => {
    if (request.url().endsWith("/batches")) requests.push(request.postDataJSON());
  });
  await page.goto(`${BASE}/app/compras/planejar/${sessionId}`, { waitUntil: "networkidle" });
  const name = page.getByTestId("plan-card-name");
  await name.waitFor();
  const dish = (await name.innerText()).trim();
  check("the deck shows a dish", dish.length > 0, dish);
  const before = requests.length;

  await page.getByTestId("plan-more").click();
  const panel = page.getByTestId("plan-more-options");
  await panel.waitFor();
  check("the panel asks what to use", (await page.getByTestId("plan-more-pantry").count()) === 1);
  check("the panel names the dish it compares with", (await panel.innerText()).includes(dish));
  check("opening the panel sends nothing", requests.length === before);
  check("the hint stays closed until asked for", (await page.getByTestId("plan-more-hint").count()) === 0);

  await page.getByTestId("plan-more-hint-open").click();
  const hint = page.getByTestId("plan-more-hint");
  await hint.fill("x".repeat(40));
  await hint.pressSequentially("yyy");
  check("the hint stops at 40 characters", (await hint.inputValue()).length === 40);
  await hint.fill("algo leve");
  await panel.scrollIntoViewIfNeeded();
  if (shot) await page.screenshot({ path: shot, fullPage: true });
  await page.setViewportSize({ width: 320, height: 640 });
  check("320 px has no horizontal scroll", await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth));
  await page.setViewportSize({ width: 390, height: 800 });

  await page.getByTestId("plan-more-pantry-free").click();
  await page.getByTestId("plan-more-similarity-different").click();
  await page.getByTestId("plan-more-submit").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="plan-ai-notice"]'), null, { timeout: 20000 });
  const sent = requests.at(-1);
  check("the answers reach the server", sent?.pantry === "free" && sent?.similarity === "different" && sent?.hint === "algo leve" && Boolean(sent?.referenceCardId), JSON.stringify({ ...sent, referenceCardId: Boolean(sent?.referenceCardId) }));
  check("the panel closes", (await panel.count()) === 0);
  check("without AI the person is told, and the dish comes back", (await name.innerText()).trim().length > 0, (await page.getByTestId("plan-ai-notice").innerText()).trim());
} catch (error) {
  check("journey finished", false, String(error).split("\n")[0]);
} finally {
  await browser.close();
}

const failed = results.filter((ok) => !ok).length;
console.log(failed === 0 ? `All ${results.length} checks passed.` : `${failed} of ${results.length} checks failed.`);
process.exit(failed === 0 ? 0 : 1);
