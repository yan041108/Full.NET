import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/settings-dict-types-v1.json'
);
const dictTypeContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/DictTypeManagementContracts.cs'
);
const dictItemContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/DictItemManagementContracts.cs'
);
const dictTypeEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings/Features/ManageHostDictTypes/Endpoint.cs'
);
const dictItemEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings/Features/ManageHostDictItems/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 数据字典 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'settings-dict-types-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/settings\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^settings\.dict_types\.(read|create|update|disable)$/u
      );
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

test('Host 数据字典 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const dictTypeContractsSource = await readFile(dictTypeContractsSourcePath, 'utf8');
  const dictItemContractsSource = await readFile(dictItemContractsSourcePath, 'utf8');
  const contractsSource = `${dictTypeContractsSource}\n${dictItemContractsSource}`;
  const dictTypeEndpointSource = await readFile(dictTypeEndpointSourcePath, 'utf8');
  const dictItemEndpointSource = await readFile(dictItemEndpointSourcePath, 'utf8');

  assert.match(dictTypeContractsSource, /record DictTypeResponse/u);
  assert.match(dictTypeContractsSource, /record CreateDictTypeRequest/u);
  assert.match(dictTypeContractsSource, /record UpdateDictTypeRequest/u);
  assert.match(dictItemContractsSource, /record DictItemResponse/u);
  assert.match(dictItemContractsSource, /record CreateDictItemRequest/u);
  assert.match(dictItemContractsSource, /record UpdateDictItemRequest/u);
  assert.match(dictTypeContractsSource, /settings\.dict_types\.read/u);
  assert.match(dictTypeContractsSource, /settings\.dict_types\.create/u);
  assert.match(dictTypeContractsSource, /settings\.dict_types\.update/u);
  assert.match(dictTypeContractsSource, /settings\.dict_types\.disable/u);
  assert.match(dictTypeContractsSource, /settings\.dict_types\.write/u);
  assert.match(
    dictTypeEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/dict-types"\)/u
  );
  assert.match(
    dictItemEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/dict-types\/\{dictTypeId:guid\}\/items"\)/u
  );
  assert.match(
    dictItemEndpointSource,
    /MapGroup\("\/api\/v1\/settings\/dict-items"\)/u
  );

  // 每条夹具路由都必须能定位到具体的 Map* 调用，避免夹具与实现漂移。
  const relativeRoutes = new Map([
    ['/api/v1/settings/dict-types', {
      source: dictTypeEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/settings/dict-types/{dictTypeId}', {
      source: dictTypeEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{dictTypeId:guid}",'],
        ['PUT', 'MapPut("/{dictTypeId:guid}",']
      ])
    }],
    ['/api/v1/settings/dict-types/{dictTypeId}/disable', {
      source: dictTypeEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{dictTypeId:guid}/disable",']
      ])
    }],
    ['/api/v1/settings/dict-types/{dictTypeId}/items', {
      source: dictItemEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",'],
        ['POST', 'MapPost("/",']
      ])
    }],
    ['/api/v1/settings/dict-items/{dictItemId}', {
      source: dictItemEndpointSource,
      markers: new Map([
        ['PUT', 'MapPut("/{dictItemId:guid}",']
      ])
    }],
    ['/api/v1/settings/dict-items/{dictItemId}/disable', {
      source: dictItemEndpointSource,
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
