#!/usr/bin/env node
/**
 * Interface and navigation review capture for the production build (PLAN-0035).
 *
 * Serves `build/` (run `yarn build:production` first), answers `/api/v1/*` with
 * synthetic contract-shaped fixtures, and for every production route and
 * viewport records a full-page screenshot, axe-core violations, page title,
 * console errors, third-party request hosts, and how many times the session
 * endpoint was fetched while navigating through the primary navigation.
 *
 * Synthetic data only; no backend, identity provider, or personal data is
 * involved. Output: `REVIEW_OUT_DIR` (default `../../docs/evidence/plan-0035/<label>`).
 *
 * Usage: node scripts/capture-production-review.js [label]
 */
const { chromium } = require("playwright");
const fs = require("fs");
const http = require("http");
const path = require("path");

const LABEL = process.argv[2] || "review";
const OUT_DIR =
  process.env.REVIEW_OUT_DIR ||
  path.join(__dirname, "..", "..", "..", "docs", "evidence", "plan-0035", LABEL);
const PORT = Number(process.env.REVIEW_PORT || 3093);
const BASE = `http://127.0.0.1:${PORT}`;
const VIEWPORTS = [
  { name: "360", width: 360, height: 780 },
  { name: "768", width: 768, height: 1024 },
  { name: "1280", width: 1280, height: 900 },
];
const LOT_ID = "33333333-3333-3333-3333-333333333331";
const ROUTES = [
  { id: "entry", path: "/", authenticated: false },
  { id: "access", path: "/acesso", authenticated: false },
  { id: "home", path: "/app/hoje" },
  { id: "inventory", path: "/app/despensa" },
  { id: "inventory-new", path: "/app/despensa/novo" },
  { id: "inventory-detail", path: `/app/despensa/${LOT_ID}` },
  { id: "inventory-edit", path: `/app/despensa/${LOT_ID}/editar` },
  { id: "profile", path: "/app/perfil" },
  { id: "profile-data", path: "/app/perfil/dados" },
  { id: "profile-preferences", path: "/app/perfil/preferencias" },
  { id: "profile-equipment", path: "/app/perfil/equipamentos" },
  { id: "unknown-app-route", path: "/app/receitas" },
];

