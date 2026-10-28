#!/usr/bin/env node
/**
 * Guard: the entry page and global styles must not load code, styles, or fonts from another origin.
 *
 * The SPA shell wraps every authenticated route, so a third-party script there (analytics, session
 * recording, tag managers) can read pantry, profile, and restriction data. Font and style CDNs
 * disclose every visitor's address to a third party. PLAN-0032 removed such snippets inherited from
 * the Emergent import; this guard keeps them from coming back.
 *
 * Checks the Vite entry `index.html`, `build/index.html` when present, and every CSS file under `src/`.
 */
const fs = require("fs");
const path = require("path");

const root = path.join(__dirname, "..");
const failures = [];

// Any src/href pointing at an absolute or protocol-relative URL, plus inline loaders by name.
const htmlExternal = /<(script|link|iframe|img)\b[^>]*\b(src|href)\s*=\s*["'](https?:)?\/\//gi;
const htmlInlineLoaders = /posthog|googletagmanager|gtag\(|google-analytics|hotjar|clarity\.ms|segment\.com/gi;
const cssExternal = /@import\s+(url\()?\s*["']?(https?:)?\/\//gi;

function checkHtml(file) {
  if (!fs.existsSync(file)) return;
  const html = fs.readFileSync(file, "utf8");
  for (const match of html.matchAll(htmlExternal)) {
    failures.push(`${path.relative(root, file)}: external <${match[1]}> reference`);
  }
  for (const match of html.matchAll(htmlInlineLoaders)) {
    failures.push(`${path.relative(root, file)}: third-party loader "${match[0]}"`);
  }
}

function walkCss(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walkCss(full);
    else if (entry.name.endsWith(".css")) {
      const css = fs.readFileSync(full, "utf8");
      if (cssExternal.test(css)) failures.push(`${path.relative(root, full)}: external @import`);
      cssExternal.lastIndex = 0;
    }
  }
}

checkHtml(path.join(root, "index.html"));
checkHtml(path.join(root, "build", "index.html"));
walkCss(path.join(root, "src"));

if (failures.length > 0) {
  console.error("Third-party HTML/CSS guard failed:");
  for (const failure of failures) console.error(` - ${failure}`);
  process.exit(1);
}
console.log("Third-party HTML/CSS guard passed.");
