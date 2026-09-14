// Architecture check for the frontend dependency graph (ADR-0012 §3,
// docs/conventions/frontend.md):
//   - a feature must not import another feature directly;
//   - core/shared/data-access must not depend on any feature;
//   - the graph must stay free of import cycles.
//
// Uses the TypeScript compiler API (ts.resolveModuleName) to resolve every
// import the same way tsc itself would — including the `@shared/*` path
// alias from tsconfig.json, `export ... from '...'` re-exports, and dynamic
// `import('...')` calls with a string-literal argument (e.g. the lazy routes
// in app.routes.ts). A specifier that isn't a string literal (a computed
// dynamic import path) cannot be resolved statically and is skipped — this
// is a known limitation of any static analysis, not particular to this tool.
//
// Rule logic lives in lib/dependency-rules.mjs (unit-tested there with
// synthetic allowed/forbidden examples); this file only does the TS-specific
// work of building the real edge list from the actual source tree.
import ts from 'typescript';
import { readFileSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';
import { classifyZone, checkEdge, findCycles, zoneLabel } from './lib/dependency-rules.mjs';

const frontendRoot = resolve(import.meta.dirname, '..');
const appRoot = resolve(frontendRoot, 'src', 'app');
const tsconfigPath = resolve(frontendRoot, 'tsconfig.app.json');

const configFile = ts.readConfigFile(tsconfigPath, ts.sys.readFile);
if (configFile.error) {
  console.error(ts.formatDiagnostic(configFile.error, { getCanonicalFileName: (f) => f, getCurrentDirectory: () => frontendRoot, getNewLine: () => '\n' }));
  process.exit(1);
}
const parsedConfig = ts.parseJsonConfigFileContent(configFile.config, ts.sys, frontendRoot);
const compilerOptions = parsedConfig.options;

/** Every import/export/dynamic-import specifier found in one file, with its own file path attached. */
function collectSpecifiers(sourceFile) {
  const specifiers = [];
  const visit = (node) => {
    if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteral(node.moduleSpecifier)) {
      specifiers.push(node.moduleSpecifier.text);
    } else if (ts.isCallExpression(node) && node.expression.kind === ts.SyntaxKind.ImportKeyword) {
      const [arg] = node.arguments;
      if (arg && ts.isStringLiteral(arg)) {
        specifiers.push(arg.text);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);
  return specifiers;
}

function toAppRelative(absPath) {
  const rel = relative(appRoot, absPath).split('\\').join('/');
  return rel.startsWith('..') ? null : rel;
}

const edges = []; // { from: appRelativePath, to: appRelativePath }
const unresolved = []; // specifiers that look internal but didn't resolve (likely a real bug elsewhere)

for (const fileName of parsedConfig.fileNames) {
  const absFile = resolve(fileName);
  const relFile = toAppRelative(absFile);
  if (relFile === null) continue; // outside src/app (shouldn't happen given tsconfig's include)

  const text = readFileSync(absFile, 'utf8');
  const sourceFile = ts.createSourceFile(absFile, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);

  for (const specifier of collectSpecifiers(sourceFile)) {
    const looksInternal = specifier.startsWith('.') || compilerOptions.paths && Object.keys(compilerOptions.paths).some((p) => specifier.startsWith(p.replace(/\*$/, '')));
    const resolution = ts.resolveModuleName(specifier, absFile, compilerOptions, ts.sys);
    const resolvedPath = resolution.resolvedModule?.resolvedFileName;
    if (!resolvedPath) {
      if (looksInternal) unresolved.push(`${relFile}: could not resolve "${specifier}"`);
      continue; // external package (e.g. "@angular/core") — not part of this app's graph
    }
    const relTarget = toAppRelative(resolve(resolvedPath));
    if (relTarget === null) continue; // resolves outside src/app (external package under node_modules, or a .d.ts lib)
    if (relTarget === relFile) continue; // no self-edges from e.g. a barrel re-exporting its own directory
    edges.push({ from: relFile, to: relTarget });
  }
}

const violations = [];
for (const { from, to } of edges) {
  const sourceZone = classifyZone(from);
  const targetZone = classifyZone(to);
  const reason = checkEdge(sourceZone, targetZone);
  if (reason) {
    violations.push(`${from} -> ${to}\n    ${reason}`);
  }
}

const cycles = findCycles(edges);
// findCycles may report the same cycle more than once (once per unvisited entry
// point into it); de-duplicate by its sorted node set for reporting.
const seenCycleKeys = new Set();
const distinctCycles = cycles.filter((cycle) => {
  const key = [...new Set(cycle)].sort().join('|');
  if (seenCycleKeys.has(key)) return false;
  seenCycleKeys.add(key);
  return true;
});

let hasErrors = false;

if (unresolved.length > 0) {
  hasErrors = true;
  console.error('Could not resolve the following relative/aliased imports (likely a typo or a moved file):\n');
  for (const line of unresolved) console.error(`  - ${line}`);
  console.error('');
}

if (violations.length > 0) {
  hasErrors = true;
  console.error('Feature boundary violations (ADR-0012 §3):\n');
  for (const v of violations) console.error(`  - ${v}`);
  console.error(`\n${violations.length} violation(s). Use data-access/<domain>, core, or shared instead.\n`);
}

if (distinctCycles.length > 0) {
  hasErrors = true;
  console.error('Import cycles:\n');
  for (const cycle of distinctCycles) {
    console.error(`  - ${cycle.join(' -> ')}`);
  }
  console.error(`\n${distinctCycles.length} cycle(s) found. The dependency graph must stay acyclic (ADR-0012 §3).\n`);
}

if (hasErrors) {
  process.exit(1);
}

console.log(`OK: ${edges.length} internal import edges checked across ${parsedConfig.fileNames.length} files — no feature-boundary violations, no cycles.`);
