import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/regions-administrative-regions-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Regions/Contracts/AdministrativeRegionContracts.cs'
);
const permissionsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Regions/Contracts/RegionsPermissions.cs'
);
const endpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Regions/Features/ManageAdministrativeRegions/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

function isValidRegionsPermission(permission) {
  return /^regions\.administrative_regions\.(read|create|update|delete|import)$/u.test(
    permission
  );
}

test('行政区域 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'regions-administrative-regions-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/regions\/administrative-regions/u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok(isValidRegionsPermission(operation.permission), `非法权限码：${operation.permission}`);
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.successStatus === 204) {
        assert.equal(operation.responseSchema, undefined);
      } else {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('行政区域 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const permissionsSource = await readFile(permissionsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointPath, 'utf8');

  for (const permission of [
    'regions.administrative_regions.read',
    'regions.administrative_regions.create',
    'regions.administrative_regions.update',
    'regions.administrative_regions.delete',
    'regions.administrative_regions.import'
  ]) {
    assert.ok(permissionsSource.includes(permission), `C# 契约缺少权限码：${permission}`);
  }

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/regions\/administrative-regions"\)/u);
  assert.match(endpointSource, /WithTags\("RegionsAdministrativeRegions"\)/u);
  assert.match(endpointSource, /WithName\("regionsListAdministrativeRegionChildren"\)/u);
  assert.match(endpointSource, /WithName\("regionsGetAdministrativeRegionTree"\)/u);
  assert.match(endpointSource, /WithName\("regionsListAdministrativeRegions"\)/u);
  assert.match(endpointSource, /WithName\("regionsCreateAdministrativeRegion"\)/u);
  assert.match(endpointSource, /WithName\("regionsGetLatestAdministrativeRegionDatasetManifest"\)/u);
  assert.match(endpointSource, /WithName\("regionsGetAdministrativeRegion"\)/u);
  assert.match(endpointSource, /WithName\("regionsUpdateAdministrativeRegion"\)/u);
  assert.match(endpointSource, /WithName\("regionsDeleteAdministrativeRegion"\)/u);
  assert.match(endpointSource, /WithName\("regionsPreviewAdministrativeRegionImport"\)/u);
  assert.match(endpointSource, /WithName\("regionsApplyAdministrativeRegionImport"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/regions/administrative-regions/children', 'MapGet("/children",'],
    ['GET /api/v1/regions/administrative-regions/tree', 'MapGet("/tree",'],
    ['GET /api/v1/regions/administrative-regions', 'MapGet("/",'],
    ['POST /api/v1/regions/administrative-regions', 'MapPost("/",'],
    [
      'GET /api/v1/regions/administrative-regions/dataset-manifest/latest',
      'MapGet("/dataset-manifest/latest",'
    ],
    ['GET /api/v1/regions/administrative-regions/{regionId}', 'MapGet("/{regionId:guid}",'],
    ['PUT /api/v1/regions/administrative-regions/{regionId}', 'MapPut("/{regionId:guid}",'],
    [
      'POST /api/v1/regions/administrative-regions/{regionId}/delete',
      'MapPost("/{regionId:guid}/delete",'
    ],
    [
      'POST /api/v1/regions/administrative-regions/import/preview',
      'MapPost("/import/preview",'
    ],
    [
      'POST /api/v1/regions/administrative-regions/import/apply',
      'MapPost("/import/apply",'
    ]
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `未登记的路由操作：${key}`);
      assert.ok(endpointSource.includes(marker), `端点源码缺少：${key}`);
      assert.ok(
        endpointSource.includes(RegionsPermissionsFromContract(operation.permission)),
        `Endpoint 缺少权限：${operation.permission}`
      );
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('Page') || schemaName.endsWith('List')) {
      continue;
    }

    assert.match(contractsSource, new RegExp(`record ${schemaName}\\b`, 'u'));
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`\\b${pascal}\\b`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});

function RegionsPermissionsFromContract(permission) {
  const map = {
    'regions.administrative_regions.read': 'RegionsPermissions.Read',
    'regions.administrative_regions.create': 'RegionsPermissions.Create',
    'regions.administrative_regions.update': 'RegionsPermissions.Update',
    'regions.administrative_regions.delete': 'RegionsPermissions.Delete',
    'regions.administrative_regions.import': 'RegionsPermissions.Import'
  };
  return map[permission];
}
