import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-ldap-connections-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityLdapConnectionContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageLdapConnections/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('LDAP 连接 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-ldap-connections-v1');

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
          /^identity\.ldap_connections\.(read|create|update|delete|test|preview_sync)$/u
        );
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
    }
  }
});

test('LDAP 连接 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record LdapConnectionResponse/u);
  assert.match(contractsSource, /identity\.ldap_connections\.preview_sync/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/ldap-connections"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/identity/ldap-connections', 'MapGet("/",'],
    ['POST /api/v1/identity/ldap-connections', 'MapPost("/",'],
    ['GET /api/v1/identity/ldap-connections/{connectionId}', 'MapGet("/{connectionId:guid}",'],
    ['PUT /api/v1/identity/ldap-connections/{connectionId}', 'MapPut("/{connectionId:guid}",'],
    ['DELETE /api/v1/identity/ldap-connections/{connectionId}', 'MapDelete("/{connectionId:guid}",'],
    ['POST /api/v1/identity/ldap-connections/{connectionId}/disable', 'MapPost("/{connectionId:guid}/disable",'],
    ['POST /api/v1/identity/ldap-connections/{connectionId}/test-connection', 'MapPost("/{connectionId:guid}/test-connection",'],
    ['POST /api/v1/identity/ldap-connections/{connectionId}/test-authentication', 'MapPost("/{connectionId:guid}/test-authentication",'],
    ['POST /api/v1/identity/ldap-connections/{connectionId}/preview-sync', 'MapPost("/{connectionId:guid}/preview-sync",']
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
