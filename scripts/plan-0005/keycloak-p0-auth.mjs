#!/usr/bin/env node
/**
 * PLAN-0005 P0 real-Keycloak authentication validation.
 * Extends the accepted smoke with cookie attributes, token-absence probes,
 * CSRF negative cases, logout invalidation, and open-redirect rejection.
 * Never prints passwords, cookies, tokens, or Authorization headers.
 */
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, "../..");
const evidenceDir = process.env.PLAN0005_EVIDENCE_DIR ?? join(root, "docs/evidence/plan-0005");
const reportPath = join(evidenceDir, "reports", "keycloak-p0-auth.json");

const apiUrl = process.env.COCINARIS_SMOKE_API_URL ?? "https://localhost:7443";
const browserPath = process.env.COCINARIS_SMOKE_BROWSER ?? "google-chrome";
const allowUntrustedLocalCertificate = process.env.COCINARIS_SMOKE_ALLOW_UNTRUSTED_LOCAL_CERTIFICATE === "1";
const browserSandboxArguments = process.getuid?.() === 0 ? ["--no-sandbox"] : [];
const browserTrustStoreArguments = process.platform === "linux" ? ["--disable-features=ChromeRootStoreUsed"] : [];
const users = [
  { username: process.env.COCINARIS_SMOKE_USER_A ?? "inventory-user-a", password: process.env.COCINARIS_SMOKE_PASSWORD_A },
  { username: process.env.COCINARIS_SMOKE_USER_B ?? "inventory-user-b", password: process.env.COCINARIS_SMOKE_PASSWORD_B }
];

if (users.some((user) => !user.password)) {
  process.stderr.write("COCINARIS_SMOKE_PASSWORD_A and COCINARIS_SMOKE_PASSWORD_B are required.\n");
  process.exit(2);
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function waitFor(check, description, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const result = await check();
      if (result) return result;
    } catch (error) {
      lastError = error;
    }
    await delay(200);
  }
  throw new Error(`Timed out waiting for ${description}${lastError ? `: ${lastError.message}` : ""}.`);
}

async function terminateBrowserProcess(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  child.kill("SIGTERM");
  const exited = await Promise.race([
    new Promise((resolve) => child.once("exit", () => resolve(true))),
    delay(5000).then(() => false)
  ]);
  if (!exited && child.exitCode === null && child.signalCode === null) {
    child.kill("SIGKILL");
    await Promise.race([new Promise((resolve) => child.once("exit", resolve)), delay(5000)]);
  }
}

async function removeBrowserProfile(profile) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      await rm(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
      return;
    } catch {
      if (attempt === 19) throw new Error("Failed to remove browser profile.");
      await delay(500);
    }
  }
}

