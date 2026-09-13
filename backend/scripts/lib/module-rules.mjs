// Pure, framework-agnostic rules for the backend project-reference graph
// (high-level-architecture.md §2 "правило границы модуля"; ADR-0006 — one
// bounded context = one module; ADR-0007/ADR-0009 — the documented
// exceptions that let one module's layer reference another's). No filesystem
// or .csproj parsing here, so the rules are unit-testable with synthetic
// examples (see module-rules.test.mjs).
//
// Per-layer allow-list (a project may reference a target project iff the
// target's area appears in the allow-list for the project's own layer):
//   Domain         -> shared-kernel only. Not even its own module's other
//                      layers ("Domain не зависит ни от чего").
//   Application    -> own module's Domain, shared-kernel, ANY module's
//                      Application (ADR-0007: explicit narrow contracts,
//                      e.g. Operations.Application -> Wallets.Application).
//   Infrastructure -> own module's Application, own module's Domain,
//                      shared-kernel, building-blocks-infrastructure,
//                      ANY module's Domain (domain-event handlers) and ANY
//                      module's Application (ADR-0009: reverse ports, e.g.
//                      Wallets.Infrastructure -> ReferenceData.Application).
//   Api            -> own module's Application, shared-kernel. Not even its
//                      own module's Infrastructure ("Api зависит от
//                      Application, не от Infrastructure напрямую").
// building-blocks-infrastructure -> shared-kernel only (a cross-cutting leaf,
//   must not depend on any specific module).
// shared-kernel -> nothing.
// host -> anything (composition root, exempt from the module-boundary rule).

/**
 * Classifies a project path relative to `backend/src` (forward slashes) using
 * the ADR-0012 §2 module template. Returns null for anything outside the
 * tracked areas (there should be none under backend/src).
 */
export function classifyProject(relPath) {
  const segments = relPath.split('/');
  if (segments[0] === 'BuildingBlocks' && segments[1] === 'SharedKernel') return { area: 'shared-kernel' };
  if (segments[0] === 'BuildingBlocks' && segments[1] === 'Infrastructure') return { area: 'building-blocks-infrastructure' };
  if (segments[0] === 'Host') return { area: 'host' };
  if (segments[0] === 'Modules' && segments[1] && segments[2]) {
    const layer = segments[2];
    if (!['Domain', 'Application', 'Infrastructure', 'Api'].includes(layer)) return null;
    return { area: 'module', module: segments[1], layer };
  }
  return null;
}

export function projectLabel(info) {
  if (info.area === 'module') return `${info.module}.${info.layer}`;
  return info.area;
}

/**
 * Evaluates one ProjectReference edge (source references target) against the
 * per-layer allow-list above. Returns a violation message, or null if the
 * edge is allowed.
 */
export function checkProjectEdge(source, target) {
  const targetIsOwnModule = source.area === 'module' && target.area === 'module' && target.module === source.module;

  switch (source.area) {
    case 'host':
      return null; // composition root — exempt from the module-boundary rule
    case 'shared-kernel':
      return `${projectLabel(source)} (SharedKernel) must not reference anything — it is the innermost layer`;
    case 'building-blocks-infrastructure':
      if (target.area === 'shared-kernel') return null;
      return `${projectLabel(source)} must not depend on ${projectLabel(target)} — cross-cutting building blocks must not depend on a specific module`;
    case 'module':
      break;
    default:
      return `unclassified source project (${JSON.stringify(source)})`;
  }

  switch (source.layer) {
    case 'Domain':
      if (target.area === 'shared-kernel') return null;
      return `${projectLabel(source)} (Domain) must not reference ${projectLabel(target)} — Domain depends on nothing but SharedKernel`;
    case 'Application':
      if (target.area === 'shared-kernel') return null;
      if (targetIsOwnModule && target.layer === 'Domain') return null;
      if (target.area === 'module' && target.layer === 'Application') return null; // ADR-0007: explicit cross-module contract
      return `${projectLabel(source)} must not reference ${projectLabel(target)} — Application may only depend on its own Domain and on other modules' Application (ADR-0007)`;
    case 'Infrastructure':
      if (target.area === 'shared-kernel' || target.area === 'building-blocks-infrastructure') return null;
      if (targetIsOwnModule && (target.layer === 'Application' || target.layer === 'Domain')) return null;
      if (target.area === 'module' && (target.layer === 'Domain' || target.layer === 'Application')) return null; // ADR-0009: domain-event handlers / reverse ports
      return `${projectLabel(source)} must not reference ${projectLabel(target)} — Infrastructure may depend on Domain/Application of any module, but not another module's Infrastructure or Api`;
    case 'Api':
      if (target.area === 'shared-kernel') return null;
      if (targetIsOwnModule && target.layer === 'Application') return null;
      return `${projectLabel(source)} must not reference ${projectLabel(target)} — Api may only depend on its own Application (not its own Infrastructure, not other modules)`;
    default:
      return `unclassified layer on ${projectLabel(source)}`;
  }
}

/** Package families that are Infrastructure/Api concerns (persistence, web
 * hosting, HTTP clients) and must not leak into Domain or Application. */
const INFRASTRUCTURE_PACKAGE_PATTERNS = [
  /^Microsoft\.EntityFrameworkCore/i,
  /^Npgsql/i,
  /^EFCore\.NamingConventions$/i,
  /^Microsoft\.AspNetCore\./i,
  /^Swashbuckle\.AspNetCore$/i,
  /^System\.IdentityModel\.Tokens\.Jwt$/i,
  /^Microsoft\.Extensions\.Http/i,
];

/**
 * Evaluates one PackageReference on a given project. Domain may reference no
 * package at all ("Domain не зависит ни от чего" — high-level-architecture.md
 * §2); Application may not reference persistence/web-hosting packages, which
 * belong in Infrastructure/Api by convention (docs/conventions/backend.md).
 * Returns a violation message, or null if the package is allowed.
 */
export function checkPackageReference(project, packageId) {
  if (project.area !== 'module') return null;
  if (project.layer === 'Domain') {
    return `${projectLabel(project)} (Domain) must not reference NuGet package "${packageId}" — Domain depends on nothing`;
  }
  if (project.layer === 'Application' && INFRASTRUCTURE_PACKAGE_PATTERNS.some((p) => p.test(packageId))) {
    return `${projectLabel(project)} must not reference NuGet package "${packageId}" — persistence/web packages belong in Infrastructure or Api`;
  }
  return null;
}

/** Same cycle-detection algorithm as the frontend checker (kept duplicated,
 * not shared, per ADR-0012 §1: backend and frontend are self-contained). */
export function findCycles(edges) {
  const adjacency = new Map();
  for (const { from, to } of edges) {
    if (!adjacency.has(from)) adjacency.set(from, []);
    adjacency.get(from).push(to);
  }

  const visited = new Set();
  const stack = [];
  const onStack = new Set();
  const cycles = [];

  function visit(node) {
    visited.add(node);
    stack.push(node);
    onStack.add(node);
    for (const next of adjacency.get(node) ?? []) {
      if (!visited.has(next)) {
        visit(next);
      } else if (onStack.has(next)) {
        const start = stack.indexOf(next);
        cycles.push([...stack.slice(start), next]);
      }
    }
    stack.pop();
    onStack.delete(node);
  }

  for (const node of adjacency.keys()) {
    if (!visited.has(node)) visit(node);
  }
  return cycles;
}