/** Contract-shaped synthetic fixtures (see packages/contracts/openapi/cocinaris-v1.json). */
function fixtures() {
  const field = (value, presence, defaultValue = null) => ({
    value,
    presence,
    defaultValue,
    durability: "durable",
  });
  const userId = "22222222-2222-2222-2222-222222222222";
  const session = {
    userId,
    csrfToken: "csrf-review",
    supportedLocales: ["en", "pt-BR", "es"],
    displayName: "Ada Review",
    language: "pt-BR",
    timeZone: "America/Sao_Paulo",
    measurementSystem: "Metric",
    profileExists: true,
    profilePercentComplete: 60,
    adultDeclarationState: "Declared",
  };
  const now = "2026-10-28T12:00:00Z";
  const lot = (n, name, quantity, storageLocation, extra = {}) => ({
    lotId: `33333333-3333-3333-3333-33333333333${n}`,
    productId: `44444444-4444-4444-4444-44444444444${n}`,
    productName: name,
    quantity,
    storageLocation,
    customLocation: null,
    packageState: "Sealed",
    printedExpirationDate: null,
    notes: null,
    version: `"lot-${n}-v1"`,
    createdAt: now,
    updatedAt: now,
    ...extra,
  });
  const lots = [
    lot(1, "Leite integral", { measuredValue: 700, unit: "Milliliter", availabilityState: null }, "Refrigerator", {
      packageState: "Opened",
      printedExpirationDate: "2026-10-30",
    }),
    lot(2, "Peito de frango", { measuredValue: 1000, unit: "Gram", availabilityState: null }, "Freezer"),
    lot(3, "Ovos", { measuredValue: 6, unit: "Unit", availabilityState: null }, "Refrigerator"),
    lot(4, "Sal", { measuredValue: null, unit: null, availabilityState: "Low" }, "Pantry", {
      packageState: "Unknown",
    }),
  ];
  const profile = {
    ownerUserId: userId,
    displayName: field("Ada Review", "confirmed"),
    household: {
      defaultAdultCount: field(1, "confirmed", 1),
      defaultChildCount: field(0, "default", 0),
      defaultServingCount: field(1, "confirmed", 1),
      language: field("pt-BR", "confirmed", "en"),
      region: field("BR", "confirmed"),
      currency: field("BRL", "confirmed"),
      measurementSystem: field("Metric", "confirmed", "Metric"),
      timeZone: field("America/Sao_Paulo", "confirmed"),
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
    version: "v3",
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: now,
  };
  return {
    session,
    lots,
    profile,
    preferences: { version: "v3", entries: [] },
    equipment: { version: "v3", entries: [] },
    completeness: {
      percentComplete: 60,
      completedSections: 3,
      totalSections: 5,
      sectionCounts: { household: 1, preferences: 0, equipment: 0 },
      adultDeclarationState: "Declared",
      profileExists: true,
    },
  };
}

function startStaticSpa(rootDir, port) {
  const root = path.resolve(rootDir);
  const types = {
    ".html": "text/html; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json",
    ".svg": "image/svg+xml",
    ".woff2": "font/woff2",
    ".woff": "font/woff",
    ".png": "image/png",
    ".ico": "image/x-icon",
  };
  const server = http.createServer((req, res) => {
    const url = new URL(req.url || "/", BASE);
    let filePath = path.join(root, decodeURIComponent(url.pathname));
    if (!filePath.startsWith(root) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
      filePath = path.join(root, "index.html");
    }
    res.writeHead(200, {
      "Content-Type": types[path.extname(filePath).toLowerCase()] || "application/octet-stream",
    });
    res.end(fs.readFileSync(filePath));
  });
  server.listen(port, "127.0.0.1");
  return server;
}

async function installApi(page, fx, authenticated, counters) {
  const json = (route, status, body, headers = {}) =>
    route.fulfill({ status, contentType: "application/json", headers, body: JSON.stringify(body) });
  await page.route("**/api/v1/**", (route) => {
    const url = new URL(route.request().url());
    const p = url.pathname.replace(/^\/api\/v1/, "");
    if (p === "/session") {
      counters.session += 1;
      return authenticated
        ? json(route, 200, fx.session)
        : json(route, 401, { title: "Unauthorized", status: 401, errorCode: "unauthenticated" });
    }
    if (p === "/inventory/lots") return json(route, 200, { items: fx.lots, nextCursor: null });
    const lotMatch = p.match(/^\/inventory\/lots\/([^/]+)(\/history|\/provenance)?$/);
    if (lotMatch) {
      const found = fx.lots.find((l) => l.lotId === lotMatch[1]);
      if (!found) return json(route, 404, { title: "Not found", status: 404, errorCode: "lot_not_found" });
      if (lotMatch[2] === "/history") {
        return json(route, 200, [
          {
            entryId: "55555555-5555-5555-5555-555555555551",
            kind: "Transaction",
            type: "Initial",
            previousQuantity: null,
            resultingQuantity: found.quantity,
            reasonCode: null,
            changedFields: null,
            occurredAt: found.createdAt,
          },
        ]);
      }
      if (lotMatch[2] === "/provenance") {
        return json(route, 200, {
          lotId: found.lotId,
          consumedBy: [],
          consumedByTruncated: false,
          producedBy: [],
          producedByTruncated: false,
        });
      }
      return json(route, 200, found, { etag: found.version });
    }
    if (p === "/profile") return json(route, 200, fx.profile, { etag: '"v3"' });
    if (p === "/profile/preferences") return json(route, 200, fx.preferences, { etag: '"v3"' });
    if (p === "/profile/equipment") return json(route, 200, fx.equipment, { etag: '"v3"' });
    if (p === "/profile/completeness") return json(route, 200, fx.completeness);
    return json(route, 404, { title: "Not found", status: 404, errorCode: "not_found" });
  });
}

async function runAxe(page) {
  await page.addScriptTag({ path: require.resolve("axe-core/axe.min.js") });
  return page.evaluate(async () => {
    // eslint-disable-next-line no-undef
    const result = await axe.run(document, { resultTypes: ["violations"] });
    return result.violations.map((v) => ({
      id: v.id,
      impact: v.impact,
      nodes: v.nodes.length,
      help: v.help,
      targets: v.nodes.slice(0, 3).map((n) => n.target.join(" ")),
    }));
  });
}

async function main() {
  const buildDir = path.join(__dirname, "..", "build");
  if (!fs.existsSync(path.join(buildDir, "index.html"))) {
    throw new Error("Missing build/index.html; run `yarn build:production` first.");
  }
  fs.mkdirSync(OUT_DIR, { recursive: true });
  const server = startStaticSpa(buildDir, PORT);
  const browser = await chromium.launch();
  const fx = fixtures();
  const report = { label: LABEL, generatedAt: new Date().toISOString(), routes: [], navigation: [] };
  try {
    for (const vp of VIEWPORTS) {
      for (const route of ROUTES) {
        const context = await browser.newContext({
          viewport: { width: vp.width, height: vp.height },
          locale: "pt-BR",
          timezoneId: "America/Sao_Paulo",
          reducedMotion: "reduce",
        });
        const page = await context.newPage();
        const counters = { session: 0 };
        const consoleErrors = [];
        const externalHosts = new Set();
        page.on("console", (m) => m.type() === "error" && consoleErrors.push(m.text()));
        page.on("pageerror", (e) => consoleErrors.push(e.message));
        page.on("request", (r) => {
          const host = new URL(r.url()).hostname;
          if (host !== "127.0.0.1") externalHosts.add(host);
        });
        await page.addInitScript(() => window.localStorage.setItem("cocinaris_production_locale", "pt-BR"));
        await installApi(page, fx, route.authenticated !== false, counters);
        await page.goto(`${BASE}${route.path}`, { waitUntil: "networkidle" });
        await page.waitForTimeout(300);
        const file = `${route.id}-${vp.name}.png`;
        await page.screenshot({ path: path.join(OUT_DIR, file), fullPage: true });
        const metrics = await page.evaluate(() => ({
          title: document.title,
          lang: document.documentElement.lang,
          finalPath: window.location.pathname,
          h1: Array.from(document.querySelectorAll("h1")).map((h) => h.textContent.trim()),
          horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
          // Innermost elements that extend past the viewport, to locate overflow causes.
          overflowCulprits: Array.from(document.querySelectorAll("body *"))
            .filter((el) => el.getBoundingClientRect().right > window.innerWidth + 1)
            .filter((el) => !Array.from(el.children).some((c) => c.getBoundingClientRect().right > window.innerWidth + 1))
            .slice(0, 5)
            .map((el) => {
              const testId = el.getAttribute("data-testid");
              const text = (el.textContent || "").trim().slice(0, 40);
              return `${el.tagName.toLowerCase()}${testId ? `[${testId}]` : ""} "${text}"`;
            }),
          hasMainLandmark: Boolean(document.querySelector("main")),
          hasSkipLink: Boolean(document.querySelector('a[href="#main-content"]')),
        }));
        const axeViolations = vp.name === "1280" ? await runAxe(page) : undefined;
        report.routes.push({
          route: route.id,
          path: route.path,
          viewport: vp.name,
          screenshot: file,
          ...metrics,
          sessionFetches: counters.session,
          consoleErrors,
          externalHosts: [...externalHosts],
          axeViolations,
        });
        await context.close();
      }
    }

    // Navigation churn: click through the primary navigation and count session fetches.
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, locale: "pt-BR" });
    const page = await context.newPage();
    const counters = { session: 0 };
    await page.addInitScript(() => window.localStorage.setItem("cocinaris_production_locale", "pt-BR"));
    await installApi(page, fx, true, counters);
    await page.goto(`${BASE}/app/hoje`, { waitUntil: "networkidle" });
    for (const testId of ["production-nav-despensa", "production-nav-perfil", "production-nav-home", "production-nav-despensa"]) {
      const before = counters.session;
      await page.getByTestId(testId).click();
      await page.waitForLoadState("networkidle");
      report.navigation.push({ clicked: testId, path: new URL(page.url()).pathname, sessionFetches: counters.session - before });
    }
    await context.close();
  } finally {
    await browser.close();
    server.close();
  }
  fs.writeFileSync(path.join(OUT_DIR, "report.json"), `${JSON.stringify(report, null, 2)}\n`);
  console.log(`Wrote ${report.routes.length} captures to ${OUT_DIR}`);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
