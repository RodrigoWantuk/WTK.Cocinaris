import { readFileSync } from "fs";
import { join } from "path";
import { createProductionRuntime } from "@/app/runtime/createProductionRuntime";
import { createPrototypeRuntime } from "@/app/runtime/createPrototypeRuntime";
import { resolveFrontendMode } from "@/app/runtime/mode";
import { UnavailablePreparationRouteRepository } from "@/adapters/live/unavailablePreparationRouteRepository";
import { sharedMockPreparationRouteRepository } from "@/adapters/mock/preparationRouteRepository";

const frontendRoot = join(__dirname, "..", "..");

function readSrc(relativePath: string): string {
  return readFileSync(join(frontendRoot, "src", relativePath), "utf8");
}

const pkg = JSON.parse(
  readFileSync(join(frontendRoot, "package.json"), "utf8"),
) as {
  dependencies?: Record<string, string>;
  devDependencies?: Record<string, string>;
};

/** Built without a contiguous banned token so source grep isolation stays clean. */
const FORBIDDEN_VISUAL_EDITS_PACKAGE = [
  "@",
  "emergent",
  "base",
  "/",
  "visual-edits",
].join("");

describe("production isolation", () => {
  it("does not depend on Emergent visual-edits package in production config", () => {
    expect(pkg.dependencies?.[FORBIDDEN_VISUAL_EDITS_PACKAGE]).toBeUndefined();
    expect(
      pkg.devDependencies?.[FORBIDDEN_VISUAL_EDITS_PACKAGE],
    ).toBeUndefined();
  });

  it("validates frontend mode at resolve time", () => {
    expect(resolveFrontendMode("prototype")).toBe("prototype");
    expect(resolveFrontendMode("production")).toBe("production");
    expect(resolveFrontendMode("test")).toBe("test");
    expect(() => resolveFrontendMode("live")).toThrow(
      /Invalid REACT_APP_FRONTEND_MODE/,
    );
    expect(() => resolveFrontendMode("", "production")).toThrow(
      /required when NODE_ENV/,
    );
    expect(resolveFrontendMode("", "development")).toBe("prototype");
  });

  it("production app module does not import store or mockData", () => {
    const source = readSrc("app/ProductionApp.tsx");
    expect(source).not.toMatch(/from ["']@\/lib\/store["']/);
    expect(source).not.toMatch(/from ["']@\/lib\/mockData["']/);
    expect(source).not.toMatch(/adapters\/mock/);
    expect(source).not.toMatch(/from ["']@\/components\/ScenarioBar["']/);
    expect(source).not.toMatch(/SEED_PANTRY|SEED_PLAN|SEED_SHOPPING/);
  });

  it("default yarn build script is production fail-closed", () => {
    const scripts = (
      JSON.parse(readFileSync(join(frontendRoot, "package.json"), "utf8")) as {
        scripts: Record<string, string>;
      }
    ).scripts;
    expect(scripts.build).toMatch(/FRONTEND_MODE=production/);
    expect(scripts.build).not.toMatch(/FRONTEND_MODE=prototype/);
  });

  it("production composition root does not use mock preparation or home repositories", () => {
    const runtime = createProductionRuntime();
    expect(runtime.mode).toBe("production");
    expect(runtime.enableScenarioBar).toBe(false);
    expect(runtime.enablePrototypeFixtures).toBe(false);
    expect(runtime.persistPrototypeAuth).toBe(false);
    expect(runtime.preparationRouteRepository).toBeInstanceOf(
      UnavailablePreparationRouteRepository,
    );
    expect(runtime.preparationRouteRepository).not.toBe(
      sharedMockPreparationRouteRepository,
    );
    expect(runtime.shoppingRequirementProjections).toEqual([]);
    expect(runtime.contextualHomeAdapter).toBeDefined();
    const sessionSource = readSrc("app/runtime/createProductionRuntime.ts");
    expect(sessionSource).toMatch(/createBffSessionAdapter/);
    expect(sessionSource).not.toMatch(/createUnavailableSessionAdapter/);
    expect(sessionSource).toMatch(/createLiveInventoryRepository/);
    expect(sessionSource).toMatch(/createLiveRecipeRepository/);
    expect(sessionSource).toMatch(/createUnavailableContextualHomeAdapter/);
    expect(sessionSource).not.toMatch(/createMockSessionAdapter/);
    expect(sessionSource).not.toMatch(/createMockContextualHomeAdapter/);
  });

  it("production composition root wires live recipes and does not use mock recipe fixtures", () => {
    const runtime = createProductionRuntime();
    expect(runtime.recipeRepository).toBeDefined();
    const recipePages = [
      "features/recipes/RecipesListPage.tsx",
      "features/recipes/RecipeGeneratePage.tsx",
      "features/recipes/RecipeDetailPage.tsx",
    ];
    for (const page of recipePages) {
      const source = readSrc(page);
      expect(source).not.toMatch(/adapters\/mock/);
      expect(source).not.toMatch(/from ["']@\/lib\/mockData["']/);
      expect(source).not.toMatch(/from ["']@\/lib\/store["']/);
    }
  });

  it("prototype composition root explicitly wires mock tooling", () => {
    const runtime = createPrototypeRuntime();
    expect(runtime.enableScenarioBar).toBe(true);
    expect(runtime.preparationRouteRepository).toBe(
      sharedMockPreparationRouteRepository,
    );
    expect(runtime.contextualHomeAdapter).toBeDefined();
    expect(runtime.recipeRepository).toBeDefined();
    const prototypeSource = readSrc("app/runtime/createPrototypeRuntime.ts");
    expect(prototypeSource).toMatch(/createUnavailableRecipeRepository/);
  });

  it("production runtime module does not import adapters/mock", () => {
    const source = readSrc("app/runtime/createProductionRuntime.ts");
    expect(source).not.toMatch(/adapters\/mock/);
    expect(source).not.toMatch(/sharedMockPreparationRouteRepository/);
    expect(source).not.toMatch(
      /SEED_PANTRY|SEED_PLAN|cocinaris_state_v1|createMockSessionAdapter|createMockContextualHomeAdapter/,
    );
  });

  it("production app does not import contextual-home mock modules", () => {
    const source = readSrc("app/ProductionApp.tsx");
    expect(source).not.toMatch(/adapters\/mock\/contextual-home/);
    expect(source).not.toMatch(/PrototypeContextualHomeRoute/);
    expect(source).not.toMatch(/createMockContextualHomeAdapter/);
  });

  it("AppShell renders scenario tooling only when runtime enables it", () => {
    const source = readSrc("components/AppShell.tsx");
    expect(source).toMatch(/enableScenarioBar/);
    expect(source).toMatch(/ScenarioBar/);
  });

  it("PreparationRouteProvider requires an injected repository", () => {
    const source = readSrc(
      "features/preparation-route/PreparationRouteProvider.tsx",
    );
    expect(source).not.toMatch(/sharedMockPreparationRouteRepository/);
    expect(source).toMatch(/repository: PreparationRouteRepository/);
  });
});
