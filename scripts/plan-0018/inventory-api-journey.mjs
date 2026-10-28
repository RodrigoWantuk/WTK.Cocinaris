#!/usr/bin/env node
/**
 * PLAN-0018 authenticated inventory API journey (real Keycloak session).
 * Pattern aligned with scripts/plan-0005/keycloak-p0-auth.mjs.
 * Never prints passwords, cookies, or tokens.
 */
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, "../..");
const evidenceDir =
  process.env.PLAN0018_EVIDENCE_DIR ??
  process.env.PLAN0005_EVIDENCE_DIR ??
  join(root, "docs/evidence/plan-0018");
const reportPath = join(evidenceDir, "production-inventory-journey-result.json");
const apiUrl = process.env.COCINARIS_SMOKE_API_URL ?? "https://localhost:7443";
const browserPath = process.env.COCINARIS_SMOKE_BROWSER ?? "google-chrome";
const allowUntrustedLocalCertificate =
  process.env.COCINARIS_SMOKE_ALLOW_UNTRUSTED_LOCAL_CERTIFICATE === "1";
const browserSandboxArguments = process.getuid?.() === 0 ? ["--no-sandbox"] : [];
const browserTrustStoreArguments =
  process.platform === "linux" ? ["--disable-features=ChromeRootStoreUsed"] : [];
const users = [
  {
    username: process.env.COCINARIS_SMOKE_USER_A ?? "inventory-user-a",
    password: process.env.COCINARIS_SMOKE_PASSWORD_A,
  },
  {
    username: process.env.COCINARIS_SMOKE_USER_B ?? "inventory-user-b",
    password: process.env.COCINARIS_SMOKE_PASSWORD_B,
  },
];
const sutSha = process.env.PLAN0016_SUT_SHA || process.env.PLAN0018_SUT_SHA || "UNKNOWN";

if (users.some((user) => !user.password)) {
  process.stderr.write(
    "COCINARIS_SMOKE_PASSWORD_A and COCINARIS_SMOKE_PASSWORD_B are required.\n",
  );
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
  throw new Error(
    `Timed out waiting for ${description}${lastError ? `: ${lastError.message}` : ""}.`,
  );
}

async function terminateBrowserProcess(child) {
  if (child.exitCode !== null || child.signalCode !== null) return;
  child.kill("SIGTERM");
  const exited = await Promise.race([
    new Promise((resolve) => child.once("exit", () => resolve(true))),
    delay(5000).then(() => false),
  ]);
  if (!exited && child.exitCode === null && child.signalCode !== null) {
    child.kill("SIGKILL");
    await Promise.race([
      new Promise((resolve) => child.once("exit", resolve)),
      delay(5000),
    ]);
  }
}

async function removeBrowserProfile(profile) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      await rm(profile, {
        recursive: true,
        force: true,
        maxRetries: 5,
        retryDelay: 200,
      });
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
    this.socket.addEventListener("message", (event) => {
      const message = JSON.parse(event.data);
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
      this.socket.addEventListener(
        "error",
        () => reject(new Error("Could not connect to Chrome DevTools.")),
        { once: true },
      );
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    this.socket.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) =>
      this.pending.set(id, { resolve, reject }),
    );
  }

  async evaluate(expression, awaitPromise = true) {
    const result = await this.send("Runtime.evaluate", {
      expression,
      awaitPromise,
      returnByValue: true,
    });
    if (result.exceptionDetails) {
      throw new Error(
        result.exceptionDetails.exception?.description ??
          result.exceptionDetails.text,
      );
    }
    return result.result.value;
  }

  close() {
    this.socket.close();
  }
}

