import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/files-storage-providers-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Files.Contracts/StorageProviderContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Files/Features/ManageStorageProviders/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('存储 Provider OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'files-storage-providers-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      if (operation.permission) {
        assert.match(
          operation.permission,
          /^files\.storage_providers\.(read|test)$/u
        );
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('存储 Provider OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record StorageProviderCatalogItem/u);
  assert.match(contractsSource, /files\.storage_providers\.test/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/files\/storage-providers"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/files/storage-providers', 'MapGet("/",'],
    ['POST /api/v1/files/storage-providers/{providerKey}/test', 'MapPost("/{providerKey}/test",']
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `缺少路由标记：${key}`);
      assert.ok(endpointSource.includes(marker), `缺少路由标记：${key} -> ${marker}`);
    }
  }
});
