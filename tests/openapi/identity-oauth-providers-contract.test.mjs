import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-oauth-providers-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityOAuthContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageOAuthProviders/Endpoint.cs'
);
const publicEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/OAuthFlow/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('OAuth 提供程序 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-oauth-providers-v1');

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
          /^identity\.oauth_providers\.(read|create|update|delete)$/u
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

test('OAuth 提供程序 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  const publicEndpointSource = await readFile(publicEndpointSourcePath, 'utf8');

  assert.match(contractsSource, /record OAuthProviderResponse/u);
  assert.match(contractsSource, /identity\.oauth_providers\.delete/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/oauth-providers"\)/u);
  assert.match(publicEndpointSource, /MapGet\("\/api\/v1\/identity\/oauth\/providers"/u);

  const routeMarkers = new Map([
    ['GET /api/v1/identity/oauth-providers', 'MapGet("/",'],
    ['POST /api/v1/identity/oauth-providers', 'MapPost("/",'],
    ['GET /api/v1/identity/oauth-providers/{providerId}', 'MapGet("/{providerId:guid}",'],
    ['PUT /api/v1/identity/oauth-providers/{providerId}', 'MapPut("/{providerId:guid}",'],
    ['DELETE /api/v1/identity/oauth-providers/{providerId}', 'MapDelete("/{providerId:guid}",'],
    ['GET /api/v1/identity/oauth/providers', 'MapGet("/api/v1/identity/oauth/providers",'],
    ['GET /api/v1/identity/oauth/{providerKey}/authorize', 'MapGet("/api/v1/identity/oauth/{providerKey}/authorize",'],
    ['GET /api/v1/identity/oauth/callback', 'MapGet("/api/v1/identity/oauth/callback",']
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `缺少路由标记：${key}`);
      const source = key.includes('/oauth/providers') || key.includes('/oauth/{providerKey}')
        || key.includes('/oauth/callback')
        ? publicEndpointSource
        : endpointSource;
      assert.ok(source.includes(marker), `缺少路由标记：${key} -> ${marker}`);
    }
  }
});
