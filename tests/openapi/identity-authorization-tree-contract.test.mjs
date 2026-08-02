import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-authorization-tree-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/AuthorizationTreeContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 授权树 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-authorization-tree-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.equal(operation.permission, 'identity.roles.read');
      assert.ok(typeof operation.successStatus === 'number');
    }
  }
});

test('授权树契约与源码端点及响应类型对齐', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(endpointSource, /\/api\/v1\/identity\/authorization-tree/u);
  assert.match(endpointSource, /IdentityRoleManagementPermissions\.Read/u);
  assert.match(contractsSource, /AuthorizationTreePageResponse/u);
  assert.match(contractsSource, /AuthorizationTreeActionResponse/u);
  assert.ok(contract.schemas.AuthorizationTreePageResponseCollection);
  assert.ok(contract.schemas.AuthorizationTreeActionResponse);
});