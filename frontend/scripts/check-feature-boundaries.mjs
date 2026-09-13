// Fails if a file under src/app/features/<domain> imports from a different
// features/<other-domain> directly. Cross-domain data must flow through
// data-access/<domain>, core, or shared (ADR-0012 §3).
import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";

const appRoot = resolve(import.meta.dirname, "..", "src", "app");
const featuresRoot = join(appRoot, "features");
const importPattern = /from\s+["']([^"']+)["']/g;

function listTsFiles(dir) {
  const entries = readdirSync(dir, { withFileTypes: true });
  return entries.flatMap((entry) => {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) return listTsFiles(full);
    return entry.name.endsWith(".ts") ? [full] : [];
  });
}

function featureOf(absolutePath) {
  const rel = relative(featuresRoot, absolutePath);
  if (rel.startsWith("..")) return null;
  return rel.split(/[/\\]/)[0];
}

const violations = [];

for (const file of listTsFiles(featuresRoot)) {
  const ownFeature = featureOf(file);
  const source = readFileSync(file, "utf8");
  for (const match of source.matchAll(importPattern)) {
    const specifier = match[1];
    if (!specifier.startsWith(".")) continue; // package/alias import, not a cross-feature path
    const target = resolve(dirname(file), specifier);
    const targetFeature = featureOf(target);
    if (targetFeature && targetFeature !== ownFeature) {
      violations.push(
        `${relative(appRoot, file)}: imports "${specifier}" from feature "${targetFeature}" (own feature: "${ownFeature}")`
      );
    }
  }
}

if (violations.length > 0) {
  console.error("Feature boundary violations (a feature must not import another feature directly):\n");
  for (const v of violations) console.error(`  - ${v}`);
  console.error(`\n${violations.length} violation(s). Use data-access/<domain>, core, or shared instead.`);
  process.exit(1);
}

console.log("OK: no cross-feature imports found.");
