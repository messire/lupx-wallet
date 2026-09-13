// Pure, framework-agnostic rules for the frontend dependency graph (ADR-0012 §3,
// docs/conventions/frontend.md). No filesystem or TypeScript parsing here — this
// module only classifies already-resolved file paths and evaluates edges, so the
// rules themselves are unit-testable with synthetic examples
// (see check-feature-boundaries.test.mjs).

/**
 * Classifies a path relative to `src/app` (forward slashes) into a dependency
 * zone. Files directly under `src/app` (app.routes.ts, app.config.ts, ...) and
 * any directory not named after a tracked zone classify as "root" — they have
 * no zone-specific restriction of their own, but appear as valid edge targets.
 */
export function classifyZone(relPath) {
  const segments = relPath.split('/');
  const [first, second] = segments;
  if (first === 'features' && second) return { kind: 'feature', domain: second };
  if (first === 'data-access' && second) return { kind: 'data-access', domain: second };
  if (first === 'core') return { kind: 'core' };
  if (first === 'shared') return { kind: 'shared' };
  if (first === 'layout') return { kind: 'layout' };
  return { kind: 'root' };
}

export function zoneLabel(zone) {
  return zone.domain ? `${zone.kind}:${zone.domain}` : zone.kind;
}

const ZONES_FORBIDDEN_FROM_DEPENDING_ON_FEATURES = new Set(['core', 'shared', 'data-access']);

/**
 * Evaluates one dependency edge (source imports target) against ADR-0012 §3:
 * a feature must not import a different feature directly, and core/shared/
 * data-access must not depend on any feature. Returns a violation message,
 * or null if the edge is allowed.
 */
export function checkEdge(sourceZone, targetZone) {
  if (sourceZone.kind === 'feature' && targetZone.kind === 'feature' && sourceZone.domain !== targetZone.domain) {
    return `feature "${sourceZone.domain}" must not import feature "${targetZone.domain}" directly — use data-access, core, or shared instead`;
  }
  if (ZONES_FORBIDDEN_FROM_DEPENDING_ON_FEATURES.has(sourceZone.kind) && targetZone.kind === 'feature') {
    return `${zoneLabel(sourceZone)} must not depend on feature "${targetZone.domain}" (ADR-0012 §3: core/shared/data-access must not depend on features)`;
  }
  return null;
}

/**
 * Finds cycles in a directed graph given as an edge list of {from, to} node
 * ids. Returns each distinct cycle as an array of node ids (first === last).
 * The same cycle may be reported once per node it passes through when several
 * are unvisited entry points into it; callers that only need existence can
 * take cycles[0].
 */
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
