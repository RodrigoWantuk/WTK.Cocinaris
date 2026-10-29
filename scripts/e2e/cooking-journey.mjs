#!/usr/bin/env node
/**
 * Signed-in cooking journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0042.
 * Creates a pantry lot, seeds one saved recipe that uses it directly in the development database
 * (recipe generation needs the AI provider, which the replica does not call), then cooks it in the
 * browser: start, completion review, confirm with leftovers, and checks the pantry and "Hoje".
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/cooking-journey.mjs [username]
 * Requires the development compose stack (service `postgres`). Never prints passwords or cookies.
 */
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import { randomUUID } from "node:crypto";
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
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR" })).newPage();
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const productName = `Arroz E2E ${Date.now()}`;
  const { userId, lotId } = await page.evaluate(async (name) => {
    const session = await (await fetch("/api/v1/session", { credentials: "include" })).json();
    const response = await fetch("/api/v1/inventory/lots", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": session.csrfToken, "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify({ productName: name, quantity: { measuredValue: 500, unit: "Gram" }, storageLocation: "Pantry" }),
    });
    return { userId: session.userId, lotId: (await response.json()).lotId };
  }, productName);

  const recipeId = randomUUID();
  const content = JSON.stringify({
    recipe: {
      ingredients: [{ ingredientId: "ing-rice", sourceType: "inventory", displayName: productName, inventoryItemId: lotId.replaceAll("-", ""), requiredQuantity: 200, unit: "g", optional: false }],
      stages: [{ stageId: "stage-1", name: "Cozinhar", instructions: "Cozinhe o arroz em fogo baixo por 15 minutos.", activeMinutes: 5, passiveMinutes: 15, dependsOn: [], sensoryCues: ["água absorvida"], usesIngredientIds: ["ing-rice"] }],
      assumptions: [],
    },
  }).replaceAll("'", "''");
  const sql = `INSERT INTO recipes.recipes ("Id","OwnerUserId","CurrentRevisionNumber","CreatedAt","UpdatedAt") VALUES ('${recipeId}','${userId}',1,now(),now());
INSERT INTO recipes.recipe_revisions ("Id","RecipeId","OwnerUserId","RevisionNumber","Name","MealTypesJson","Servings","NormalizedRecipeJson","ThumbnailVisualJson","SourceCandidateId","SourceGenerationSessionId","CreatedAt")
VALUES ('${randomUUID()}','${recipeId}','${userId}',1,'Arroz E2E','["lunch"]',2,'${content}','{}','cand-e2e','${randomUUID()}',now());`;
  execFileSync("docker", ["compose", "-f", path.join(repo, "infrastructure/compose/compose.dev.yml"), "exec", "-T", "postgres", "psql", "-q", "-v", "ON_ERROR_STOP=1", "-U", "cocinaris_dev", "-d", "cocinaris"], { input: sql, stdio: ["pipe", "ignore", "inherit"] });

  await page.goto(`${BASE}/app/receitas/${recipeId}`, { waitUntil: "networkidle" });
  check("recipe detail shows stage instructions", (await page.getByText("Cozinhe o arroz em fogo baixo por 15 minutos.").count()) === 1);
  await page.getByTestId("recipes-detail-cook").click();
  await page.waitForURL(/\/app\/receitas\/cozinhar\//, { timeout: 15000 });
  const executionUrl = page.url();

  await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
  check("today offers to resume cooking", (await page.getByTestId("today-cooking").count()) === 1);

  await page.goto(executionUrl, { waitUntil: "networkidle" });
  await page.getByTestId("cooking-finish").click();
  const line = page.getByTestId("cooking-review-line").first();
  check("review proposes 200 g from the lot", (await line.getByRole("textbox").inputValue()) === "200");
  await page.getByTestId("cooking-leftover-toggle").check();
  await page.getByTestId("cooking-confirm").click();
  await page.getByTestId("cooking-finished").waitFor({ timeout: 15000 });

  const after = await page.evaluate(async (id) => (await (await fetch(`/api/v1/inventory/lots/${id}`, { credentials: "include" })).json()).quantity.measuredValue, lotId);
  check("pantry lot went from 500 g to 300 g", after === 300, `measured ${after}`);
  const leftovers = await page.evaluate(async () => (await (await fetch("/api/v1/inventory/lots?search=Arroz%20E2E", { credentials: "include" })).json()).items.length);
  check("leftover lot was created next to the original lot", leftovers === 2, `items ${leftovers}`);
} catch (error) {
  check("journey", false, error.message.split("\n")[0]);
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
