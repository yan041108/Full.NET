import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/files-host-folders-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Files.Contracts/HostFolderContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Files/Features/ManageHostFolders/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 虚拟目录 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'files-host-folders-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/files\/host-folders/u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^files\.(files\.read|folders\.(create|update|delete))$/u
      );
      assert.ok(typeof operation.successStatus === 'number');
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('Host 虚拟目录 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record HostFolderTreeNode/u);
  assert.match(contractsSource, /record HostFolderResponse/u);
  assert.match(contractsSource, /files\.folders\.create/u);
  assert.match(contractsSource, /files\.folders\.update/u);
  assert.match(contractsSource, /files\.folders\.delete/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/files\/host-folders"\)/u);
  assert.match(endpointSource, /WithTags\("FilesHostFolders"\)/u);
  assert.match(endpointSource, /WithName\("filesGetHostFolderTree"\)/u);
  assert.match(endpointSource, /WithName\("filesCreateHostFolder"\)/u);
  assert.match(endpointSource, /WithName\("filesUpdateHostFolder"\)/u);
  assert.match(endpointSource, /WithName\("filesDeleteHostFolder"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/files/host-folders/tree', new Map([['GET', 'MapGet("/tree",']])],
    ['/api/v1/files/host-folders', new Map([['POST', 'MapPost("/",']])],
    ['/api/v1/files/host-folders/{folderId}/update', new Map([['POST', 'MapPost("/{folderId:guid}/update",']])],
    ['/api/v1/files/host-folders/{folderId}/delete', new Map([['POST', 'MapPost("/{folderId:guid}/delete",']])]
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
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(pascal, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
