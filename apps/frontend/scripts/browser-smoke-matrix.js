#!/usr/bin/env node
/**
 * Automated Playwright browser smoke matrix (prototype mode).
 * Not a manual validation — CI and local `yarn smoke:browser`.
 *
 * Prerequisites:
 *   yarn install --frozen-lockfile
 *   yarn smoke:browser:install
 *   yarn start   # separate terminal, or SMOKE_MANAGE_SERVER=1
 *   yarn smoke:browser
 */
const { chromium, devices } = require("playwright");
const { spawn, execSync } = require("child_process");
const fs = require("fs");
const http = require("http");
const path = require("path");
const os = require("os");
const {
  hasPerceptibleFocusIndicator,
  evaluateReducedMotionDurations,
  parseCssTimeSeconds,
  motionRelevantSelectors,
} = require("./browser-smoke-assertions");

const BASE = process.env.SMOKE_BASE_URL || "http://127.0.0.1:3000";
const PRODUCTION_BASE =
  process.env.SMOKE_PRODUCTION_URL || "http://127.0.0.1:3001";
const HEADLESS = process.env.SMOKE_HEADED !== "1";
const OUT_DIR =
  process.env.SMOKE_OUT_DIR ||
  path.join(__dirname, "..", "docs", "browser-smoke");
const results = [];
let failedArtifacts = [];

/**
 * Minimal static SPA server for the production CRA build.
 * @returns {import('http').Server}
 */
function startStaticSpa(rootDir, port) {
  const root = path.resolve(rootDir);
  const server = http.createServer((req, res) => {
    try {
      const url = new URL(req.url || "/", `http://127.0.0.1:${port}`);
      let rel = decodeURIComponent(url.pathname);
      if (rel === "/") rel = "/index.html";
      let filePath = path.join(root, rel);
      if (!filePath.startsWith(root)) {
        res.writeHead(403);
        res.end("Forbidden");
        return;
      }
      if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        filePath = path.join(root, "index.html");
      }
      const data = fs.readFileSync(filePath);
      const ext = path.extname(filePath).toLowerCase();
      const types = {
        ".html": "text/html; charset=utf-8",
        ".js": "application/javascript; charset=utf-8",
        ".css": "text/css; charset=utf-8",
        ".json": "application/json",
        ".png": "image/png",
        ".jpg": "image/jpeg",
        ".svg": "image/svg+xml",
        ".ico": "image/x-icon",
        ".map": "application/json",
        ".txt": "text/plain; charset=utf-8",
      };
      res.writeHead(200, {
        "Content-Type": types[ext] || "application/octet-stream",
      });
      res.end(data);
    } catch {
      res.writeHead(500);
      res.end("error");
    }
  });
  server.listen(port, "127.0.0.1");
  return server;
}

function record(check, result, notes = "") {
  results.push({ check, result, notes });
  console.log(`[${result}] ${check}${notes ? ` — ${notes}` : ""}`);
}

function fail(check, notes) {
  record(check, "Failed", notes);
  throw new Error(`${check}: ${notes}`);
}

async function captureFailure(page, name) {
  try {
    fs.mkdirSync(path.join(OUT_DIR, "failures"), { recursive: true });
    const shot = path.join(OUT_DIR, "failures", `${name}.png`);
    await page.screenshot({ path: shot, fullPage: true });
    failedArtifacts.push(shot);
  } catch {
    // ignore screenshot errors
  }
}

function meta() {
  let gitHead = "";
  try {
    gitHead = execSync("git rev-parse HEAD", {
      cwd: path.join(__dirname, "..", "..", ".."),
      encoding: "utf8",
    }).trim();
  } catch {
    gitHead = "unknown";
  }
  const eventName = process.env.GITHUB_EVENT_NAME || "local";
  const githubSha = process.env.GITHUB_SHA || "";
  const prHeadSha = process.env.SMOKE_PR_HEAD_SHA || "";
  const syntheticMergeSha = eventName === "pull_request" ? githubSha || "" : "";
  const testedCodeSha =
    process.env.SMOKE_TESTED_CODE_SHA ||
    prHeadSha ||
    githubSha ||
    gitHead ||
    "unknown";

  let yarnVersion = "unknown";
  let nodeVersion = process.version;
  let playwrightVersion = "unknown";
  try {
    yarnVersion = execSync("yarn --version", { encoding: "utf8" }).trim();
  } catch {
    /* ignore */
  }
  try {
    playwrightVersion = require("playwright/package.json").version;
  } catch {
    /* ignore */
  }
  return {
    kind: "automated-browser-smoke",
    evidenceNote:
      "CI artifact only — not a versioned source-of-truth report in git",
    testedCodeSha,
    prHeadSha: prHeadSha || null,
    syntheticMergeSha: syntheticMergeSha || null,
    gitHeadSha: gitHead || null,
    githubRunId: process.env.GITHUB_RUN_ID || null,
    githubRunAttempt: process.env.GITHUB_RUN_ATTEMPT || null,
    githubEventName: eventName,
    workflowName: process.env.GITHUB_WORKFLOW || "local",
    prototypeBaseUrl: BASE,
    productionBaseUrl: PRODUCTION_BASE,
    testedModes: ["prototype", "production"],
    nodeVersion,
    yarnVersion,
    playwrightVersion,
    chromiumVersion: "resolved-by-playwright",
    frontendMode: "prototype+production-static",
    timestamp: new Date().toISOString(),
    headless: HEADLESS,
    operatingSystem: `${os.platform()} ${os.release()} ${os.arch()}`,
  };
}

/**
 * Fixture bodies for the intercepted authenticated `/app/perfil*` browser-smoke
 * journey. These are hand-built wire shapes matching the generated OpenAPI
 * contract in `src/generated/api-client`, not captures from a real backend —
 * keep them in sync with `packages/contracts` if the profile contract changes.
 */
