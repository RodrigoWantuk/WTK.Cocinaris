#!/usr/bin/env node
/**
 * Signed-in recipe library journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0038.
 * In the browser: writes a recipe in the editor, favorites it, cooks it, saves the cooking
 * adjustments as a new version from the finished screen, creates a variation, and archives it.
 * No AI provider is needed.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/library-journey.mjs [username]
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
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR" })).newPage();
try {
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  const name = `Feijão E2E ${Date.now()}`;
  await page.goto(`${BASE}/app/receitas`, { waitUntil: "networkidle" });
  await page.getByTestId("recipes-new-link").click();
  await page.getByTestId("recipe-editor").waitFor();
  await page.getByTestId("recipe-editor-name").fill(name);
  await page.getByTestId("recipe-editor-meal-dinner").check();
  const ingredient = page.getByTestId("recipe-editor-ingredient").first();
  await ingredient.getByLabel("Ingrediente").fill("Feijão");
  await ingredient.getByLabel("Quantidade").fill("250,5");
  const stage = page.getByTestId("recipe-editor-stage").first();
  await stage.getByLabel("Título da etapa").fill("Cozinhar");
  await stage.getByLabel(/Instruções/).fill("Deixe de molho.\nCozinhe na pressão por 30 minutos.");
  await page.getByTestId("recipe-editor-save").click();
  await page.getByTestId("recipes-detail").waitFor({ timeout: 15000 });
  check("manual recipe saved with both steps", (await page.getByText("Cozinhe na pressão por 30 minutos.").count()) === 1);
  check("decimal quantity kept", (await page.getByText(/250,5/).count()) > 0);
  const recipeUrl = page.url();

  await page.getByTestId("recipes-detail-favorite").click();
  await page.waitForFunction(() => document.querySelector('[data-testid="recipes-detail-favorite"]')?.getAttribute("aria-pressed") === "true");
  await page.goto(`${BASE}/app/receitas`, { waitUntil: "networkidle" });
  await page.getByTestId("recipes-filter-favorites").click();
  await page.getByText(name).waitFor({ timeout: 10000 });
  check("favorite listed under Favoritas", true);

  await page.goto(recipeUrl, { waitUntil: "networkidle" });
  await page.getByTestId("recipes-detail-cook").click();
  await page.waitForURL(/\/app\/receitas\/cozinhar\//, { timeout: 15000 });
  await page.getByTestId("cooking-finish").click();
  // Nothing came from the pantry: leave the line unchecked so nothing is consumed.
  const include = page.getByTestId("cooking-review-line").first().getByRole("checkbox");
  if (await include.isChecked()) await include.uncheck();
  await page.getByLabel("Anotações (só você vê)").fill("Precisa de mais sal");
  await page.getByTestId("cooking-confirm").click();
  await page.getByTestId("cooking-finished").waitFor({ timeout: 15000 });

  await page.getByTestId("cooking-done-save-changes").click();
  await page.getByTestId("recipe-editor-cooking-notes").waitFor({ timeout: 10000 });
  check("editor shows the cooking notes", (await page.getByText("Precisa de mais sal").count()) === 1);
  await page.getByTestId("recipe-editor-name").fill(`${name} com sal`);
  await page.getByTestId("recipe-editor-reason").fill("Mais sal");
  await page.getByTestId("recipe-editor-save").click();
  await page.getByTestId("recipes-detail").waitFor({ timeout: 15000 });
  check("saved as version 2", (await page.getByText("Versão 2").count()) > 0);
  await page.getByTestId("recipes-history-revision").first().waitFor();
  check("history records the edit after cooking", (await page.getByTestId("recipes-history-revision").first().textContent()).includes("depois de um preparo"));
  check("history lists the cooking session", (await page.getByTestId("recipes-history-cooking").count()) === 1);

  await page.getByTestId("recipes-detail-variation").click();
  await page.getByTestId("recipe-editor-reason").fill("Versão vegana");
  await page.getByTestId("recipe-editor-name").fill(`${name} vegano`);
  await page.getByTestId("recipe-editor-save").click();
  await page.getByTestId("recipes-detail-parent").waitFor({ timeout: 15000 });
  check("variation links to the original", true);

  page.once("dialog", (dialog) => void dialog.accept());
  await page.getByTestId("recipes-detail-archive").click();
  await page.getByText("Esta receita está arquivada.").waitFor({ timeout: 10000 });
  await page.goto(`${BASE}/app/receitas`, { waitUntil: "networkidle" });
  check("archived variation left the library", (await page.getByText(`${name} vegano`).count()) === 0);
  await page.getByTestId("recipes-filter-archived").click();
  await page.getByText(`${name} vegano`).waitFor({ timeout: 10000 });
  check("archived variation listed under Arquivadas", true);
} catch (error) {
  check("journey", false, `${error.message.split("\n")[0]} (at ${new URL(page.url()).pathname})`);
  if (process.env.COCINARIS_E2E_SCREENSHOT) await page.screenshot({ path: process.env.COCINARIS_E2E_SCREENSHOT, fullPage: true });
} finally {
  await browser.close();
}

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
process.exit(results.length > 0 && results.every((result) => result.ok) ? 0 : 1);
