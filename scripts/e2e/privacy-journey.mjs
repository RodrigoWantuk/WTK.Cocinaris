#!/usr/bin/env node
/**
 * Privacy center journey against the proxy replica (scripts/e2e/proxy-replica.sh), PLAN-0049 Gate S1.
 * At 360 px: the profile links to "Seus dados", the page fits without sideways scroll, its controls are at
 * least 44 px high, and "Baixar meus dados" saves a ZIP whose name and size look right. The ZIP is checked
 * for its two files and then deleted; nothing of it is printed. With --delete (Slice 2), it then deletes the
 * account: the word, the page outside the app, an old session refused, and a new sign-in that starts an
 * empty account. Use a fixture user whose replica data may go.
 *
 * Usage: COCINARIS_SMOKE_PASSWORD_A=<development-fixture-password> \
 *          node scripts/e2e/privacy-journey.mjs [username] [--delete]
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
const args = process.argv.slice(2);
const username = args.find((arg) => !arg.startsWith("--")) ?? "inventory-user-a";
const deleting = args.includes("--delete");
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
  check("deleting waits for the typed word", await page.getByTestId("privacy-delete").isDisabled());

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

  if (deleting) {
    const sessionOf = () => page.evaluate(async () => {
      const response = await fetch("/api/v1/session", { credentials: "include" });
      return { status: response.status, userId: response.ok ? (await response.json()).userId : null };
    });
    const before = await sessionOf();
    const cookies = await context.cookies();
    await page.getByTestId("privacy-delete-word").fill("excluir");
    await Promise.all([page.waitForURL(/\/conta-excluida$/, { timeout: 30000 }), page.getByTestId("privacy-delete").click()]);
    await page.getByTestId("account-deleted").waitFor({ timeout: 10000 });
    check("the deleted page says so", (await page.getByRole("heading", { level: 1 }).textContent())?.includes("Sua conta foi excluída") === true);
    const deletedOverflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    check("the deleted page fits at 360 px", deletedOverflow <= 0, `${deletedOverflow} px`);

    // The old cookie, put back as another device would still hold it, no longer opens anything.
    const oldDevice = await browser.newContext({ ignoreHTTPSErrors: true });
    await oldDevice.addCookies(cookies);
    const oldPage = await oldDevice.newPage();
    await oldPage.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
    const oldStatus = await oldPage.evaluate(async () => (await fetch("/api/v1/session", { credentials: "include" })).status);
    check("a session from before the deletion is refused", oldStatus === 401, String(oldStatus));
    await oldDevice.close();

    // The erasure runs within seconds; then signing in starts an empty account (D2).
    await page.waitForTimeout(5000);
    await page.goto(`${BASE}/acesso`, { waitUntil: "domcontentloaded" });
    await page.getByTestId("production-login").click();
    const form = page.locator("#kc-form-login");
    await Promise.race([form.waitFor({ timeout: 30000 }), page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 })]);
    if (await form.isVisible().catch(() => false)) {
      await page.fill("#username", username);
      await page.fill("#password", password);
      await Promise.all([page.waitForURL(/localhost:7443\/app\//, { timeout: 30000 }), page.click("#kc-login")]);
    }
    const after = await sessionOf();
    check("signing in again starts a new account", after.status === 200 && after.userId && after.userId !== before.userId, `${before.status} → ${after.status}`);
    const recipes = await page.evaluate(async () => (await (await fetch("/api/v1/recipes", { credentials: "include" })).json()));
    const count = Array.isArray(recipes) ? recipes.length : Array.isArray(recipes?.items) ? recipes.items.length : -1;
    check("the new account has no recipes", count === 0, String(count));
  }

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
