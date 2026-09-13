// Examples of allowed and forbidden dependencies (ADR-0012 §3). Run with
// `node --test frontend/scripts/lib/dependency-rules.test.mjs` — no test
// framework dependency, uses Node's built-in test runner (Node 18+).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classifyZone, checkEdge, findCycles } from './dependency-rules.mjs';

test('classifyZone: recognizes each tracked zone by its top-level directory', () => {
  assert.deepEqual(classifyZone('features/wallets/pages/wallets-page/wallets.component.ts'), {
    kind: 'feature',
    domain: 'wallets',
  });
  assert.deepEqual(classifyZone('data-access/operations/operations-api.service.ts'), {
    kind: 'data-access',
    domain: 'operations',
  });
  assert.deepEqual(classifyZone('core/auth/auth.guard.ts'), { kind: 'core' });
  assert.deepEqual(classifyZone('shared/ui/button/button.component.ts'), { kind: 'shared' });
  assert.deepEqual(classifyZone('layout/shell/shell.component.ts'), { kind: 'layout' });
  assert.deepEqual(classifyZone('app.routes.ts'), { kind: 'root' });
});

test('checkEdge: allows a feature to import its own files', () => {
  const wallets = { kind: 'feature', domain: 'wallets' };
  assert.equal(checkEdge(wallets, wallets), null);
});

test('checkEdge: forbids one feature importing another feature directly', () => {
  const operations = { kind: 'feature', domain: 'operations' };
  const wallets = { kind: 'feature', domain: 'wallets' };
  const reason = checkEdge(operations, wallets);
  assert.match(reason, /must not import feature "wallets" directly/);
});

test('checkEdge: allows a feature to import data-access, core, and shared', () => {
  const operations = { kind: 'feature', domain: 'operations' };
  assert.equal(checkEdge(operations, { kind: 'data-access', domain: 'wallets' }), null);
  assert.equal(checkEdge(operations, { kind: 'core' }), null);
  assert.equal(checkEdge(operations, { kind: 'shared' }), null);
});

test('checkEdge: forbids data-access depending on a feature', () => {
  const reason = checkEdge({ kind: 'data-access', domain: 'wallets' }, { kind: 'feature', domain: 'wallets' });
  assert.match(reason, /data-access:wallets must not depend on feature "wallets"/);
});

test('checkEdge: forbids core depending on a feature', () => {
  const reason = checkEdge({ kind: 'core' }, { kind: 'feature', domain: 'wallets' });
  assert.match(reason, /^core must not depend on feature "wallets"/);
});

test('checkEdge: forbids shared depending on a feature', () => {
  const reason = checkEdge({ kind: 'shared' }, { kind: 'feature', domain: 'operations' });
  assert.match(reason, /^shared must not depend on feature "operations"/);
});

test('checkEdge: allows data-access, core, and shared to depend on each other and on layout/root', () => {
  assert.equal(checkEdge({ kind: 'data-access', domain: 'wallets' }, { kind: 'shared' }), null);
  assert.equal(checkEdge({ kind: 'shared' }, { kind: 'core' }), null);
  assert.equal(checkEdge({ kind: 'layout' }, { kind: 'shared' }), null);
  assert.equal(checkEdge({ kind: 'root' }, { kind: 'core' }), null);
});

test('findCycles: reports no cycle for a plain DAG (data-access <- feature -> shared)', () => {
  const edges = [
    { from: 'features/wallets/wallets.component.ts', to: 'data-access/wallets/wallets-api.service.ts' },
    { from: 'features/wallets/wallets.component.ts', to: 'shared/ui/button.component.ts' },
    { from: 'data-access/wallets/wallets-api.service.ts', to: 'core/http/problem-details.ts' },
  ];
  assert.deepEqual(findCycles(edges), []);
});

test('findCycles: detects a direct two-file cycle', () => {
  const edges = [
    { from: 'shared/a.ts', to: 'shared/b.ts' },
    { from: 'shared/b.ts', to: 'shared/a.ts' },
  ];
  const cycles = findCycles(edges);
  assert.equal(cycles.length > 0, true);
  assert.ok(cycles[0].includes('shared/a.ts') && cycles[0].includes('shared/b.ts'));
});

test('findCycles: detects a longer indirect cycle across zones', () => {
  const edges = [
    { from: 'data-access/wallets/wallets-api.service.ts', to: 'shared/money/money.ts' },
    { from: 'shared/money/money.ts', to: 'core/http/problem-details.ts' },
    { from: 'core/http/problem-details.ts', to: 'data-access/wallets/wallets-api.service.ts' },
  ];
  const cycles = findCycles(edges);
  assert.equal(cycles.length > 0, true);
});

test('findCycles: a self-import is its own cycle', () => {
  const edges = [{ from: 'shared/a.ts', to: 'shared/a.ts' }];
  const cycles = findCycles(edges);
  assert.deepEqual(cycles, [['shared/a.ts', 'shared/a.ts']]);
});
