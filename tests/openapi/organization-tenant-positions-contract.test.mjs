import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/organization-tenant-positions-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationPositionManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositions/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('租户职位 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'organization-tenant-positions-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^organization\.positions\.(read|create|update|disable|assign_unit|assign_position_level)$/u);
      assert.ok(typeof operation.successStatus === 'number');
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('租户职位 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateOrganizationPositionRequest/u);
  assert.match(contractsSource, /record UpdateOrganizationPositionRequest/u);
  assert.match(contractsSource, /record AssignOrganizationPositionUnitRequest/u);
  assert.match(contractsSource, /record AssignOrganizationPositionLevelRequest/u);
  assert.match(contractsSource, /record OrganizationPositionResponse/u);
  assert.match(contractsSource, /organization\.positions\.read/u);
  assert.match(contractsSource, /organization\.positions\.create/u);
  assert.match(contractsSource, /organization\.positions\.update/u);
  assert.match(contractsSource, /organization\.positions\.disable/u);
  assert.match(contractsSource, /organization\.positions\.assign_unit/u);
  assert.match(contractsSource, /organization\.positions\.assign_position_level/u);
  assert.match(contractsSource, /organization\.positions\.write/u);

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/organization\/positions"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/organization/positions', new Map([
      ['GET', 'MapGet("/",'],
      ['POST', 'MapPost("/",']
    ])],
    ['/api/v1/organization/positions/{positionId}', new Map([
      ['GET', 'MapGet("/{positionId:guid}",'],
      ['PUT', 'MapPut("/{positionId:guid}",']
    ])],
    ['/api/v1/organization/positions/{positionId}/disable', new Map([
      ['POST', 'MapPost("/{positionId:guid}/disable",']
    ])],
    ['/api/v1/organization/positions/{positionId}/unit', new Map([
      ['PUT', 'MapPut("/{positionId:guid}/unit",']
    ])],
    ['/api/v1/organization/positions/{positionId}/position-level', new Map([
      ['PUT', 'MapPut("/{positionId:guid}/position-level",']
    ])]
  ]);

  for (const entry of contract.paths) {
    const routes = relativeRoutes.get(entry.path);
    assert.ok(routes, `未登记的路由组：${entry.path}`);
    for (const operation of entry.operations) {
      const marker = routes.get(operation.method);
      assert.ok(marker, `${entry.path} 缺少 ${operation.method}`);
      assert.match(endpointSource, new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u'));
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName === 'OrganizationPositionResponsePage') {
      continue;
    }
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`${pascal}`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
