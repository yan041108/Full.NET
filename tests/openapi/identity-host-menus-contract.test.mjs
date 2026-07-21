import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-host-menus-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityMenuManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageHostMenus/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 菜单 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-host-menus-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^identity\.menus\.(read|write)$/u);
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

test('Host 菜单 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateHostMenuRequest/u);
  assert.match(contractsSource, /record UpdateHostMenuRequest/u);
  assert.match(contractsSource, /record HostMenuResponse/u);
  assert.match(contractsSource, /identity\.menus\.read/u);
  assert.match(contractsSource, /identity\.menus\.write/u);

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/menus"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/identity/menus', new Map([
      ['GET', 'MapGet("/",'],
      ['POST', 'MapPost("/",']
    ])],
    ['/api/v1/identity/menus/{menuId}', new Map([
      ['GET', 'MapGet("/{menuId:guid}",'],
      ['PUT', 'MapPut("/{menuId:guid}",']
    ])],
    ['/api/v1/identity/menus/{menuId}/disable', new Map([
      ['POST', 'MapPost("/{menuId:guid}/disable",']
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
    if (schemaName === 'HostMenuResponsePage') {
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