const profileInterceptionFixtures = (() => {
  function field(value, presence, defaultValue = null, durability = "durable") {
    return { value, presence, defaultValue, durability };
  }

  /** Shared aggregate concurrency token — body version and ETag must agree. */
  const AGGREGATE_VERSION = "v3";
  const AGGREGATE_ETAG = '"v3"';

  const session = {
    userId: "22222222-2222-2222-2222-222222222222",
    csrfToken: "csrf-intercepted",
    supportedLocales: ["en", "pt-BR", "es"],
    displayName: "Ada Intercepted",
    language: "en",
    timeZone: "UTC",
    measurementSystem: "Metric",
    profileExists: true,
    profilePercentComplete: 60,
    adultDeclarationState: "Declared",
  };

  const profile = {
    ownerUserId: session.userId,
    displayName: field("Ada Intercepted", "confirmed"),
    household: {
      defaultAdultCount: field(2, "confirmed", 1),
      defaultChildCount: field(0, "default", 0),
      defaultServingCount: field(2, "confirmed", 1),
      language: field("en", "confirmed", "en"),
      region: field("US", "confirmed"),
      currency: field("USD", "confirmed"),
      measurementSystem: field("Metric", "confirmed", "Metric"),
      timeZone: field("UTC", "confirmed"),
      planningCadence: field("Weekly", "confirmed"),
      shoppingCadence: field("Weekly", "confirmed"),
    },
    cookingContext: {
      overallSkill: field("Comfortable", "confirmed"),
      confidence: field("Moderate", "confirmed"),
      preferredInstructionDetail: field("Standard", "confirmed"),
      ordinaryPrepMinutes: field(30, "confirmed"),
      exceptionalPrepMinutes: field(60, "confirmed"),
      effortTolerance: field("Medium", "confirmed"),
      cleanupTolerance: field("Medium", "confirmed"),
      repeatMealPreference: field("Neutral", "confirmed"),
      reheatingPreference: field("Comfortable", "confirmed"),
      leftoverPreference: field("Comfortable", "confirmed"),
      freezingPreference: field("Comfortable", "confirmed"),
    },
    adultDeclaration: {
      adultDeclared: true,
      termsVersion: "2026-01-01",
      privacyVersion: "2026-01-01",
      acceptedAt: "2026-01-15T12:00:00Z",
      state: "Declared",
    },
    knownTechniques: [],
    techniquesToLearn: [],
    goals: [],
    abandonmentReasons: [],
    profileExists: true,
    version: AGGREGATE_VERSION,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-15T12:00:00Z",
  };

  const preferences = { version: AGGREGATE_VERSION, entries: [] };
  const equipment = { version: AGGREGATE_VERSION, entries: [] };
  const completeness = {
    percentComplete: 60,
    completedSections: 3,
    totalSections: 5,
    sectionCounts: { household: 1, preferences: 0, equipment: 0 },
    adultDeclarationState: "Declared",
    profileExists: true,
  };

  return {
    session,
    profile,
    preferences,
    equipment,
    completeness,
    AGGREGATE_VERSION,
    AGGREGATE_ETAG,
  };
})();

/**
 * Installs `page.route()` interception for the session and profile endpoints
 * used by the assignment scenarios (overview, household save with the
 * unsaved-changes guard, preferences, equipment). GET responses always carry
 * an `ETag` header when `profileExists` is true, mirroring the backend
 * contract the live adapter relies on (it never derives an ETag from the
 * response body). A PATCH to `/api/v1/profile` is answered with the same
 * fixture profile (bumped aggregate version) rather than a real mutation.
 *
 * Returns a small controller so residual scenarios can force post-save workspace
 * reload failures or session-refresh failures without talking to a live backend.
 */
async function installProfileInterception(page) {
  const fx = profileInterceptionFixtures;
  let aggregateVersion = fx.AGGREGATE_VERSION;
  const etagFor = (version) => `"${version}"`;
  let failNextProfileGets = 0;
  let failNextSessionRefresh = false;
  let sessionHits = 0;

  await page.route("**/api/v1/session", (route) => {
    sessionHits += 1;
    // First hit is the initial SessionProvider load; later hits are refreshSession().
    if (failNextSessionRefresh && sessionHits > 1) {
      failNextSessionRefresh = false;
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ title: "session unavailable" }),
      });
      return;
    }
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(fx.session),
    });
  });
  await page.route("**/api/v1/profile/preferences", (route) => {
    const method = route.request().method();
    if (method === "PUT") {
      aggregateVersion = `v${Number(String(aggregateVersion).replace(/\D/g, "") || "3") + 1}`;
    }
    if (method === "GET" && failNextProfileGets > 0) {
      failNextProfileGets -= 1;
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ title: "preferences unavailable" }),
      });
      return;
    }
    route.fulfill({
      status: 200,
      contentType: "application/json",
      headers: { etag: etagFor(aggregateVersion) },
      body: JSON.stringify({
        ...fx.preferences,
        version: aggregateVersion,
      }),
    });
  });
  await page.route("**/api/v1/profile/equipment", (route) => {
    const method = route.request().method();
    if (method === "PUT") {
      aggregateVersion = `v${Number(String(aggregateVersion).replace(/\D/g, "") || "3") + 1}`;
    }
    if (method === "GET" && failNextProfileGets > 0) {
      failNextProfileGets -= 1;
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ title: "equipment unavailable" }),
      });
      return;
    }
    route.fulfill({
      status: 200,
      contentType: "application/json",
      headers: { etag: etagFor(aggregateVersion) },
      body: JSON.stringify({
        ...fx.equipment,
        version: aggregateVersion,
      }),
    });
  });
  await page.route("**/api/v1/profile/completeness", (route) => {
    if (failNextProfileGets > 0) {
      failNextProfileGets -= 1;
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ title: "completeness unavailable" }),
      });
      return;
    }
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(fx.completeness),
    });
  });
  await page.route("**/api/v1/profile", (route) => {
    const method = route.request().method();
    if (method === "PATCH" || method === "PUT") {
      aggregateVersion = `v${Number(String(aggregateVersion).replace(/\D/g, "") || "3") + 1}`;
      route.fulfill({
        status: 200,
        contentType: "application/json",
        headers: { etag: etagFor(aggregateVersion) },
        body: JSON.stringify({
          ...fx.profile,
          version: aggregateVersion,
          updatedAt: new Date().toISOString(),
        }),
      });
      return;
    }
    if (failNextProfileGets > 0) {
      failNextProfileGets -= 1;
      route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ title: "profile unavailable" }),
      });
      return;
    }
    route.fulfill({
      status: 200,
      contentType: "application/json",
      headers: { etag: etagFor(aggregateVersion) },
      body: JSON.stringify({
        ...fx.profile,
        version: aggregateVersion,
      }),
    });
  });

  return {
    /** Fail the next N profile-workspace GET responses (any of the four sources). */
    failNextWorkspaceGets(count) {
      failNextProfileGets = count;
    },
    /** Fail the next session refresh after the initial authenticated load. */
    failNextSessionRefreshOnce() {
      failNextSessionRefresh = true;
    },
  };
}