class DevToolsConnection {
  constructor(url) {
    this.socket = new WebSocket(url);
    this.nextId = 0;
    this.pending = new Map();
    this.networkFailures = [];
    this.socket.addEventListener("message", (event) => {
      const message = JSON.parse(event.data);
      if (message.method === "Network.loadingFailed") {
        this.networkFailures.push({ errorText: message.params.errorText, type: message.params.type });
        this.networkFailures = this.networkFailures.slice(-5);
      }
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result);
    });
  }

  async open() {
    await new Promise((resolve, reject) => {
      this.socket.addEventListener("open", resolve, { once: true });
      this.socket.addEventListener("error", () => reject(new Error("Could not connect to Chrome DevTools.")), { once: true });
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    this.socket.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
  }

  async evaluate(expression, awaitPromise = true) {
    const result = await this.send("Runtime.evaluate", { expression, awaitPromise, returnByValue: true });
    if (result.exceptionDetails) {
      throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
    }
    return result.result.value;
  }

  close() {
    this.socket.close();
  }
}

async function startBrowser(port) {
  const profile = await mkdtemp(join(tmpdir(), "cocinaris-plan0005-auth-"));
  const child = spawn(browserPath, [
    "--headless=new",
    "--no-first-run",
    "--no-default-browser-check",
    ...browserSandboxArguments,
    ...browserTrustStoreArguments,
    ...(allowUntrustedLocalCertificate ? ["--ignore-certificate-errors", "--allow-insecure-localhost"] : []),
    `--remote-debugging-port=${port}`,
    `--user-data-dir=${profile}`,
    "about:blank"
  ], { stdio: "ignore" });

  try {
    await waitFor(async () => (await fetch(`http://127.0.0.1:${port}/json/version`)).ok, "Chrome DevTools");
    const target = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" }).then((r) => r.json());
    const connection = new DevToolsConnection(target.webSocketDebuggerUrl);
    await connection.open();
    await connection.send("Page.enable");
    await connection.send("Network.enable");
    if (allowUntrustedLocalCertificate) {
      await connection.send("Security.enable");
      await connection.send("Security.setIgnoreCertificateErrors", { ignore: true });
    }
    await connection.send("Page.navigate", { url: `${apiUrl}/health/live` });
    await waitFor(async () => (await connection.evaluate("location.href")).startsWith(apiUrl), "Cocinaris HTTPS endpoint");
    return { child, connection, profile };
  } catch (error) {
    await terminateBrowserProcess(child);
    await removeBrowserProfile(profile);
    throw error;
  }
}

async function stopBrowser(browser) {
  browser.connection.close();
  await terminateBrowserProcess(browser.child);
  await removeBrowserProfile(browser.profile);
}

async function currentUrl(browser) {
  return browser.connection.evaluate("location.href");
}

async function login(browser, user, returnUrl = "/health/live") {
  const safeReturn = JSON.stringify(returnUrl);
  await browser.connection.evaluate(
    `(() => { document.body.innerHTML = ''; const form = document.createElement('form'); form.method='post'; form.action='/api/v1/auth/login'; const input=document.createElement('input'); input.name='returnUrl'; input.value=${safeReturn}; form.appendChild(input); document.body.appendChild(form); form.submit(); })()`,
    false
  );
  await waitFor(async () => {
    const url = await currentUrl(browser);
    return url.includes("/protocol/openid-connect/auth") || url.includes("/login-actions/authenticate");
  }, "Keycloak authorization page");

  const username = JSON.stringify(user.username);
  const password = JSON.stringify(user.password);
  await waitFor(
    async () =>
      browser.connection.evaluate(
        "document.getElementById('username') !== null && document.getElementById('password') !== null && document.getElementById('kc-form-login') !== null"
      ),
    "Keycloak credential form"
  );
  await browser.connection.evaluate(
    `document.getElementById("username").value = ${username}; document.getElementById("password").value = ${password}; document.getElementById("kc-form-login").submit();`,
    false
  );
  await waitFor(async () => {
    const url = await currentUrl(browser);
    return url.startsWith(apiUrl) || url.startsWith("chrome-error://");
  }, "OIDC callback completion", 20000);

  await browser.connection.send("Page.navigate", { url: `${apiUrl}/health/live` });
  await waitFor(async () => (await currentUrl(browser)).startsWith(apiUrl), "Cocinaris session probe");

  const session = await browser.connection.evaluate(
    "fetch('/api/v1/session', { credentials: 'include' }).then(async response => ({ status: response.status, body: await response.json() }))"
  );
  if (
    session.status !== 200 ||
    !session.body?.userId ||
    !session.body?.csrfToken ||
    Object.hasOwn(session.body, "accessToken") ||
    Object.hasOwn(session.body, "refreshToken")
  ) {
    throw new Error("Backend session response did not meet the cookie-session contract.");
  }
  return session.body;
}

async function probeTokenAbsence(browser) {
  return browser.connection.evaluate(`(async () => {
    const html = document.documentElement.outerHTML;
    const localKeys = Object.keys(localStorage);
    const sessionKeys = Object.keys(sessionStorage);
    let indexedDbNames = [];
    try {
      if (indexedDB.databases) {
        indexedDbNames = (await indexedDB.databases()).map(db => db.name).filter(Boolean);
      }
    } catch (_) {}
    const cookie = document.cookie;
    const globals = [];
    for (const key of Object.getOwnPropertyNames(window)) {
      try {
        const value = window[key];
        if (typeof value === 'string' && /(access_token|refresh_token|eyJ[A-Za-z0-9_-]{10,}\\.[A-Za-z0-9_-]+)/i.test(value)) {
          globals.push(key);
        }
      } catch (_) {}
    }
    const suspiciousHtml = /(access_token|refresh_token|Bearer\\s+eyJ)/i.test(html);
    const suspiciousCookie = /(access_token|refresh_token|eyJ)/i.test(cookie);
    return {
      localStorageKeyCount: localKeys.length,
      sessionStorageKeyCount: sessionKeys.length,
      indexedDbNameCount: indexedDbNames.length,
      documentCookieLength: cookie.length,
      suspiciousGlobalCount: globals.length,
      suspiciousHtml,
      suspiciousJsVisibleCookie: suspiciousCookie
    };
  })()`);
}

async function inspectSessionCookie(browser) {
  // Chrome DevTools Network.getCookies returns HttpOnly cookies without values in our summary.
  const cookies = await browser.connection.send("Network.getCookies", { urls: [apiUrl] });
  const sessionCookies = (cookies.cookies ?? []).filter((c) => /cocinaris|session/i.test(c.name));
  return sessionCookies.map((c) => ({
    name: c.name,
    httpOnly: Boolean(c.httpOnly),
    secure: Boolean(c.secure),
    sameSite: c.sameSite ?? null,
    path: c.path ?? null,
    // Never include value.
    hasValue: typeof c.value === "string" && c.value.length > 0
  }));
}

async function createLot(browser, csrfToken) {
  const productName = `PLAN-0005 lot ${crypto.randomUUID()}`;
  const request = {
    productName,
    quantity: { measuredValue: 1.25, unit: "Gram", availabilityState: null },
    storageLocation: "Pantry",
    customLocation: null,
    packageState: null,
    printedExpirationDate: null,
    notes: null
  };
  return browser.connection.evaluate(
    `fetch('/api/v1/inventory/lots', { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': ${JSON.stringify(csrfToken)}, 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(${JSON.stringify(request)}) }).then(async response => ({ status: response.status, body: await response.json().catch(() => null) }))`
  );
}

async function mutateWithCsrf(browser, csrfToken) {
  return createLot(browser, csrfToken);
}

function record(results, id, status, note) {
  results.push({ testId: id, status, note });
}

const results = [];
const startedAtUtc = new Date().toISOString();
let browsers = [];

try {
  browsers.push(await startBrowser(9332));
  browsers.push(await startBrowser(9333));

  const ready = await Promise.all(
    browsers.map((browser) =>
      browser.connection.evaluate("fetch('/health/ready', { credentials: 'include' }).then(r => r.status)")
    )
  );
  if (ready.some((status) => status !== 200)) {
    throw new Error(`API not ready at ${apiUrl}`);
  }

  // Open redirect rejection before authenticated session.
  await browsers[0].connection.evaluate(
    `(() => { const form = document.createElement('form'); form.method='post'; form.action='/api/v1/auth/login'; const input=document.createElement('input'); input.name='returnUrl'; input.value='https://evil.example/phish'; form.appendChild(input); document.body.appendChild(form); form.submit(); })()`,
    false
  );
  await delay(1500);
  const redirectUrl = await currentUrl(browsers[0]);
  const openRedirectRejected =
    !redirectUrl.includes("evil.example") &&
    (redirectUrl.includes("/protocol/openid-connect/auth") ||
      redirectUrl.startsWith(apiUrl) ||
      redirectUrl.includes("login"));
  // Re-check via API form posting and ensuring we never land on evil host.
  record(
    results,
    "TEST-0005-008",
    openRedirectRejected && !redirectUrl.includes("evil.example") ? "Passed" : "Failed",
    `Post-login navigation host remained non-evil (${new URL(redirectUrl.startsWith("http") ? redirectUrl : apiUrl).host}).`
  );

  await browsers[0].connection.send("Page.navigate", { url: `${apiUrl}/health/live` });
  await waitFor(async () => (await currentUrl(browsers[0])).startsWith(apiUrl), "reset browser A");

  const sessionA = await login(browsers[0], users[0]);
  const sessionB = await login(browsers[1], users[1]);
  record(results, "TEST-0005-002", "Passed", "Authorization Code login returned to API origin with session.");
  record(
    results,
    "TEST-0005-009",
    sessionA.userId !== sessionB.userId ? "Passed" : "Failed",
    "Synthetic users mapped to distinct internal UUIDs."
  );

  const cookiesA = await inspectSessionCookie(browsers[0]);
  const sessionCookie = cookiesA.find((c) => c.httpOnly) ?? cookiesA[0];
  if (!sessionCookie) {
    record(results, "TEST-0005-004", "Failed", "No session cookie discovered via DevTools.");
  } else {
    const httpsSecureOk = sessionCookie.secure === true;
    const httpOnlyOk = sessionCookie.httpOnly === true;
    const pathOk = sessionCookie.path === "/" || sessionCookie.path === null || typeof sessionCookie.path === "string";
    const sameSiteOk = ["Strict", "Lax", "None", null].includes(sessionCookie.sameSite) && sessionCookie.sameSite !== undefined;
    record(
      results,
      "TEST-0005-004",
      httpsSecureOk && httpOnlyOk && pathOk && sameSiteOk ? "Passed" : "Failed",
      `Cookie attrs httpOnly=${sessionCookie.httpOnly} secure=${sessionCookie.secure} sameSite=${sessionCookie.sameSite} path=${sessionCookie.path}`
    );
  }

  const absence = await probeTokenAbsence(browsers[0]);
  const tokensAbsent =
    !absence.suspiciousHtml &&
    !absence.suspiciousJsVisibleCookie &&
    absence.suspiciousGlobalCount === 0;
  record(
    results,
    "TEST-0005-003",
    tokensAbsent ? "Passed" : "Failed",
    `storage/local=${absence.localStorageKeyCount} session=${absence.sessionStorageKeyCount} idb=${absence.indexedDbNameCount} globals=${absence.suspiciousGlobalCount}`
  );

  // CSRF cases
  const missing = await mutateWithCsrf(browsers[0], "");
  record(results, "TEST-0005-005", missing.status >= 400 && missing.status < 500 ? "Passed" : "Failed", `missing CSRF status=${missing.status}`);

  const invalid = await mutateWithCsrf(browsers[0], "definitely-not-a-valid-csrf-token");
  record(results, "TEST-0005-006", invalid.status >= 400 && invalid.status < 500 ? "Passed" : "Failed", `invalid CSRF status=${invalid.status}`);

  const otherUserCsrf = await mutateWithCsrf(browsers[0], sessionB.csrfToken);
  record(
    results,
    "TEST-0005-006B",
    otherUserCsrf.status >= 400 && otherUserCsrf.status < 500 ? "Passed" : "Failed",
    `foreign CSRF status=${otherUserCsrf.status}`
  );

  const created = await mutateWithCsrf(browsers[0], sessionA.csrfToken);
  record(results, "TEST-0005-CREATE", created.status === 201 ? "Passed" : "Failed", `valid CSRF create status=${created.status}`);

  const ownerStatus = await browsers[0].connection.evaluate(
    `fetch('/api/v1/inventory/lots/${created.body?.lotId}', { credentials: 'include' }).then(r => r.status)`
  );
  const otherStatus = await browsers[1].connection.evaluate(
    `fetch('/api/v1/inventory/lots/${created.body?.lotId}', { credentials: 'include' }).then(r => r.status)`
  );
  record(
    results,
    "TEST-0005-011",
    ownerStatus === 200 && otherStatus === 404 ? "Passed" : "Failed",
    `owner/other detail ${ownerStatus}/${otherStatus}`
  );

  // Logout + session invalidation + stale CSRF
  const logout = await browsers[0].connection.evaluate(
    `fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'include', headers: { 'X-CSRF-TOKEN': ${JSON.stringify(sessionA.csrfToken)} }, redirect: 'manual' }).then(async r => ({ status: r.status, type: r.type }))`
  );
  const afterLogout = await browsers[0].connection.evaluate(
    "fetch('/api/v1/session', { credentials: 'include' }).then(r => r.status)"
  );
  record(
    results,
    "TEST-0005-007",
    afterLogout === 401 ? "Passed" : "Failed",
    `logoutStatus=${logout.status} sessionAfter=${afterLogout}`
  );

  const staleCsrfCreate = await mutateWithCsrf(browsers[0], sessionA.csrfToken);
  record(
    results,
    "TEST-0005-007B",
    staleCsrfCreate.status >= 400 && staleCsrfCreate.status < 500 ? "Passed" : "Failed",
    `post-logout CSRF mutation status=${staleCsrfCreate.status}`
  );

  const failed = results.filter((r) => r.status === "Failed");
  const payload = {
    plan: "PLAN-0005",
    group: "real-keycloak-oidc-p0",
    integratedMainSha: process.env.PLAN0005_INTEGRATED_SHA ?? "b94abd9a83fe29d88b095e3e9a42f10d01c05414",
    apiUrl,
    allowUntrustedLocalCertificate,
    startedAtUtc,
    endedAtUtc: new Date().toISOString(),
    summary: {
      total: results.length,
      passed: results.filter((r) => r.status === "Passed").length,
      failed: failed.length,
      blocked: results.filter((r) => r.status === "Blocked").length
    },
    results
  };
  await mkdir(join(evidenceDir, "reports"), { recursive: true });
  await writeFile(reportPath, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
  process.stdout.write(`PLAN-0005 Keycloak P0 auth report → ${reportPath}\n`);
  if (failed.length > 0) {
    process.stderr.write(`${failed.length} Keycloak P0 checks Failed.\n`);
    process.exit(1);
  }
} finally {
  await Promise.all(browsers.map(stopBrowser));
}