async function startBrowser(port) {
  const profile = await mkdtemp(join(tmpdir(), "cocinaris-plan0018-inv-"));
  const child = spawn(
    browserPath,
    [
      "--headless=new",
      "--no-first-run",
      "--no-default-browser-check",
      ...browserSandboxArguments,
      ...browserTrustStoreArguments,
      ...(allowUntrustedLocalCertificate
        ? ["--ignore-certificate-errors", "--allow-insecure-localhost"]
        : []),
      `--remote-debugging-port=${port}`,
      `--user-data-dir=${profile}`,
      "about:blank",
    ],
    { stdio: "ignore" },
  );

  try {
    await waitFor(
      async () => (await fetch(`http://127.0.0.1:${port}/json/version`)).ok,
      "Chrome DevTools",
    );
    const target = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {
      method: "PUT",
    }).then((r) => r.json());
    const connection = new DevToolsConnection(target.webSocketDebuggerUrl);
    await connection.open();
    await connection.send("Page.enable");
    await connection.send("Network.enable");
    if (allowUntrustedLocalCertificate) {
      await connection.send("Security.enable");
      await connection.send("Security.setIgnoreCertificateErrors", {
        ignore: true,
      });
    }
    await connection.send("Page.navigate", { url: `${apiUrl}/health/live` });
    await waitFor(
      async () => (await connection.evaluate("location.href")).startsWith(apiUrl),
      "Cocinaris HTTPS endpoint",
    );
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
    false,
  );
  await waitFor(async () => {
    const url = await currentUrl(browser);
    return (
      url.includes("/protocol/openid-connect/auth") ||
      url.includes("/login-actions/authenticate")
    );
  }, "Keycloak authorization page");

  const username = JSON.stringify(user.username);
  const password = JSON.stringify(user.password);
  await waitFor(
    async () =>
      browser.connection.evaluate(
        "document.getElementById('username') !== null && document.getElementById('password') !== null && document.getElementById('kc-form-login') !== null",
      ),
    "Keycloak credential form",
  );
  await browser.connection.evaluate(
    `document.getElementById("username").value = ${username}; document.getElementById("password").value = ${password}; document.getElementById("kc-form-login").submit();`,
    false,
  );
  await waitFor(async () => {
    const url = await currentUrl(browser);
    return url.startsWith(apiUrl) || url.startsWith("chrome-error://");
  }, "OIDC callback completion", 20000);

  await browser.connection.send("Page.navigate", {
    url: `${apiUrl}/health/live`,
  });
  await waitFor(
    async () => (await currentUrl(browser)).startsWith(apiUrl),
    "Cocinaris session probe",
  );

  const session = await browser.connection.evaluate(
    "fetch('/api/v1/session', { credentials: 'include' }).then(async response => ({ status: response.status, body: await response.json() }))",
  );
  if (
    session.status !== 200 ||
    !session.body?.userId ||
    !session.body?.csrfToken
  ) {
    throw new Error("Backend session response did not meet the cookie-session contract.");
  }
  return session.body;
}

async function api(
  browser,
  path,
  { method = "GET", csrf, etag, idem, body } = {},
) {
  return browser.connection.evaluate(
    `fetch(${JSON.stringify(path)}, {
      method: ${JSON.stringify(method)},
      credentials: 'include',
      headers: ${JSON.stringify({
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
        ...(csrf ? { "X-CSRF-TOKEN": csrf } : {}),
        ...(etag ? { "If-Match": etag } : {}),
        ...(idem ? { "Idempotency-Key": idem } : {}),
      })},
      body: ${body === undefined ? "undefined" : JSON.stringify(JSON.stringify(body))}
    }).then(async r => {
      const text = await r.text();
      let json = null;
      try { json = text ? JSON.parse(text) : null; } catch {}
      return { status: r.status, etag: r.headers.get('etag'), body: json };
    })`,
  );
}

function record(results, testId, status, note) {
  results.push({ testId, status, note });
}

const results = [];
const startedAtUtc = new Date().toISOString();
let browsers = [];

