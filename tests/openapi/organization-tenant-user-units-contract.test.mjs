import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/organization-tenant-user-units-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationUserUnitManagementContracts.cs'
);
const assignableContractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization.Contracts/OrganizationAssignableUserContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('租户用户-机构隶属 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'organization-tenant-user-units-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^organization\.user_units\.(read|write)$/u);
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

test('租户用户-机构隶属 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = [
    await readFile(contractsSourcePath, 'utf8'),
    await readFile(assignableContractsSourcePath, 'utf8')
  ].join('\n');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateOrganizationUserUnitRequest/u);
  assert.match(contractsSource, /record UpdateOrganizationUserUnitRequest/u);
  assert.match(contractsSource, /record OrganizationUserUnitResponse/u);
  assert.match(contractsSource, /record OrganizationAssignableUserResponse/u);
  assert.match(contractsSource, /organization\.user_units\.read/u);
  assert.match(contractsSource, /organization\.user_units\.write/u);

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/organization\/user-units"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/organization/user-units/assignable-users', new Map([
      ['GET', 'MapGet("/assignable-users",']
    ])],
    ['/api/v1/organization/user-units', new Map([
      ['GET', 'MapGet("/",'],
      ['POST', 'MapPost("/",']
    ])],
    ['/api/v1/organization/user-units/{assignmentId}', new Map([
      ['PUT', 'MapPut("/{assignmentId:guid}",']
    ])],
    ['/api/v1/organization/user-units/{assignmentId}/disable', new Map([
      ['POST', 'MapPost("/{assignmentId:guid}/disable",']
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
    if (schemaName.endsWith('ResponsePage')) {
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
