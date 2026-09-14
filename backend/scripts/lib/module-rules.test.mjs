// Examples of allowed and forbidden backend project/package dependencies
// (high-level-architecture.md §2, ADR-0006/0007/0009). Run with
// `node --test backend/scripts/lib/module-rules.test.mjs`.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { classifyProject, checkProjectEdge, checkPackageReference, findCycles } from './module-rules.mjs';

const walletsDomain = { area: 'module', module: 'Wallets', layer: 'Domain' };
const walletsApplication = { area: 'module', module: 'Wallets', layer: 'Application' };
const walletsInfrastructure = { area: 'module', module: 'Wallets', layer: 'Infrastructure' };
const walletsApi = { area: 'module', module: 'Wallets', layer: 'Api' };
const operationsApplication = { area: 'module', module: 'Operations', layer: 'Application' };
const operationsInfrastructure = { area: 'module', module: 'Operations', layer: 'Infrastructure' };
const referenceDataApplication = { area: 'module', module: 'ReferenceData', layer: 'Application' };
const sharedKernel = { area: 'shared-kernel' };
const buildingBlocksInfra = { area: 'building-blocks-infrastructure' };
const host = { area: 'host' };

test('classifyProject: recognizes each tracked area from its backend/src-relative path', () => {
  assert.deepEqual(classifyProject('Modules/Wallets/Domain/LupexWallet.Wallets.Domain.csproj'.split('/').slice(0, -1).join('/')), {
    area: 'module',
    module: 'Wallets',
    layer: 'Domain',
  });
  assert.deepEqual(classifyProject('BuildingBlocks/SharedKernel'), { area: 'shared-kernel' });
  assert.deepEqual(classifyProject('BuildingBlocks/Infrastructure'), { area: 'building-blocks-infrastructure' });
  assert.deepEqual(classifyProject('Host/LupexWallet.Api'), { area: 'host' });
});

test('checkProjectEdge: Domain may only reference SharedKernel', () => {
  assert.equal(checkProjectEdge(walletsDomain, sharedKernel), null);
});

test('checkProjectEdge: Domain must not reference its own module Application/Infrastructure', () => {
  assert.match(checkProjectEdge(walletsDomain, walletsApplication), /must not reference Wallets\.Application/);
  assert.match(checkProjectEdge(walletsDomain, walletsInfrastructure), /must not reference Wallets\.Infrastructure/);
});

test('checkProjectEdge: Domain must not reference another module Domain', () => {
  const operationsDomain = { area: 'module', module: 'Operations', layer: 'Domain' };
  assert.match(checkProjectEdge(walletsDomain, operationsDomain), /Domain depends on nothing but SharedKernel/);
});

test('checkProjectEdge: Application may reference its own Domain', () => {
  assert.equal(checkProjectEdge(walletsApplication, walletsDomain), null);
});

test('checkProjectEdge: Application may reference another module Application (ADR-0007 explicit contract)', () => {
  assert.equal(checkProjectEdge(operationsApplication, walletsApplication), null);
  assert.equal(checkProjectEdge(walletsApplication, referenceDataApplication), null);
});

test('checkProjectEdge: Application must not reference another module Domain or Infrastructure', () => {
  const operationsDomain = { area: 'module', module: 'Operations', layer: 'Domain' };
  assert.match(checkProjectEdge(walletsApplication, operationsDomain), /Application may only depend on its own Domain/);
  assert.match(checkProjectEdge(walletsApplication, operationsInfrastructure), /Application may only depend on its own Domain/);
});

test('checkProjectEdge: Infrastructure may reference own Application, SharedKernel, and BuildingBlocks.Infrastructure', () => {
  assert.equal(checkProjectEdge(walletsInfrastructure, walletsApplication), null);
  assert.equal(checkProjectEdge(walletsInfrastructure, sharedKernel), null);
  assert.equal(checkProjectEdge(walletsInfrastructure, buildingBlocksInfra), null);
});

