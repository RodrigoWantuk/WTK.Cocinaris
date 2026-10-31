#!/usr/bin/env node
/**
 * Renders the installable-app icons (PLAN-0059) from `public/icons/icon.svg` with the Chromium that
 * Playwright already installs for the browser checks, so no image library is needed. Run after
 * changing the SVG: `node scripts/generate-app-icons.mjs`. The PNGs are committed because the
 * deploy builds from the repository without running this script.
 *
 * Outputs (in `public/icons/`):
 * - `icon-192.png`, `icon-512.png`: the regular icons ("any").
 * - `maskable-512.png`: full-bleed square (no rounded corners) for launcher masks.
 * - `apple-touch-icon.png` (180 × 180): iOS home screen, which applies its own mask.
 * - `favicon-32.png`: browser tab.
 */
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(path.join(root, "package.json"));
const { chromium } = require("playwright");
const dir = path.join(root, "public", "icons");
const svg = fs.readFileSync(path.join(dir, "icon.svg"), "utf8");
// The maskable icon fills the square; launchers cut their own shape out of it.
const fullBleed = svg.replace('rx="112"', 'rx="0"');

const outputs = [
  ["icon-192.png", 192, svg],
  ["icon-512.png", 512, svg],
  ["maskable-512.png", 512, fullBleed],
  ["apple-touch-icon.png", 180, fullBleed],
  ["favicon-32.png", 32, svg],
];

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
for (const [name, size, source] of outputs) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<html><body style="margin:0;background:transparent">${source.replace("<svg ", `<svg width="${size}" height="${size}" `)}</body></html>`,
  );
  await page.screenshot({
    path: path.join(dir, name),
    omitBackground: true,
    clip: { x: 0, y: 0, width: size, height: size },
  });
  process.stdout.write(`${name} ${size}×${size}\n`);
}
await browser.close();
