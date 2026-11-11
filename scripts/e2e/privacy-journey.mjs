#!/usr/bin/env node
/**
 * Privacy center journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0049 Gate S1.
 * At 360 px: the profile links to "Seus dados", the page fits without sideways scroll, its controls are at
 * least 44 px high, and "Baixar meus dados" saves a ZIP whose name and size look right. The ZIP is checked
 * for its two files and then deleted; nothing of it is printed.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/privacy-journey.mjs [username]
 * Never prints passwords, cookies, or exported data.
 */
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { readFile, rm } from "node:fs/promises";
import os from "node:os";
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

const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
try {
  const context = await browser.newContext({ viewport: { width: 360, height: 740 }, ignoreHTTPSErrors: true, locale: "pt-BR", acceptDownloads: true });
  const page = await context.newPage();
  await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
  await page.getByTestId("production-login").click();
  await page.waitForSelector("#kc-form-login", { timeout: 30000 });
  await page.fill("#username", username);
  await page.fill("#password", password);
  await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);

  await page.goto(`${BASE}/app/perfil`, { waitUntil: "domcontentloaded" });
  const link = page.getByTestId("profile-overview-link-privacy");
  await link.waitFor({ timeout: 20000 });
  check("the profile links to Seus dados", (await link.getAttribute("href")) === "/app/perfil/privacidade");
  await link.click();
  await page.getByTestId("profile-privacy").waitFor({ timeout: 20000 });
  check("the page title says Seus dados", (await page.getByRole("heading", { level: 1 }).textContent())?.includes("Seus dados") === true);

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  check("no sideways scroll at 360 px", overflow <= 0, `${overflow} px`);
  const small = await page.evaluate(() =>
    [...document.querySelectorAll('[data-testid="profile-privacy"] button, [data-testid="profile-privacy"] nav a')]
      .filter((element) => element.getBoundingClientRect().height < 44)
      .map((element) => element.getAttribute("data-testid") ?? element.textContent?.trim()),
  );
  check("buttons and links are at least 44 px high", small.length === 0, small.join(", "));
  check("account deletion is shown as coming", await page.getByTestId("privacy-delete").isDisabled());

  const [download] = await Promise.all([page.waitForEvent("download", { timeout: 60000 }), page.getByTestId("privacy-download").click()]);
  const name = download.suggestedFilename();
  check("the download is a dated ZIP", /^cocinaris-dados-\d{4}-\d{2}-\d{2}\.zip$/.test(name), name);
  const file = path.join(os.tmpdir(), `privacy-journey-${process.pid}.zip`);
  await download.saveAs(file);
  const bytes = await readFile(file);
  await rm(file, { force: true });
  // A ZIP starts with "PK" and names its entries in plain text in the central directory.
  const text = bytes.toString("latin1");
  check("the ZIP holds the JSON and the readable page", bytes.subarray(0, 2).toString("latin1") === "PK" && text.includes("cocinaris-dados.json") && text.includes("cocinaris-dados.html"), `${bytes.length} bytes`);
  await page.getByText("O arquivo está sendo salvo").waitFor({ timeout: 10000 });
  check("the page says the file is being saved", true);
  await context.close();
} catch (error) {
  check("journey", false, String(error).split("\n")[0]);
} finally {
  await browser.close();
}

for (const result of results) {
  process.stdout.write(`${result.ok ? "Passed" : "Failed"}  ${result.name}${result.detail ? ` — ${result.detail}` : ""}\n`);
}
process.exit(results.every((result) => result.ok) ? 0 : 1);
