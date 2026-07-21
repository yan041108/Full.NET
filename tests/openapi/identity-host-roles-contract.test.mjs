import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-host-roles-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityRoleManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 角色 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-host-roles-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^identity\.roles\.(read|write)$/u);
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

test('Host 角色 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateHostRoleRequest/u);
  assert.match(contractsSource, /record UpdateHostRoleRequest/u);
  assert.match(contractsSource, /record ReplaceHostRolePermissionsRequest/u);
  assert.match(contractsSource, /record HostRoleResponse/u);
  assert.match(contractsSource, /identity\.roles\.read/u);
  assert.match(contractsSource, /identity\.roles\.write/u);

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/roles"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/identity/roles', new Map([
      ['GET', 'MapGet("/",'],
      ['POST', 'MapPost("/",']
    ])],
    ['/api/v1/identity/roles/{roleId}', new Map([
      ['GET', 'MapGet("/{roleId:guid}",'],
      ['PUT', 'MapPut("/{roleId:guid}",']
    ])],
    ['/api/v1/identity/roles/{roleId}/permissions', new Map([
      ['PUT', 'MapPut("/{roleId:guid}/permissions",']
    ])],
    ['/api/v1/identity/roles/{roleId}/disable', new Map([
      ['POST', 'MapPost("/{roleId:guid}/disable",']
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
    if (schemaName === 'HostRoleResponsePage') {
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
