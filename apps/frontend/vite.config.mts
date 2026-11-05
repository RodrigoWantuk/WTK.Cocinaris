/// <reference types="vitest/config" />
import path from "node:path";
import react from "@vitejs/plugin-react";
import { defineConfig, type Plugin, type PluginOption } from "vite";
import { VitePWA } from "vite-plugin-pwa";

/**
 * Frontend toolchain (ADR-0008, PLAN-0036). Replaces CRA/CRACO while keeping the
 * external contracts of `apps/frontend`: `REACT_APP_FRONTEND_MODE` selects
 * prototype/production/test, builds fail without an explicit mode, prototype-only
 * modules are swapped out of production bundles, and output keeps the
 * `build/static/{js,css,media}` layout the bundle inspection and deploy script read.
 */

const FRONTEND_MODES = ["prototype", "production", "test"] as const;
type FrontendMode = (typeof FRONTEND_MODES)[number];

function resolveFrontendMode(command: "build" | "serve"): FrontendMode {
  const raw = (process.env.REACT_APP_FRONTEND_MODE ?? "").trim().toLowerCase();
  if (!raw) {
    if (command === "build" && !process.env.VITEST) {
      throw new Error(
        "REACT_APP_FRONTEND_MODE is required for builds. Use yarn build / yarn build:production (production) or yarn build:prototype.",
      );
    }
    return process.env.VITEST ? "test" : "prototype";
  }
  if (!(FRONTEND_MODES as readonly string[]).includes(raw)) {
    throw new Error(
      `Invalid REACT_APP_FRONTEND_MODE="${process.env.REACT_APP_FRONTEND_MODE}". Expected prototype|production|test.`,
    );
  }
  return raw as FrontendMode;
}

const src = (relative: string) =>
  path.resolve(import.meta.dirname, "src", relative);

/**
 * Production builds must not contain mock stores, fixtures, or scenario tooling.
 * Each prototype-only module resolves to its production stub, whatever import
 * specifier (alias or relative) reached it.
 */
function productionIsolation(): Plugin {
  const replacements: Array<[RegExp, string]> = [
    [
      /[\\/]src[\\/]components[\\/]ScenarioBar(\.tsx)?$/,
      src("components/runtime/ScenarioBar.production.tsx"),
    ],
    [
      /[\\/]src[\\/]app[\\/]runtime[\\/]createPrototypeRuntime(\.ts)?$/,
      src("app/runtime/createPrototypeRuntime.production.ts"),
    ],
    [
      /[\\/]src[\\/]app[\\/]PrototypeApp(\.tsx)?$/,
      src("app/PrototypeApp.production.ts"),
    ],
  ];
  return {
    name: "cocinaris-production-isolation",
    enforce: "pre",
    async resolveId(source, importer, options) {
      const resolved = await this.resolve(source, importer, {
        ...options,
        skipSelf: true,
      });
      if (!resolved) return null;
      for (const [pattern, replacement] of replacements) {
        if (pattern.test(resolved.id)) return replacement;
      }
      return null;
    },
  };
}

/**
 * Service worker of the production build (ADR-0012). It precaches built assets and nothing else:
 * with no runtime caching route, API responses never reach it, so it cannot hold personal data.
 * New workers activate at once but the page is not reloaded, which could discard a form.
 */
function offlineShell(): PluginOption {
  return VitePWA({
    registerType: "autoUpdate",
    injectRegister: "script",
    filename: "sw.js",
    // public/manifest.webmanifest is the manifest; the plugin must not generate another one.
    manifest: false,
    workbox: {
      globPatterns: ["**/*.{js,css,html,woff2,png,svg,webmanifest}"],
      // The application is one bundle; if it were skipped, the offline shell would silently break.
      maximumFileSizeToCacheInBytes: 8 * 1024 * 1024,
      navigateFallback: "/index.html",
      navigateFallbackDenylist: [
        /^\/api\//,
        /^\/signin-oidc/,
        /^\/signout-callback-oidc/,
        /^\/health\//,
      ],
      cleanupOutdatedCaches: true,
      // Set here because the plugin's `autoUpdate` alone left a new worker waiting, so a deploy
      // never reached a person whose open tab kept the old worker alive (found by the G3 check).
      skipWaiting: true,
      clientsClaim: true,
      runtimeCaching: [],
      // Plan notifications (ADR-0013): generateSW stays, and this adds only the push handlers.
      importScripts: ["push-sw.js"],
    },
  });
}

export default defineConfig(({ command }) => {
  const frontendMode = resolveFrontendMode(command);
  return {
    plugins: [
      react(),
      ...(frontendMode === "production" ? [productionIsolation()] : []),
      ...(frontendMode === "production" && command === "build"
        ? [offlineShell()]
        : []),
    ],
    resolve: {
      alias: [
        {
          find: /^@cocinaris\/api-client$/,
          replacement: src("generated/api-client/index.ts"),
        },
        {
          find: /^@cocinaris\/api-client\/(.*)$/,
          replacement: `${src("generated/api-client")}/$1`,
        },
        { find: /^@\//, replacement: `${src("")}/` },
      ],
    },
    // Application code keeps reading `process.env.*` (see src/app/runtime/mode.ts);
    // these are replaced at build time exactly as CRA did.
    define: {
      "process.env.REACT_APP_FRONTEND_MODE": JSON.stringify(frontendMode),
      "process.env.NODE_ENV": JSON.stringify(
        command === "build"
          ? "production"
          : frontendMode === "test"
            ? "test"
            : "development",
      ),
    },
    server: { host: "127.0.0.1", port: 3000, strictPort: true },
    preview: { host: "127.0.0.1", port: 3001, strictPort: true },
    build: {
      outDir: "build",
      emptyOutDir: true,
      sourcemap: false,
      rolldownOptions: {
        output: {
          entryFileNames: "static/js/[name].[hash].js",
          chunkFileNames: "static/js/[name].[hash].js",
          assetFileNames: (asset) =>
            asset.names.some((name) => name.endsWith(".css"))
              ? "static/css/[name].[hash][extname]"
              : "static/media/[name].[hash][extname]",
        },
      },
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./src/setupTests.ts"],
      include: ["src/**/*.test.{ts,tsx}"],
      // CRA's Jest preset ran with resetMocks: true; keep the same isolation.
      mockReset: true,
      css: false,
    },
  };
});
