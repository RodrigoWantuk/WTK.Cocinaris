#!/usr/bin/env node
/**
 * Signed-in profile journey against the proxy replica (scripts/e2e/proxy-replica.sh). It saves one
 * change on each profile page and fails if any save leaves the workspace in the
 * "saved but could not reload" state, which is how proxy-altered ETags surfaced (PLAN-0053).
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/profile-journey.mjs [username]
 * Never prints passwords, cookies, or tokens. Exits non-zero when a step fails.
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

const browser = await chromium.launch();
const page = await (await browser.newContext({ ignoreHTTPSErrors: true, locale: "pt-BR" })).newPage();
const mutations = [];
page.on("response", (response) => {
  const url = new URL(response.url());
  const method = response.request().method();
  if (url.pathname.startsWith("/api/v1/profile") && method !== "GET") mutations.push(`${method} ${url.pathname} ${response.status()}`);
});

const results = [];
async function step(name, action) {
  try {
    await action();
    await page.waitForTimeout(1500);
    const stale = (await page.getByTestId("profile-save-refresh-failed").count()) > 0;
    results.push({ name, ok: !stale, detail: stale ? "save-refresh-failed banner shown" : "" });
  } catch (error) {
    results.push({ name, ok: false, detail: error.message.split("\n")[0] });
  }
}

await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
await page.getByTestId("production-login").click();
await page.waitForSelector("#kc-form-login", { timeout: 30000 });
await page.fill("#username", username);
await page.fill("#password", password);
await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

const displayName = `E2E ${Date.now()}`;
await page.goto(`${BASE}/app/perfil/dados`, { waitUntil: "networkidle" });
await step("data: change display name and save", async () => {
  await page.getByRole("textbox", { name: "Nome de exibição" }).fill(displayName);
  await page.getByTestId("profile-data-household-save").click();
});
await page.goto(`${BASE}/app/perfil/preferencias`, { waitUntil: "networkidle" });
await step("preferences: add two catalog entries", async () => {
  for (let i = 0; i < 2; i += 1) {
    await page.getByTestId("profile-preferences-catalog-select").selectOption({ index: 1 });
    await page.getByTestId("profile-preferences-catalog-submit").click();
    await page.waitForTimeout(1000);
  }
});
await page.goto(`${BASE}/app/perfil/equipamentos`, { waitUntil: "networkidle" });
await step("equipment: add one entry and save", async () => {
  await page.getByTestId("profile-equipment-catalog-select").selectOption({ index: 1 });
  await page.getByTestId("profile-equipment-catalog-submit").click();
  await page.getByTestId("profile-equipment-save").click();
});
await page.goto(`${BASE}/app/perfil/dados`, { waitUntil: "networkidle" });
await step("data: saved name persists after reload", async () => {
  const value = await page.getByRole("textbox", { name: "Nome de exibição" }).inputValue();
  if (value !== displayName) throw new Error("display name was not persisted");
});
await browser.close();

for (const result of results) console.log(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}`);
console.log(`mutations: ${mutations.join(", ") || "none"}`);
process.exit(results.every((result) => result.ok) ? 0 : 1);