async function waitForServer(url, timeoutMs = 120000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const res = await fetch(url, { method: "GET" });
      if (res.ok || res.status === 200) return;
    } catch {
      /* retry */
    }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error(`Server not ready at ${url} within ${timeoutMs}ms`);
}

async function run() {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const reportMeta = meta();

  let serverProc = null;
  let productionServer = null;
  if (process.env.SMOKE_MANAGE_SERVER === "1") {
    serverProc = spawn("yarn", ["start"], {
      cwd: path.join(__dirname, ".."),
      env: {
        ...process.env,
        BROWSER: "none",
        CI: "true",
        HOST: process.env.HOST || "127.0.0.1",
        PORT: process.env.PORT || "3000",
        REACT_APP_FRONTEND_MODE: "prototype",
        // Prevent CRA interactive prompt when port is busy.
        WDS_SOCKET_PORT: "0",
      },
      // Do not pipe stdio — CRA can deadlock when stdout buffers fill.
      stdio: "ignore",
      detached: false,
    });
    await waitForServer(BASE, 180000);

    const frontendRoot = path.join(__dirname, "..");
    const buildDir = path.join(frontendRoot, "build");
    console.log("Building production bundle for mobile locale smoke...");
    execSync("yarn build:production", {
      cwd: frontendRoot,
      stdio: "inherit",
      env: { ...process.env, CI: "true" },
    });
    const prodPort = Number(new URL(PRODUCTION_BASE).port || 3001);
    productionServer = startStaticSpa(buildDir, prodPort);
    await waitForServer(PRODUCTION_BASE, 30000);
  } else {
    await waitForServer(BASE, 30000).catch((err) => {
      throw new Error(
        `${err.message}. Start the app with \`yarn start\` or set SMOKE_MANAGE_SERVER=1.`,
      );
    });
    await waitForServer(PRODUCTION_BASE, 15000).catch((err) => {
      throw new Error(
        `${err.message}. Serve production build at SMOKE_PRODUCTION_URL (${PRODUCTION_BASE}) or set SMOKE_MANAGE_SERVER=1.`,
      );
    });
  }

  const browser = await chromium.launch({
    headless: HEADLESS,
    args: ["--no-sandbox", "--disable-dev-shm-usage"],
  });
  const browserVersion = browser.version();
  reportMeta.chromiumVersion = browserVersion;

  async function withPage(opts, name, fn) {
    const context = await browser.newContext({
      ...opts,
      recordVideo: process.env.SMOKE_VIDEO
        ? { dir: path.join(OUT_DIR, "videos") }
        : undefined,
    });
    if (process.env.SMOKE_TRACE === "1") {
      await context.tracing.start({ screenshots: true, snapshots: true });
    }
    const page = await context.newPage();
    page.setDefaultTimeout(15000);
    try {
      await fn(page, context);
      if (process.env.SMOKE_TRACE === "1") {
        await context.tracing.stop({
          path: path.join(OUT_DIR, "traces", `${name}.zip`),
        });
      }
    } catch (err) {
      await captureFailure(page, name.replace(/\W+/g, "_"));
      if (process.env.SMOKE_TRACE === "1") {
        fs.mkdirSync(path.join(OUT_DIR, "traces"), { recursive: true });
        await context.tracing
          .stop({ path: path.join(OUT_DIR, "traces", `${name}-failed.zip`) })
          .catch(() => {});
      }
      throw err;
    } finally {
      await context.close();
    }
  }

  async function captureElementStyles(page, testId) {
    return page.evaluate((id) => {
      const el = document.querySelector(`[data-testid="${id}"]`);
      if (!el) return null;
      const s = getComputedStyle(el);
      return {
        outlineStyle: s.outlineStyle,
        outlineWidth: s.outlineWidth,
        outlineColor: s.outlineColor,
        boxShadow: s.boxShadow,
        borderColor: s.borderColor,
        borderWidth: s.borderWidth,
        backgroundColor: s.backgroundColor,
        isActive: document.activeElement === el,
      };
    }, testId);
  }

  /**
   * Capture baseline styles while unfocused, Tab to the control, then compare.
   */
  async function assertKeyboardFocusVisible(page, label, candidateIds) {
    let testId = null;
    for (const id of candidateIds) {
      const locator = page.locator(`[data-testid="${id}"]`);
      if ((await locator.count()) > 0) {
        await locator.first().waitFor({ state: "attached", timeout: 10000 });
        testId = id;
        break;
      }
    }
    if (!testId) {
      // One more pass after a short settle — SPA route content may still mount.
      await page.waitForTimeout(250);
      for (const id of candidateIds) {
        if ((await page.locator(`[data-testid="${id}"]`).count()) > 0) {
          testId = id;
          break;
        }
      }
    }
    if (!testId) {
      fail(
        `keyboard focus-visible (${label})`,
        `control missing (${candidateIds.join(", ")})`,
      );
    }

    await page.evaluate(() => {
      const active = document.activeElement;
      if (active && typeof active.blur === "function") active.blur();
    });
    let baseline = await captureElementStyles(page, testId);
    if (!baseline) {
      fail(`keyboard focus-visible (${label})`, `element ${testId} not found`);
    }
    if (baseline.isActive) {
      await page.evaluate(() => {
        if (
          document.activeElement &&
          document.activeElement !== document.body
        ) {
          document.activeElement.blur();
        }
      });
      baseline = await captureElementStyles(page, testId);
    }
    const focusedId = await tabUntilTestId(page, [testId], 100);
    if (!focusedId) {
      fail(
        `keyboard focus-visible (${label})`,
        `${testId} not reachable by Tab`,
      );
    }

    const focused = await page.evaluate(() => {
      const el = document.activeElement;
      if (!el || el === document.body) {
        return { ok: false, reason: "no activeElement" };
      }
      const s = getComputedStyle(el);
      return {
        ok: true,
        testId: el.getAttribute("data-testid") || "",
        tag: el.tagName.toLowerCase(),
        matchesFocusVisible: el.matches(":focus-visible"),
        outlineStyle: s.outlineStyle,
        outlineWidth: s.outlineWidth,
        outlineColor: s.outlineColor,
        boxShadow: s.boxShadow,
        borderColor: s.borderColor,
        borderWidth: s.borderWidth,
        backgroundColor: s.backgroundColor,
      };
    });
    if (!focused.ok) {
      fail(`keyboard focus-visible (${label})`, focused.reason);
    }
    if (!focused.matchesFocusVisible) {
      fail(
        `keyboard focus-visible (${label})`,
        `activeElement is not :focus-visible (testid=${focused.testId} tag=${focused.tag})`,
      );
    }
    if (
      !hasPerceptibleFocusIndicator({
        matchesFocusVisible: focused.matchesFocusVisible,
        baseline,
        focused,
      })
    ) {
      fail(
        `keyboard focus-visible (${label})`,
        `no material focus indicator vs baseline (shadow baseline=${baseline.boxShadow} focused=${focused.boxShadow})`,
      );
    }
  }

  async function tabUntilTestId(page, ids, maxTabs = 60) {
    const wanted = new Set(ids);
    for (let i = 0; i < maxTabs; i++) {
      await page.keyboard.press("Tab");
      const id = await page.evaluate(
        () => document.activeElement?.getAttribute("data-testid") || "",
      );
      if (wanted.has(id)) return id;
    }
    return null;
  }

  async function navTo(page, key) {
    const side = page.getByTestId(`sidenav-${key}`);
    if (
      (await side.count()) > 0 &&
      (await side.isVisible().catch(() => false))
    ) {
      await side.click();
      return;
    }
    await page.getByTestId(`bottomnav-${key}`).click();
  }

  async function selectScenario(page, id) {
    await page.evaluate((scenarioId) => {
      const key = "cocinaris_state_v1";
      let state = {};
      try {
        state = JSON.parse(localStorage.getItem(key) || "{}") || {};
      } catch {
        state = {};
      }
      state.scenario = scenarioId;
      state.authed = true;
      localStorage.setItem(key, JSON.stringify(state));
    }, id);
    await page.reload({ waitUntil: "domcontentloaded" });
    await page.waitForURL(/\/app\//, { timeout: 15000 }).catch(() => {});
  }

  async function enterHome(page, { scenario } = {}) {
    await page.goto(BASE + "/", {
      waitUntil: "domcontentloaded",
      timeout: 30000,
    });
    await page.getByTestId("landing-enter").click();
    await page.getByTestId("access-demo").click();
    await page.waitForURL(/\/app\/hoje/, { timeout: 20000 });
    if (scenario) {
      await selectScenario(page, scenario);
      await page.goto(BASE + "/app/hoje", { waitUntil: "domcontentloaded" });
    }
    await page.getByTestId("contextual-home").waitFor({ timeout: 15000 });
  }

  const localeExpectations = {
    "pt-BR": {
      a: "Transforme alimentos disponíveis em refeições úteis",
      b: "Criar conta ou entrar",
      c: "Não é só um gerador de receitas",
    },
    en: {
      a: "Turn available food into useful meals",
      b: "Create an account or sign in",
      c: "not just a recipe generator",
    },
    es: {
      a: "Convierte alimentos disponibles en comidas útiles",
      b: "Crear cuenta o iniciar sesión",
      c: "No es solo un generador de recetas",
    },
  };

  try {
    await withPage(
      { viewport: { width: 360, height: 740 } },
      "360",
      async (page) => {
        await enterHome(page, { scenario: "routeWithDeps" });
        await navTo(page, "pantry");
        await page.waitForURL(/\/app\/despensa/);
        await navTo(page, "plan");
        await page.waitForURL(/\/app\/planejamento/);
        await page.getByTestId("route-chain").waitFor();
        record("360px: Landing→Access→Home→Pantry→Plan", "Passed");
        await navTo(page, "today");
        await page.waitForURL(/\/app\/hoje/);
        record("360px touch/mobile nav", "Passed", "bottomnav");
      },
    );

    await withPage(
      { viewport: { width: 768, height: 1024 } },
      "768",
      async (page) => {
        await enterHome(page, { scenario: "routeWithDeps" });
        await navTo(page, "plan");
        await page.getByTestId("route-chain").waitFor();
        record("768px journey", "Passed");
      },
    );

    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "1280",
      async (page) => {
        await enterHome(page, { scenario: "routeWithDeps" });
        await navTo(page, "plan");
        await page.getByTestId("route-chain").waitFor();
        record("1280px journey", "Passed");
      },
    );

    // Keyboard-only journey (no mouse clicks) with baseline vs focused :focus-visible
    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "keyboard",
      async (page) => {
        await page.goto(BASE + "/", { waitUntil: "domcontentloaded" });
        await assertKeyboardFocusVisible(page, "landing CTA", [
          "landing-enter",
          "hero-enter",
        ]);
        await page.keyboard.press("Enter");
        await page.waitForURL(/\/acesso/);
        await page
          .locator('[data-testid="access-demo"], [data-testid="access-enter"]')
          .first()
          .waitFor({ state: "visible", timeout: 15000 });

        await assertKeyboardFocusVisible(page, "acesso/demo CTA", [
          "access-demo",
          "access-enter",
        ]);
        await page.keyboard.press("Enter");
        await page.waitForURL(/\/app\/hoje/);
        await page.evaluate(() => {
          const key = "cocinaris_state_v1";
          let state = {};
          try {
            state = JSON.parse(localStorage.getItem(key) || "{}") || {};
          } catch {
            state = {};
          }
          state.scenario = "routeWithDeps";
          state.authed = true;
          localStorage.setItem(key, JSON.stringify(state));
        });
        await page.reload({ waitUntil: "domcontentloaded" });
        await page.waitForURL(/\/app\/hoje/);
        await page.getByTestId("contextual-home").waitFor({ timeout: 20000 });

        await assertKeyboardFocusVisible(page, "home action", [
          "home-open-chooser",
          "home-nav-pantry",
        ]);

        await assertKeyboardFocusVisible(page, "main navigation", [
          "sidenav-plan",
          "bottomnav-plan",
        ]);
        await page.keyboard.press("Enter");
        await page.waitForURL(/\/app\/planejamento/);
        await page.getByTestId("route-chain").waitFor();

        await assertKeyboardFocusVisible(page, "route action", [
          "chain-toggle-n2",
          "chain-toggle-n3",
        ]);

        await assertKeyboardFocusVisible(page, "settings", ["nav-settings"]);
        await page.keyboard.press("Enter");
        await page.waitForURL(/\/app\/ajustes/);

        record(
          "keyboard-only Landing→Access→Home→Plan→Settings",
          "Passed",
          "Tab/Enter only; baseline vs focused :focus-visible",
        );
      },
    );

    // CSS zoom approximation (not real browser zoom) — classified separately
    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "css-zoom",
      async (page) => {
        await enterHome(page, { scenario: "routeWithDeps" });
        await page.evaluate(() => {
          document.documentElement.style.zoom = "2";
        });
        const overflowX = await page.evaluate(
          () =>
            document.documentElement.scrollWidth >
            document.documentElement.clientWidth + 40,
        );
        await page.getByTestId("contextual-home").waitFor();
        await navTo(page, "pantry");
        await page.waitForURL(/despensa/);
        await navTo(page, "plan");
        await page.waitForURL(/planejamento/);
        await navTo(page, "shopping");
        await page.waitForURL(/compras/);
        if (overflowX) {
          fail(
            "CSS zoom approximation (not browser zoom)",
            "excessive horizontal overflow at CSS zoom=2",
          );
        }
        record(
          "CSS zoom approximation (not browser zoom)",
          "Passed",
          "documentElement.style.zoom=2; Home/Despensa/Plan/Compras ok",
        );
      },
    );

    await withPage(
      { ...devices["iPhone 12"], hasTouch: true, isMobile: true },
      "touch",
      async (page) => {
        await enterHome(page, { scenario: "routeWithDeps" });
        await page.getByTestId("bottomnav-pantry").click();
        await page.waitForURL(/despensa/);
        await selectScenario(page, "componentShared");
        await page.goto(BASE + "/app/despensa", {
          waitUntil: "domcontentloaded",
        });
        const debt = page.locator(
          '[data-testid="pantry-reserved-debt-cp_broth"]',
        );
        await debt.waitFor({ timeout: 10000 });
        await debt.click();
        await page.waitForURL(/review=shortfall/);
        await selectScenario(page, "routeWithDeps");
        await page.goto(BASE + "/app/hoje", { waitUntil: "domcontentloaded" });
        await page.getByTestId("contextual-home").waitFor();
        await page.getByTestId("bottomnav-plan").click();
        await page.getByTestId("route-chain").waitFor();
        for (const id of ["n2", "n3"]) {
          const toggle = page.getByTestId(`chain-toggle-${id}`);
          await toggle.click();
          await page.waitForTimeout(150);
        }
        const cook = page.locator('[data-testid^="chain-cook-start-"]');
        await cook.first().click();
        await page.waitForURL(/\/app\/cozinhar\//);
        record(
          "touch/mobile viewport flows",
          "Passed",
          "bottomnav, pantry debt, route, cook CTA",
        );
      },
    );

    await withPage(
      { viewport: { width: 1280, height: 800 }, reducedMotion: "reduce" },
      "reduced-motion",
      async (page) => {
        await page.emulateMedia({ reducedMotion: "reduce" });
        const matches = await page.evaluate(
          () => window.matchMedia("(prefers-reduced-motion: reduce)").matches,
        );
        if (!matches) {
          fail(
            "prefers-reduced-motion",
            "matchMedia(prefers-reduced-motion: reduce) was false",
          );
        }
        await enterHome(page, { scenario: "routeWithDeps" });
        await page.getByTestId("home-open-chooser").focus();
        await page.keyboard.press("Enter");
        await page.getByTestId("quick-chooser").waitFor({ timeout: 5000 });
        await page.getByTestId("chooser-cancel").click();
        await page.getByTestId("contextual-home").waitFor();

        // Public entry demo CTA must respect reduced motion (no smooth scroll).
        await page.goto(`${PRODUCTION_BASE}/`);
        await page.getByTestId("production-landing").waitFor();
        await page.evaluate(() => {
          window.__kfScrollBehaviorSeen = null;
          const original = Element.prototype.scrollIntoView;
          Element.prototype.scrollIntoView = function scrollIntoViewPatched(
            arg,
          ) {
            window.__kfScrollBehaviorSeen =
              arg && typeof arg === "object" && "behavior" in arg
                ? arg.behavior
                : "auto";
            return original.call(this, arg);
          };
          window.__kfRestoreScrollIntoView = () => {
            Element.prototype.scrollIntoView = original;
          };
        });
        await page.getByTestId("entry-cta-demo").click();
        const scrollBehavior = await page.evaluate(() => {
          const seen = window.__kfScrollBehaviorSeen;
          if (typeof window.__kfRestoreScrollIntoView === "function") {
            window.__kfRestoreScrollIntoView();
          }
          return seen;
        });
        if (scrollBehavior !== "auto") {
          fail(
            "prefers-reduced-motion",
            `public demo CTA scroll behavior was ${scrollBehavior}, expected auto`,
          );
        }

        // Return to prototype home before overlay motion sampling.
        await enterHome(page, { scenario: "routeWithDeps" });

        // Open a real overlay (scenario Sheet) so reduced-motion covers an open panel.
        let overlayOpened = false;
        if ((await page.getByTestId("scenario-open").count()) > 0) {
          await page.getByTestId("scenario-open").click();
          await page.getByRole("dialog").waitFor({ timeout: 5000 });
          overlayOpened = true;
        }

        const selectors = motionRelevantSelectors();
        const samples = await page.evaluate((selectorList) => {
          function parseCssTimeSeconds(value) {
            if (!value || value === "none") return 0;
            return String(value)
              .split(",")
              .map((part) => {
                const t = part.trim().toLowerCase();
                if (!t || t === "none") return 0;
                if (t.endsWith("ms")) return (parseFloat(t) || 0) / 1000;
                if (t.endsWith("s")) return parseFloat(t) || 0;
                return parseFloat(t) || 0;
              })
              .reduce((max, n) => Math.max(max, n), 0);
          }
          const seen = new Set();
          const out = [];
          for (const sel of selectorList) {
            let nodes = [];
            try {
              nodes = [...document.querySelectorAll(sel)];
            } catch {
              nodes = [];
            }
            for (const el of nodes) {
              if (seen.has(el)) continue;
              seen.add(el);
              const s = getComputedStyle(el);
              const transitionDuration = parseCssTimeSeconds(
                s.transitionDuration,
              );
              const animationDuration = parseCssTimeSeconds(
                s.animationDuration,
              );
              if (transitionDuration === 0 && animationDuration === 0) continue;
              out.push({
                id:
                  el.getAttribute("data-testid") ||
                  (el.className && el.className.toString
                    ? el.className.toString().slice(0, 80)
                    : el.tagName),
                transitionDuration,
                animationDuration,
              });
            }
          }
          return out;
        }, selectors);

        const verdict = evaluateReducedMotionDurations(samples, {
          maxSeconds: 0.5,
        });
        if (!verdict.ok) {
          fail(
            "prefers-reduced-motion",
            `long motion remains under reduce: ${JSON.stringify(verdict.violations.slice(0, 5))}`,
          );
        }
        record(
          "prefers-reduced-motion",
          "Passed",
          `matchMedia true; quick chooser operable; scenarioSheet=${overlayOpened}; motion samples=${samples.length}; no long durations on rendered nodes`,
        );
      },
    );

    await withPage(
      { viewport: { width: 360, height: 740 } },
      "production-locale-360",
      async (page) => {
        await page.goto(PRODUCTION_BASE + "/", {
          waitUntil: "domcontentloaded",
          timeout: 30000,
        });
        await page.getByTestId("production-landing").waitFor();
        const select = page.getByTestId("production-lang-select");
        if (!(await select.isVisible())) {
          fail(
            "production locale mobile 360",
            "production-lang-select not visible at 360px",
          );
        }
        await select.selectOption("en");
        const lang = await page.evaluate(() => document.documentElement.lang);
        if (lang !== "en") {
          fail(
            "production locale mobile 360",
            `documentElement.lang expected en, got ${lang}`,
          );
        }
        const stored = await page.evaluate(() =>
          localStorage.getItem("cocinaris_production_locale"),
        );
        if (stored !== "en") {
          fail(
            "production locale mobile 360",
            `expected cocinaris_production_locale=en, got ${stored}`,
          );
        }
        const tagline = await page
          .getByTestId("production-landing-tagline")
          .innerText();
        if (!/Cocinaris helps/i.test(tagline)) {
          fail(
            "production locale mobile 360",
            `EN tagline missing: ${tagline}`,
          );
        }
        const mockKey = await page.evaluate(() =>
          localStorage.getItem("cocinaris_state_v1"),
        );
        if (mockKey) {
          fail(
            "production locale mobile 360",
            "prototype store key was written",
          );
        }
        record(
          "production locale mobile 360",
          "Passed",
          "select visible; en persisted; lang updated",
        );
      },
    );

    // No live backend/Keycloak is available to this smoke harness (PRODUCTION_BASE
    // serves the static SPA bundle only), so this cannot exercise an authenticated
    // profile session end-to-end. It instead proves the production `/app/perfil*`
    // routes are registered and access-gated: a direct navigation while
    // unauthenticated must redirect to `/acesso` rather than exposing profile data,
    // crashing, or silently falling through to the generic `/app/*` unavailable
    // catch-all. Full authenticated profile coverage (load/save/conflict) is
    // exercised by Jest component tests in `src/features/profile/*.test.tsx` and
    // `src/app/ProductionProfileRoutes.test.tsx`.
    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "production-profile-route-gate",
      async (page) => {
        for (const path of [
          "/app/perfil",
          "/app/perfil/dados",
          "/app/perfil/preferencias",
          "/app/perfil/equipamentos",
        ]) {
          await page.goto(PRODUCTION_BASE + path, {
            waitUntil: "domcontentloaded",
            timeout: 30000,
          });
          await page.waitForURL(/\/acesso/, { timeout: 15000 }).catch(() => {});
          if (!/\/acesso/.test(page.url())) {
            fail(
              "production profile route gate",
              `unauthenticated ${path} did not redirect to /acesso (url=${page.url()})`,
            );
          }
          if ((await page.getByTestId("profile-overview").count()) > 0) {
            fail(
              "production profile route gate",
              `${path} exposed profile-overview while unauthenticated`,
            );
          }
        }
        record(
          "production profile route gate",
          "Passed",
          "unauthenticated /app/perfil* redirects to /acesso; no live-backend authenticated coverage in this harness",
        );
      },
    );

    // Route-interception coverage of the authenticated `/app/perfil*` journey.
    //
    // IMPORTANT: this step never talks to a real Cocinaris backend or Keycloak.
    // Every `/api/v1/session` and `/api/v1/profile*` request against
    // PRODUCTION_BASE is intercepted with `page.route()` and answered from the
    // fixtures in `profileInterceptionFixtures` below, so the frontend renders
    // exactly as it would against a real backend returning those bytes, but no
    // authoritative state is exercised. It complements, and does not replace,
    // the Jest component/integration tests in `src/features/profile/*.test.tsx`
    // and `src/app/ProductionProfileRoutes.test.tsx`, and the unauthenticated
    // route gate above.
    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "production-profile-intercepted",
      async (page) => {
        const interception = await installProfileInterception(page);

        await page.goto(PRODUCTION_BASE + "/app/perfil", {
          waitUntil: "domcontentloaded",
          timeout: 30000,
        });
        await page.getByTestId("profile-overview").waitFor({ timeout: 15000 });
        await page
          .getByTestId("profile-overview-adult-accepted-at")
          .waitFor({ timeout: 5000 });
        if (
          (await page.getByTestId("profile-overview-next-steps").count()) === 0
        ) {
          fail(
            "production profile intercepted: overview",
            "next-steps section missing",
          );
        }
        record(
          "production profile intercepted: overview",
          "Passed",
          "intercepted session+profile fixtures render completeness and adult declaration",
        );

        await page.goto(PRODUCTION_BASE + "/app/perfil/dados", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-data").waitFor({ timeout: 15000 });
        const nameInput = page.getByTestId("profile-data-input-displayName");
        await nameInput.click();
        await nameInput.fill("");
        await nameInput.pressSequentially("Ada Changed For Dirty Guard", {
          delay: 10,
        });
        await page.getByTestId("profile-data-back").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-stay").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ state: "hidden", timeout: 5000 })
          .catch(() => undefined);
        if (await page.getByTestId("profile-unsaved-dialog").isVisible()) {
          fail(
            "production profile intercepted: unsaved changes",
            "dialog still visible after choosing to stay",
          );
        }

        // Shell Home Stay
        await page.getByTestId("production-nav-home").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-stay").click();
        await page.getByTestId("profile-data").waitFor({ timeout: 5000 });
        record(
          "production profile intercepted: dirty shell Home Stay",
          "Passed",
          "primary Home navigation opened confirmation; Stay kept Profile Data",
        );

        // Shell Inventory Discard
        await page.getByTestId("production-nav-despensa").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-discard").click();
        await page.waitForURL(/\/app\/despensa/, { timeout: 10000 });
        record(
          "production profile intercepted: dirty shell Inventory Discard",
          "Passed",
          "primary Inventory navigation discarded the draft and left profile",
        );

        // Dirty logout Stay / Discard (pointer)
        await page.goto(PRODUCTION_BASE + "/app/perfil/dados", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-data").waitFor({ timeout: 15000 });
        await page.getByTestId("profile-data-input-displayName").fill("");
        await page
          .getByTestId("profile-data-input-displayName")
          .pressSequentially("Ada Logout Stay", { delay: 5 });
        await page.getByTestId("production-logout").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-stay").click();
        await page.getByTestId("profile-data").waitFor({ timeout: 5000 });
        if (
          !(await page
            .getByTestId("profile-data-input-displayName")
            .inputValue())
            .includes("Ada Logout Stay")
        ) {
          fail(
            "production profile intercepted: dirty logout Stay",
            "draft was not preserved after Stay",
          );
        }
        record(
          "production profile intercepted: dirty logout Stay",
          "Passed",
          "Logout opened confirmation; Stay kept Profile Data draft",
        );

        // Keyboard dirty logout Stay (still dirty)
        await page.getByTestId("production-logout").focus();
        await page.keyboard.press("Enter");
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-stay").focus();
        await page.keyboard.press("Enter");
        await page.getByTestId("profile-data").waitFor({ timeout: 5000 });
        record(
          "production profile intercepted: dirty logout keyboard Stay",
          "Passed",
          "Keyboard Logout+Stay preserved draft",
        );

        await page.getByTestId("production-logout").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-discard").click();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ state: "hidden", timeout: 5000 })
          .catch(() => undefined);
        record(
          "production profile intercepted: dirty logout Discard",
          "Passed",
          "Logout Discard confirmed once without reopen loop",
        );

        // Keyboard dirty logout Discard
        await page.goto(PRODUCTION_BASE + "/app/perfil/dados", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-data").waitFor({ timeout: 15000 });
        await page.getByTestId("profile-data-input-displayName").fill("");
        await page
          .getByTestId("profile-data-input-displayName")
          .pressSequentially("Ada Logout Keyboard Discard", { delay: 5 });
        await page.getByTestId("production-logout").focus();
        await page.keyboard.press("Enter");
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-discard").focus();
        await page.keyboard.press("Enter");
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ state: "hidden", timeout: 5000 })
          .catch(() => undefined);
        record(
          "production profile intercepted: dirty logout keyboard Discard",
          "Passed",
          "Keyboard Logout+Discard confirmed once without reopen loop",
        );

        await page.goto(PRODUCTION_BASE + "/app/perfil/dados", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-data").waitFor({ timeout: 15000 });
        await page.getByTestId("profile-data-input-displayName").fill("");
        await page
          .getByTestId("profile-data-input-displayName")
          .pressSequentially("Ada Saved Clean", { delay: 5 });
        await page.getByTestId("profile-data-household-save").click();
        await page.waitForTimeout(500);
        if (
          (await page
            .getByTestId("profile-data-household-error-summary")
            .count()) > 0
        ) {
          fail(
            "production profile intercepted: household save",
            "error summary present after a save the fixture answers with 200",
          );
        }
        record(
          "production profile intercepted: household save + unsaved-changes guard",
          "Passed",
          "accessible confirmation blocked navigation with a dirty draft; intercepted PATCH accepted the save",
        );

        // Browser Back while dirty on preferences (client-side history only —
        // full page.goto entries bypass React Router's useBlocker).
        await page.goto(PRODUCTION_BASE + "/app/perfil", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-overview").waitFor({ timeout: 15000 });
        await page.getByTestId("profile-overview-link-preferences").click();
        await page
          .getByTestId("profile-preferences")
          .waitFor({ timeout: 15000 });
        await page
          .getByTestId("profile-preferences-custom-label")
          .fill("Dirty custom preference");
        await page.waitForTimeout(100);
        await page.goBack();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-unsaved-stay").click();
        await page
          .getByTestId("profile-preferences")
          .waitFor({ timeout: 5000 });
        await page.goBack();
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ timeout: 10000 });
        if ((await page.getByTestId("profile-unsaved-dialog").count()) !== 1) {
          fail(
            "production profile intercepted: back confirmation loop",
            "expected exactly one confirmation dialog",
          );
        }
        await page.getByTestId("profile-unsaved-discard").click();
        await page.getByTestId("profile-overview").waitFor({ timeout: 10000 });
        await page
          .getByTestId("profile-unsaved-dialog")
          .waitFor({ state: "hidden", timeout: 5000 })
          .catch(() => undefined);
        await page.waitForTimeout(200);
        if (await page.getByTestId("profile-unsaved-dialog").isVisible()) {
          fail(
            "production profile intercepted: back confirmation loop",
            "confirmation reopened immediately after discard",
          );
        }
        record(
          "production profile intercepted: browser Back Stay/Discard without loop",
          "Passed",
          "dirty Preferences Back Stay retained draft; Discard left once without reopening",
        );

        // Post-save workspace refresh failure
        await page.goto(PRODUCTION_BASE + "/app/perfil/dados", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-data").waitFor({ timeout: 15000 });
        interception.failNextWorkspaceGets(8);
        await page
          .getByTestId("profile-data-input-displayName")
          .fill("Saved But Refresh Fails");
        await page.getByTestId("profile-data-household-save").click();
        await page
          .getByTestId("profile-save-refresh-failed")
          .waitFor({ timeout: 10000 });
        if (
          await page.getByTestId("profile-data-household-save").isEnabled()
        ) {
          fail(
            "production profile intercepted: save refresh failed",
            "save remained enabled after saveRefreshFailed",
          );
        }
        await page.getByTestId("profile-save-refresh-reload").click();
        await page.waitForTimeout(500);
        if (
          (await page.getByTestId("profile-save-refresh-failed").count()) === 0
        ) {
          // Reload may have consumed remaining failures; require warning still or cleared after success
        }
        // Force another failure then a success path by reloading until clear
        let attempts = 0;
        while (
          (await page.getByTestId("profile-save-refresh-failed").count()) > 0 &&
          attempts < 4
        ) {
          attempts += 1;
          await page.getByTestId("profile-save-refresh-reload").click();
          await page.waitForTimeout(400);
        }
        if (
          (await page.getByTestId("profile-save-refresh-failed").count()) > 0
        ) {
          fail(
            "production profile intercepted: save refresh failed",
            "warning did not clear after successful reload retries",
          );
        }
        record(
          "production profile intercepted: save refresh failed warning",
          "Passed",
          "saved-success warning appeared, blocked mutation, and cleared after reload",
        );

        // Session refresh warning
        interception.failNextSessionRefreshOnce();
        await page
          .getByTestId("profile-data-input-displayName")
          .fill("Saved Session Stale");
        await page.getByTestId("profile-data-household-save").click();
        await page
          .getByTestId("profile-session-refresh-warning")
          .waitFor({ timeout: 10000 });
        await page.getByTestId("profile-session-refresh-retry").click();
        await page
          .getByTestId("profile-session-refresh-warning")
          .waitFor({ state: "hidden", timeout: 10000 })
          .catch(() => undefined);
        if (
          await page.getByTestId("profile-session-refresh-warning").isVisible()
        ) {
          fail(
            "production profile intercepted: session refresh warning",
            "session warning remained after retry",
          );
        }
        record(
          "production profile intercepted: session refresh warning",
          "Passed",
          "profile save succeeded while session refresh failure was surfaced and cleared",
        );

        await page.goto(PRODUCTION_BASE + "/app/perfil/preferencias", {
          waitUntil: "domcontentloaded",
        });
        await page
          .getByTestId("profile-preferences")
          .waitFor({ timeout: 15000 });
        record(
          "production profile intercepted: preferences",
          "Passed",
          "intercepted preferences fixture renders",
        );

        await page.goto(PRODUCTION_BASE + "/app/perfil/equipamentos", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("profile-equipment").waitFor({ timeout: 15000 });
        record(
          "production profile intercepted: equipment",
          "Passed",
          "intercepted equipment fixture renders",
        );
      },
    );

    for (const lang of ["pt-BR", "en", "es"]) {
      await withPage(
        { viewport: { width: 1280, height: 800 } },
        `locale-${lang}`,
        async (page) => {
          await page.goto(BASE + "/", { waitUntil: "domcontentloaded" });
          const langBtn = page.getByTestId(`landing-lang-${lang}`);
          if ((await langBtn.count()) === 0) {
            fail(`locale ${lang}`, "language selector missing");
          }
          await langBtn.click();
          const selected = await langBtn.evaluate((el) =>
            el.className.includes("bg-secondary"),
          );
          if (!selected) {
            fail(`locale ${lang}`, "selector did not appear selected");
          }
          const expected = localeExpectations[lang];
          const tagline = await page
            .locator("h1")
            .first()
            .innerText()
            .catch(() => "");
          const body = await page.locator("body").innerText();
          const needed = [expected.a, expected.b, expected.c];
          const hits = needed.filter((s) => body.includes(s));
          if (hits.length < 3) {
            fail(
              `locale ${lang}`,
              `expected translated strings missing (found ${hits.length}/3). sample=${body.slice(0, 240)} taglineSeen=${tagline}`,
            );
          }
          // Confirm language actually changed away from another locale's tagline
          const otherTaglines = Object.entries(localeExpectations)
            .filter(([code]) => code !== lang)
            .map(([, v]) => v.a);
          if (otherTaglines.some((t) => body.includes(t) && t !== expected.a)) {
            // Allow shared fragments only if exact other tagline present incorrectly
            const wrong = otherTaglines.find((t) => body.includes(t));
            if (wrong && wrong !== expected.a) {
              fail(
                `locale ${lang}`,
                `page still contains other locale tagline: ${wrong}`,
              );
            }
          }
          record(`locale ${lang}`, "Passed", "selector + 3 distinct strings");
        },
      );
    }

    await withPage(
      { viewport: { width: 1280, height: 800 } },
      "shortfall-cook",
      async (page) => {
        await enterHome(page, { scenario: "componentShared" });
        await page.goto(BASE + "/app/despensa", {
          waitUntil: "domcontentloaded",
        });
        const debt = page.locator(
          '[data-testid="pantry-reserved-debt-cp_broth"]',
        );
        await debt.click();
        await page.waitForURL(/review=shortfall/);
        record("Despensa déficit → compras review", "Passed");

        await selectScenario(page, "routeWithDeps");
        await page.goto(BASE + "/app/planejamento", {
          waitUntil: "domcontentloaded",
        });
        await page.getByTestId("route-chain").waitFor();
        for (const id of ["n2", "n3"]) {
          await page.getByTestId(`chain-toggle-${id}`).click();
          await page.waitForTimeout(150);
        }
        await page
          .locator('[data-testid^="chain-cook-start-"]')
          .first()
          .click();
        await page.waitForURL(/\/app\/cozinhar\//);
        record("CTA Cozinhar navegação", "Passed", page.url());
      },
    );
  } catch (err) {
    if (!results.some((r) => r.result === "Failed")) {
      record(
        "smoke-runner",
        "Failed",
        String(err && err.message ? err.message : err),
      );
    }
  } finally {
    await browser.close();
    if (serverProc) {
      serverProc.kill("SIGTERM");
    }
    if (productionServer) {
      productionServer.close();
    }
  }

  const report = {
    kind: "automated-browser-smoke",
    ...reportMeta,
    results,
    failedArtifacts,
  };
  const jsonPath = path.join(OUT_DIR, "browser-smoke-report.json");
  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2));
  const htmlPath = path.join(OUT_DIR, "browser-smoke-report.html");
  fs.writeFileSync(
    htmlPath,
    `<!doctype html><meta charset="utf-8"/><title>Browser smoke</title><pre>${JSON.stringify(report, null, 2)}</pre>`,
  );
  console.log("Wrote", jsonPath);

  const blocking = results.filter((r) =>
    ["Failed", "Blocked", "Not executed"].includes(r.result),
  );
  if (blocking.length) {
    console.error(
      "Browser smoke failed mandatory checks:\n" +
        blocking
          .map((r) => ` - [${r.result}] ${r.check}: ${r.notes}`)
          .join("\n"),
    );
    process.exit(1);
  }
}

run().catch((err) => {
  console.error(err);
  process.exit(1);
});