try {
  browsers.push(await startBrowser(9340));
  browsers.push(await startBrowser(9341));
  const sessionA = await login(browsers[0], users[0]);
  const sessionB = await login(browsers[1], users[1]);

  const createSpecs = [
    [
      "measured-gram",
      {
        productName: "Arroz PLAN0018",
        quantity: {
          measuredValue: 1250.5,
          unit: "Gram",
          availabilityState: null,
        },
        storageLocation: "Pantry",
        customLocation: null,
        packageState: "Sealed",
        printedExpirationDate: "2026-12-31",
        notes: "nota cafe",
      },
    ],
    [
      "measured-ml",
      {
        productName: "Leite PLAN0018",
        quantity: {
          measuredValue: 500,
          unit: "Milliliter",
          availabilityState: null,
        },
        storageLocation: "Refrigerator",
        customLocation: null,
        packageState: "Opened",
        printedExpirationDate: "2026-08-15",
        notes: null,
      },
    ],
    [
      "measured-unit",
      {
        productName: "Ovos PLAN0018",
        quantity: { measuredValue: 12, unit: "Unit", availabilityState: null },
        storageLocation: "Freezer",
        customLocation: null,
        packageState: "Unknown",
        printedExpirationDate: null,
        notes: null,
      },
    ],
    [
      "qual-available",
      {
        productName: "Sal PLAN0018",
        quantity: {
          measuredValue: null,
          unit: null,
          availabilityState: "Available",
        },
        storageLocation: "Pantry",
        customLocation: null,
        packageState: null,
        printedExpirationDate: null,
        notes: null,
      },
    ],
    [
      "qual-low",
      {
        productName: "Pimenta PLAN0018",
        quantity: { measuredValue: null, unit: null, availabilityState: "Low" },
        storageLocation: "Pantry",
        customLocation: null,
        packageState: null,
        printedExpirationDate: null,
        notes: null,
      },
    ],
    [
      "other-custom",
      {
        productName: "Ervas PLAN0018",
        quantity: {
          measuredValue: 100,
          unit: "Gram",
          availabilityState: null,
        },
        storageLocation: "Other",
        customLocation: "Garage shelf",
        packageState: "Sealed",
        printedExpirationDate: "2027-01-01",
        notes: null,
      },
    ],
  ];

  const created = {};
  for (const [id, body] of createSpecs) {
    const res = await api(browsers[0], "/api/v1/inventory/lots", {
      method: "POST",
      csrf: sessionA.csrfToken,
      idem: randomUUID(),
      body,
    });
    const ok = res.status === 201 && res.body?.lotId && res.etag;
    record(
      results,
      `TEST-0018-INV-CREATE-${id}`,
      ok ? "Passed" : "Failed",
      `status=${res.status}`,
    );
    if (ok) created[id] = { ...res.body, etag: res.etag };
  }

  const missingOther = await api(browsers[0], "/api/v1/inventory/lots", {
    method: "POST",
    csrf: sessionA.csrfToken,
    idem: randomUUID(),
    body: {
      productName: "Bad Other",
      quantity: { measuredValue: 1, unit: "Gram", availabilityState: null },
      storageLocation: "Other",
      customLocation: null,
      packageState: null,
      printedExpirationDate: null,
      notes: null,
    },
  });
  record(
    results,
    "TEST-0018-INV-OTHER-REQUIRED",
    missingOther.status === 400 || missingOther.status === 422 ? "Passed" : "Failed",
    `status=${missingOther.status}${missingOther.status === 422 ? " (validation rejected missing customLocation; accepted as fail-closed)" : ""}`,
  );

  const list = await api(browsers[0], "/api/v1/inventory/lots?pageSize=50");
  record(
    results,
    "TEST-0018-INV-LIST",
    list.status === 200 && Array.isArray(list.body?.items) ? "Passed" : "Failed",
    `status=${list.status} count=${list.body?.items?.length}`,
  );

  const search = await api(
    browsers[0],
    `/api/v1/inventory/lots?search=${encodeURIComponent("Arroz")}&pageSize=20`,
  );
  record(
    results,
    "TEST-0018-INV-SEARCH",
    search.status === 200 &&
      (search.body?.items || []).some((i) => i.productName.includes("Arroz"))
      ? "Passed"
      : "Failed",
    `status=${search.status}`,
  );

  const gram = created["measured-gram"];
  if (gram) {
    let etag = gram.etag;
    for (const type of ["Consume", "Discard", "Correct"]) {
      const value = type === "Correct" ? 900 : 10;
      const adj = await api(
        browsers[0],
        `/api/v1/inventory/lots/${gram.lotId}/adjustments`,
        {
          method: "POST",
          csrf: sessionA.csrfToken,
          etag,
          idem: randomUUID(),
          body: {
            type,
            value,
            availabilityState: null,
            reasonCode: `plan0018_${type.toLowerCase()}`,
            note: null,
          },
        },
      );
      record(
        results,
        `TEST-0018-INV-ADJ-${type}`,
        adj.status === 200 && adj.etag ? "Passed" : "Failed",
        `status=${adj.status}`,
      );
      if (adj.status === 200 && adj.etag) etag = adj.etag;
    }
    gram.etag = etag;

    const hist = await api(
      browsers[0],
      `/api/v1/inventory/lots/${gram.lotId}/history`,
    );
    record(
      results,
      "TEST-0018-INV-HISTORY",
      hist.status === 200 && (hist.body?.length ?? 0) > 0 ? "Passed" : "Failed",
      `status=${hist.status} entries=${Array.isArray(hist.body) ? hist.body.length : "n/a"}`,
    );

    const noMatch = await api(
      browsers[0],
      `/api/v1/inventory/lots/${gram.lotId}/adjustments`,
      {
        method: "POST",
        csrf: sessionA.csrfToken,
        idem: randomUUID(),
        body: {
          type: "Consume",
          value: 1,
          availabilityState: null,
          reasonCode: "plan0018_nomatch",
          note: null,
        },
      },
    );
    record(
      results,
      "TEST-0018-INV-428",
      noMatch.status === 428 ? "Passed" : "Failed",
      `status=${noMatch.status}`,
    );

    const stale = await api(
      browsers[0],
      `/api/v1/inventory/lots/${gram.lotId}/adjustments`,
      {
        method: "POST",
        csrf: sessionA.csrfToken,
        etag: '"stale-etag"',
        idem: randomUUID(),
        body: {
          type: "Consume",
          value: 1,
          availabilityState: null,
          reasonCode: "plan0018_stale",
          note: null,
        },
      },
    );
    record(
      results,
      "TEST-0018-INV-412",
      stale.status === 412 ? "Passed" : "Failed",
      `status=${stale.status}`,
    );

    const del = await api(browsers[0], `/api/v1/inventory/lots/${gram.lotId}`, {
      method: "DELETE",
      csrf: sessionA.csrfToken,
      etag: gram.etag,
    });
    record(
      results,
      "TEST-0018-INV-DELETE",
      del.status === 204 || del.status === 200 ? "Passed" : "Failed",
      `status=${del.status}`,
    );
  }

  const sal = created["qual-available"];
  if (sal) {
    const adj = await api(
      browsers[0],
      `/api/v1/inventory/lots/${sal.lotId}/adjustments`,
      {
        method: "POST",
        csrf: sessionA.csrfToken,
        etag: sal.etag,
        idem: randomUUID(),
        body: {
          type: "AvailabilityChanged",
          value: null,
          availabilityState: "Low",
          reasonCode: "plan0018_avail",
          note: null,
        },
      },
    );
    record(
      results,
      "TEST-0018-INV-AVAIL",
      adj.status === 200 ? "Passed" : "Failed",
      `status=${adj.status}`,
    );
  }

  const herbs = created["other-custom"];
  if (herbs) {
    const foreign = await api(
      browsers[1],
      `/api/v1/inventory/lots/${herbs.lotId}`,
    );
    record(
      results,
      "TEST-0018-INV-ISOLATION-DETAIL",
      foreign.status === 404 ? "Passed" : "Failed",
      `userB detail status=${foreign.status}`,
    );
    const mutate = await api(
      browsers[1],
      `/api/v1/inventory/lots/${herbs.lotId}/adjustments`,
      {
        method: "POST",
        csrf: sessionB.csrfToken,
        etag: '"v1"',
        idem: randomUUID(),
        body: {
          type: "Consume",
          value: 1,
          availabilityState: null,
          reasonCode: "evil",
          note: null,
        },
      },
    );
    record(
      results,
      "TEST-0018-INV-ISOLATION-MUTATE",
      mutate.status === 404 || mutate.status === 403 ? "Passed" : "Failed",
      `userB mutate status=${mutate.status}`,
    );
    const detail = await api(
      browsers[0],
      `/api/v1/inventory/lots/${herbs.lotId}`,
    );
    record(
      results,
      "TEST-0018-INV-CUSTOM-LOCATION",
      detail.body?.customLocation === "Garage shelf" &&
        detail.body?.storageLocation === "Other"
        ? "Passed"
        : "Failed",
      `custom=${detail.body?.customLocation}`,
    );
  }

  if (created["measured-ml"]) {
    const detail = await api(
      browsers[0],
      `/api/v1/inventory/lots/${created["measured-ml"].lotId}`,
    );
    record(
      results,
      "TEST-0018-INV-PRINTED-DATE",
      detail.body?.printedExpirationDate === "2026-08-15" ? "Passed" : "Failed",
      `printed=${detail.body?.printedExpirationDate}`,
    );
  }
} catch (error) {
  record(results, "TEST-0018-INV-FATAL", "Failed", String(error.message || error));
} finally {
  for (const browser of browsers) {
    try {
      await stopBrowser(browser);
    } catch {
      /* ignore */
    }
  }
}

const failed = results.filter((r) => r.status === "Failed").length;
const passed = results.filter((r) => r.status === "Passed").length;
const payload = {
  testId: "TEST-0018-004-production-inventory-journey",
  exactShaUnderTest: sutSha,
  apiUrl,
  allowUntrustedLocalCertificate,
  startedAtUtc,
  endedAtUtc: new Date().toISOString(),
  summary: { total: results.length, passed, failed },
  results,
  status: failed === 0 && passed > 0 ? "Passed" : "Failed",
  topologyNote:
    "Real Keycloak Authorization Code session on API origin. SPA same-origin UI click-path is additional evidence; API journey proves inventory contract under authenticated BFF cookies.",
};
await mkdir(evidenceDir, { recursive: true });
await writeFile(reportPath, `${JSON.stringify(payload, null, 2)}\n`);
await writeFile(
  join(evidenceDir, "reports", "production-inventory-journey-result.json"),
  `${JSON.stringify(payload, null, 2)}\n`,
);
console.log(`Inventory journey → ${reportPath} status=${payload.status}`);
process.exit(failed === 0 && passed > 0 ? 0 : 1);
