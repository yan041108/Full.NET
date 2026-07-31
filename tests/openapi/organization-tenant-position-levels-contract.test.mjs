import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/organization-tenant-position-levels-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationPositionLevelManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositionLevels/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('租户职级 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'organization-tenant-position-levels-v1');
  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^organization\.position_levels\.(read|write)$/u
      );
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('租户职级 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateOrganizationPositionLevelRequest/u);
  assert.match(contractsSource, /record UpdateOrganizationPositionLevelRequest/u);
  assert.match(contractsSource, /record OrganizationPositionLevelResponse/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/organization\/position-levels"\)/u);

  const sourceMarkers = new Map([
    ['GET /api/v1/organization/position-levels', 'MapGet("/",'],
    ['POST /api/v1/organization/position-levels', 'MapPost("/",'],
    ['GET /api/v1/organization/position-levels/{positionLevelId}', 'MapGet("/{positionLevelId:guid}",'],
    ['PUT /api/v1/organization/position-levels/{positionLevelId}', 'MapPut("/{positionLevelId:guid}",'],
    ['POST /api/v1/organization/position-levels/{positionLevelId}/disable', 'MapPost("/{positionLevelId:guid}/disable",']
  ]);
  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.match(endpointSource, new RegExp(
        sourceMarkers.get(key).replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'),
        'u'
      ));
    }
  }
});