test('checkProjectEdge: Infrastructure may reference another module Application (ADR-0009 reverse port) or Domain (event handlers)', () => {
  const operationsDomain = { area: 'module', module: 'Operations', layer: 'Domain' };
  assert.equal(checkProjectEdge(walletsInfrastructure, referenceDataApplication), null);
  assert.equal(checkProjectEdge(walletsInfrastructure, operationsDomain), null);
});

test('checkProjectEdge: Infrastructure must not reference another module Infrastructure or Api', () => {
  const operationsApi = { area: 'module', module: 'Operations', layer: 'Api' };
  assert.match(checkProjectEdge(walletsInfrastructure, operationsInfrastructure), /not another module's Infrastructure or Api/);
  assert.match(checkProjectEdge(walletsInfrastructure, operationsApi), /not another module's Infrastructure or Api/);
});

test('checkProjectEdge: Api may reference its own Application only', () => {
  assert.equal(checkProjectEdge(walletsApi, walletsApplication), null);
  assert.match(checkProjectEdge(walletsApi, walletsInfrastructure), /not its own Infrastructure, not other modules/);
  assert.match(checkProjectEdge(walletsApi, operationsApplication), /not its own Infrastructure, not other modules/);
});

test('checkProjectEdge: BuildingBlocks.Infrastructure must not depend on a specific module', () => {
  assert.equal(checkProjectEdge(buildingBlocksInfra, sharedKernel), null);
  assert.match(checkProjectEdge(buildingBlocksInfra, walletsApplication), /must not depend on Wallets\.Application/);
});

test('checkProjectEdge: SharedKernel must not depend on anything', () => {
  assert.match(checkProjectEdge(sharedKernel, buildingBlocksInfra), /must not reference anything/);
});

test('checkProjectEdge: Host (composition root) may reference anything', () => {
  const operationsApi = { area: 'module', module: 'Operations', layer: 'Api' };
  assert.equal(checkProjectEdge(host, walletsInfrastructure), null);
  assert.equal(checkProjectEdge(host, operationsApi), null);
});

test('checkPackageReference: Domain must not reference any NuGet package', () => {
  assert.match(checkPackageReference(walletsDomain, 'MediatR'), /Domain\) must not reference NuGet package "MediatR"/);
});

test('checkPackageReference: Application may reference MediatR', () => {
  assert.equal(checkPackageReference(walletsApplication, 'MediatR'), null);
});

test('checkPackageReference: Application must not reference persistence or web packages', () => {
  assert.match(checkPackageReference(walletsApplication, 'Microsoft.EntityFrameworkCore'), /persistence\/web packages belong in Infrastructure or Api/);
  assert.match(checkPackageReference(walletsApplication, 'Npgsql.EntityFrameworkCore.PostgreSQL'), /persistence\/web packages/);
  assert.match(checkPackageReference(walletsApplication, 'Microsoft.AspNetCore.OpenApi'), /persistence\/web packages/);
});

test('checkPackageReference: Infrastructure may reference persistence packages', () => {
  assert.equal(checkPackageReference(walletsInfrastructure, 'Microsoft.EntityFrameworkCore'), null);
  assert.equal(checkPackageReference(walletsInfrastructure, 'Npgsql.EntityFrameworkCore.PostgreSQL'), null);
});

test('findCycles: reports no cycle for the documented ADR-0007 direction (Operations.Application -> Wallets.Application)', () => {
  const edges = [
    { from: 'Operations.Application', to: 'Wallets.Application' },
    { from: 'Operations.Application', to: 'Operations.Domain' },
  ];
  assert.deepEqual(findCycles(edges), []);
});

test('findCycles: detects a two-project cycle', () => {
  const edges = [
    { from: 'Wallets.Application', to: 'Operations.Application' },
    { from: 'Operations.Application', to: 'Wallets.Application' },
  ];
  const cycles = findCycles(edges);
  assert.equal(cycles.length > 0, true);
});
