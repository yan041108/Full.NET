import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-open-access-clients-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityOpenAccessClientContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageOpenAccessClients/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('OpenAccess 接入方应用 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-open-access-clients-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^identity\.open_access_clients\.(read|create|update|disable|rotate)$/u
      );
      assert.ok(contract.schemas[operation.responseSchema]);
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
    }
  }
});

test('OpenAccess 接入方应用 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateOpenAccessClientRequest/u);
  assert.match(contractsSource, /record OpenAccessClientResponse/u);
  assert.match(contractsSource, /record CreateOpenAccessClientResponse/u);
  assert.match(contractsSource, /identity\.open_access_clients\.read/u);
  assert.match(contractsSource, /identity\.open_access_clients\.create/u);
  assert.match(contractsSource, /identity\.open_access_clients\.update/u);
  assert.match(contractsSource, /identity\.open_access_clients\.disable/u);
  assert.match(contractsSource, /identity\.open_access_clients\.rotate/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/open-access-clients"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/identity/open-access-clients', 'MapGet("/",'],
    ['POST /api/v1/identity/open-access-clients', 'MapPost("/",'],
    ['GET /api/v1/identity/open-access-clients/{clientId}', 'MapGet("/{clientId:guid}",'],
    ['PUT /api/v1/identity/open-access-clients/{clientId}', 'MapPut("/{clientId:guid}",'],
    [
      'POST /api/v1/identity/open-access-clients/{clientId}/disable',
      'MapPost("/{clientId:guid}/disable",'
    ],
    [
      'POST /api/v1/identity/open-access-clients/{clientId}/rotate',
      'MapPost("/{clientId:guid}/rotate",'
    ]
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `未登记的路由操作：${key}`);
      assert.ok(endpointSource.includes(marker), `端点源码缺少：${key}`);
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName === 'OpenAccessClientResponsePage') {
      continue;
    }
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
