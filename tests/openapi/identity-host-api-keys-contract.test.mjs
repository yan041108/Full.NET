import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-host-api-keys-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityApiKeyManagementContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageHostApiKeys/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host API Key OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-host-api-keys-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^identity\.api_keys\.(read|write)$/u);
      assert.ok(contract.schemas[operation.responseSchema]);
    }
  }
});

test('Host API Key OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record CreateHostApiKeyRequest/u);
  assert.match(contractsSource, /record HostApiKeyResponse/u);
  assert.match(contractsSource, /record CreateHostApiKeyResponse/u);
  assert.match(contractsSource, /identity\.api_keys\.read/u);
  assert.match(contractsSource, /identity\.api_keys\.write/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/api-keys"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/identity/api-keys', 'MapGet("/",'],
    ['POST /api/v1/identity/api-keys', 'MapPost("/",'],
    [
      'POST /api/v1/identity/api-keys/{apiKeyId}/disable',
      'MapPost("/{apiKeyId:guid}/disable",'
    ],
    [
      'POST /api/v1/identity/api-keys/{apiKeyId}/rotate',
      'MapPost("/{apiKeyId:guid}/rotate",'
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
    if (schemaName === 'HostApiKeyResponsePage') {
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
