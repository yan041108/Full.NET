import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/settings-tenant-dict-types-v1.json'
);
const dictTypeContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/DictTypeManagementContracts.cs'
);
const dictItemContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/DictItemManagementContracts.cs'
);
const tenantDictTypeContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/TenantDictTypeManagementContracts.cs'
);
const tenantDictTypeEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings/Features/ManageTenantDictTypes/Endpoint.cs'
);
const tenantDictItemEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings/Features/ManageTenantDictItems/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('租户数据字典 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'settings-tenant-dict-types-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/settings\/tenant-dict/u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^settings\.tenant_dict_types\.(read|write)$/u);
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

test('租户数据字典 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const dictTypeContractsSource = await readFile(dictTypeContractsSourcePath, 'utf8');
  const dictItemContractsSource = await readFile(dictItemContractsSourcePath, 'utf8');
  const tenantDictTypeContractsSource = await readFile(tenantDictTypeContractsSourcePath, 'utf8');
  const contractsSource = `${dictTypeContractsSource}\n${dictItemContractsSource}\n${tenantDictTypeContractsSource}`;
  const tenantDictTypeEndpointSource = await readFile(tenantDictTypeEndpointSourcePath, 'utf8');
  const tenantDictItemEndpointSource = await readFile(tenantDictItemEndpointSourcePath, 'utf8');

  assert.match(dictTypeContractsSource, /record DictTypeResponse/u);
  assert.match(dictTypeContractsSource, /record CreateDictTypeRequest/u);
  assert.match(dictTypeContractsSource, /record UpdateDictTypeRequest/u);
  assert.match(dictItemContractsSource, /record DictItemResponse/u);
  assert.match(dictItemContractsSource, /record CreateDictItemRequest/u);
  assert.match(dictItemContractsSource, /record UpdateDictItemRequest/u);
  assert.match(tenantDictTypeContractsSource, /settings\.tenant_dict_types\.read/u);
  assert.match(tenantDictTypeContractsSource, /settings\.tenant_dict_types\.write/u);
  assert.match(
    tenantDictTypeEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/tenant-dict-types"\)/u
  );
  assert.match(
    tenantDictItemEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/tenant-dict-types\/\{dictTypeId:guid\}\/items"\)/u
  );
  assert.match(
    tenantDictItemEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/tenant-dict-items"\)/u
  );

  const relativeRoutes = new Map([
    ['/api/v1/settings/tenant-dict-types', {
      source: tenantDictTypeEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/settings/tenant-dict-types/{dictTypeId}', {
      source: tenantDictTypeEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{dictTypeId:guid}",'],
        ['PUT', 'MapPut("/{dictTypeId:guid}",']
      ])
    }],
    ['/api/v1/settings/tenant-dict-types/{dictTypeId}/disable', {
      source: tenantDictTypeEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{dictTypeId:guid}/disable",']
      ])
    }],
    ['/api/v1/settings/tenant-dict-types/{dictTypeId}/items', {
      source: tenantDictItemEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/settings/tenant-dict-items/{dictItemId}', {
      source: tenantDictItemEndpointSource,
      markers: new Map([
        ['PUT', 'MapPut("/{dictItemId:guid}",']
      ])
    }],
    ['/api/v1/settings/tenant-dict-items/{dictItemId}/disable', {
      source: tenantDictItemEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{dictItemId:guid}/disable",']
      ])
    }]
  ]);

  for (const entry of contract.paths) {
    const route = relativeRoutes.get(entry.path);
    assert.ok(route, `未登记的路由组：${entry.path}`);
    for (const operation of entry.operations) {
      const marker = route.markers.get(operation.method);
      assert.ok(marker, `${entry.path} 缺少 ${operation.method}`);
      assert.match(
        route.source,
        new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u')
      );
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('Page')) {
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
