#!/usr/bin/env node
/**
 * Fails when committed OpenAPI-generated TypeScript differs from a fresh generation,
 * or when the frontend CRA mirror drifts from packages/api-client/src.
 */
import { spawnSync } from "node:child_process";
import {
  mkdtempSync,
  readFileSync,
  rmSync,
  readdirSync,
  statSync,
} from "node:fs";
import { join, dirname, relative } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(__dirname, "../..");
const packageDir = join(repoRoot, "packages/api-client");
const committed = join(packageDir, "src/generated/schema.ts");
const openapi = join(repoRoot, "packages/contracts/openapi/cocinaris-v1.json");
const packageSrc = join(packageDir, "src");
const frontendMirror = join(
  repoRoot,
  "apps/frontend/src/generated/api-client",
);

function walkFiles(dir, base = dir, acc = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      walkFiles(full, base, acc);
    } else if (entry !== "README.md") {
      acc.push(relative(base, full));
    }
  }
  return acc;
}

const tempDir = mkdtempSync(join(tmpdir(), "kf-api-client-"));
const tempOut = join(tempDir, "schema.ts");

try {
  const result = spawnSync(
    "yarn",
    ["--cwd", packageDir, "openapi-typescript", openapi, "-o", tempOut],
    { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] },
  );
  if (result.status !== 0) {
    console.error(result.stdout);
    console.error(result.stderr);
    process.exit(result.status ?? 1);
  }

  const expected = readFileSync(tempOut, "utf8");
  const actual = readFileSync(committed, "utf8");
  if (expected !== actual) {
    console.error(
      "Generated API client drifted from packages/contracts/openapi/cocinaris-v1.json.\n" +
        "Run: cd packages/api-client && yarn generate",
    );
    process.exit(1);
  }

  const packageFiles = walkFiles(packageSrc);
  const mirrorFiles = walkFiles(frontendMirror);
  const packageSet = new Set(packageFiles);
  const mirrorSet = new Set(mirrorFiles);
  for (const file of packageFiles) {
    if (!mirrorSet.has(file)) {
      console.error(`Frontend API client mirror missing: ${file}`);
      process.exit(1);
    }
    const left = readFileSync(join(packageSrc, file), "utf8");
    const right = readFileSync(join(frontendMirror, file), "utf8");
    if (left !== right) {
      console.error(`Frontend API client mirror drifted: ${file}`);
      console.error("Run: cd packages/api-client && yarn generate");
      process.exit(1);
    }
  }
  for (const file of mirrorFiles) {
    if (!packageSet.has(file)) {
      console.error(`Frontend API client mirror has unexpected file: ${file}`);
      process.exit(1);
    }
  }

  console.log("API client drift check passed (package + frontend mirror).");
} finally {
  rmSync(tempDir, { recursive: true, force: true });
}
