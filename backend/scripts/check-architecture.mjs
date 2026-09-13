// Architecture check for the backend project-reference graph
// (high-level-architecture.md §2, ADR-0006/0007/0009,
// docs/conventions/backend.md):
//   - a project may only reference the projects allowed for its layer
//     (see scripts/lib/module-rules.mjs for the full allow-list and the
//     documented exceptions it preserves — e.g. Infrastructure implementing
//     a port declared in another module's Application, ADR-0009);
//   - Domain must not reference any NuGet package; Application must not
//     reference persistence/web-hosting packages;
//   - the project-reference graph must stay free of cycles.
//
// Scope: backend/src only. backend/tests/** is excluded on purpose — test
// projects (especially IntegrationTests) legitimately reference many
// modules/Host to exercise real cross-module behavior; the module-boundary
// rule is a production-code concern (mirrors the frontend checker, which
// likewise excludes *.spec.ts via tsconfig.app.json's `exclude`).
//
// This reads the .csproj files' <ProjectReference>/<PackageReference>
// elements directly with a small tolerant regex rather than a full XML
// parser — .csproj is simple, tool-generated XML with one relevant
// attribute per element, and adding an XML-parser dependency to a backend
// project for a CI script is not warranted. `dotnet build` already
// validates that the XML is well-formed; this script only inspects
// declared references, so a malformed .csproj would fail the build long
// before this check runs.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { classifyProject, checkProjectEdge, checkPackageReference, findCycles, projectLabel } from './lib/module-rules.mjs';

const backendRoot = resolve(import.meta.dirname, '..');
const srcRoot = join(backendRoot, 'src');

function listCsprojFiles(dir) {
  const entries = readdirSync(dir, { withFileTypes: true });
  return entries.flatMap((entry) => {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) return listCsprojFiles(full);
    return entry.name.endsWith('.csproj') ? [full] : [];
  });
}

function toSrcRelative(absPath) {
  return relative(srcRoot, absPath).split('\\').join('/');
}

const projectReferencePattern = /<ProjectReference\s+Include="([^"]+)"/g;
const packageReferencePattern = /<PackageReference\s+Include="([^"]+)"/g;

const csprojFiles = listCsprojFiles(srcRoot);
const projectByRelDir = new Map(); // "Modules/Wallets/Domain" -> absolute .csproj path

for (const file of csprojFiles) {
  const relDir = toSrcRelative(dirname(file));
  projectByRelDir.set(relDir, file);
}

const edges = []; // { from: relDir, to: relDir }
const unresolvedReferences = [];
const violations = [];

for (const file of csprojFiles) {
  const relDir = toSrcRelative(dirname(file));
  const sourceInfo = classifyProject(relDir);
  if (!sourceInfo) {
    unresolvedReferences.push(`${relDir}: not classifiable under the ADR-0012 §2 module template (unexpected path shape)`);
    continue;
  }

  const text = readFileSync(file, 'utf8');

  for (const match of text.matchAll(projectReferencePattern)) {
    const includePath = match[1].split('\\').join('/');
    const absTarget = resolve(dirname(file), includePath);
    const relTargetDir = toSrcRelative(dirname(absTarget));
    const targetInfo = classifyProject(relTargetDir);
    if (!targetInfo) {
      unresolvedReferences.push(`${relDir}: ProjectReference "${match[1]}" resolves outside the tracked module template (${relTargetDir})`);
      continue;
    }
    edges.push({ from: relDir, to: relTargetDir });
    const reason = checkProjectEdge(sourceInfo, targetInfo);
    if (reason) violations.push(`${projectLabel(sourceInfo)} -> ${projectLabel(targetInfo)}\n    ${reason}`);
  }

  for (const match of text.matchAll(packageReferencePattern)) {
    const packageId = match[1];
    const reason = checkPackageReference(sourceInfo, packageId);
    if (reason) violations.push(reason);
  }
}

const cycles = findCycles(edges);
const seenCycleKeys = new Set();
const distinctCycles = cycles.filter((cycle) => {
  const key = [...new Set(cycle)].sort().join('|');
  if (seenCycleKeys.has(key)) return false;
  seenCycleKeys.add(key);
  return true;
});

let hasErrors = false;

if (unresolvedReferences.length > 0) {
  hasErrors = true;
  console.error('Unresolvable or unclassifiable references (likely a typo or a project outside the module template):\n');
  for (const line of unresolvedReferences) console.error(`  - ${line}`);
  console.error('');
}

if (violations.length > 0) {
  hasErrors = true;
  console.error('Backend architecture violations:\n');
  for (const v of violations) console.error(`  - ${v}`);
  console.error(`\n${violations.length} violation(s).\n`);
}

if (distinctCycles.length > 0) {
  hasErrors = true;
  console.error('Project-reference cycles:\n');
  for (const cycle of distinctCycles) {
    console.error(`  - ${cycle.map((relDir) => projectLabel(classifyProject(relDir))).join(' -> ')}`);
  }
  console.error(`\n${distinctCycles.length} cycle(s) found. The project-reference graph must stay acyclic.\n`);
}

if (hasErrors) {
  process.exit(1);
}

console.log(`OK: ${csprojFiles.length} projects, ${edges.length} ProjectReference edges checked — no architecture violations, no cycles.`);
