import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-host-users-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityUserManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 用户 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-host-users-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^identity\.users\.(read|write|export|import|disable|enable)$/u);
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

test('Host 用户 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateHostUserRequest/u);
  assert.match(contractsSource, /record UpdateHostUserRequest/u);
  assert.match(contractsSource, /record ResetHostUserPasswordRequest/u);
  assert.match(contractsSource, /record HostUserResponse/u);
  assert.match(contractsSource, /identity\.users\.read/u);
  assert.match(contractsSource, /identity\.users\.write/u);
  assert.match(contractsSource, /identity\.users\.export/u);
  assert.match(contractsSource, /HostUserProjectedFieldsResponse/u);

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/users"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/identity/users', new Map([
      ['GET', 'MapGet("/",'],
      ['POST', 'MapPost("/",']
    ])],
    ['/api/v1/identity/users/export', new Map([
      ['GET', 'MapGet("/export",']
    ])],
    ['/api/v1/identity/users/import', new Map([
      ['POST', 'MapPost("/import",']
    ])],
    ['/api/v1/identity/users/batch-disable', new Map([
      ['POST', 'MapPost("/batch-disable",']
    ])],
    ['/api/v1/identity/users/batch-enable', new Map([
      ['POST', 'MapPost("/batch-enable",']
    ])],
    ['/api/v1/identity/users/{userId}', new Map([
      ['GET', 'MapGet("/{userId:guid}",'],
      ['PUT', 'MapPut("/{userId:guid}",']
    ])],
    ['/api/v1/identity/users/{userId}/disable', new Map([
      ['POST', 'MapPost("/{userId:guid}/disable",']
    ])],
    ['/api/v1/identity/users/{userId}/enable', new Map([
      ['POST', 'MapPost("/{userId:guid}/enable",']
    ])],
    ['/api/v1/identity/users/{userId}/reset-password', new Map([
      ['POST', 'MapPost("/{userId:guid}/reset-password",']
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
    if (schemaName === 'HostUserResponsePage' || schemaName === 'HostUserResponseCollection') {
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

  assert.ok(contract.paths.some(entry => entry.path === '/api/v1/identity/users/export'));
  assert.ok(contract.paths.some(entry => entry.path === '/api/v1/identity/users/import'));
  assert.ok(contract.schemas.HostUserResponse.properties.includes('projectedFields'));
});
