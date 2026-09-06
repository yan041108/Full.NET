import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-registration-ways-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/IdentityRegistrationContracts.cs'
);
const policyEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageRegistrationPolicy/Endpoint.cs'
);
const waysEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageRegistrationWays/Endpoint.cs'
);
const publicEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/PublicRegistrationWays/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('注册方式 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-registration-ways-v1');

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
          /^identity\.registration_(policy|ways)\.(read|update|create|delete)$/u
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

test('注册方式 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const policyEndpointSource = await readFile(policyEndpointSourcePath, 'utf8');
  const waysEndpointSource = await readFile(waysEndpointSourcePath, 'utf8');
  const publicEndpointSource = await readFile(publicEndpointSourcePath, 'utf8');

  assert.match(contractsSource, /record RegistrationPolicyResponse/u);
  assert.match(contractsSource, /record RegistrationWayResponse/u);
  assert.match(contractsSource, /identity\.registration_policy\.read/u);
  assert.match(contractsSource, /identity\.registration_ways\.delete/u);
  assert.match(policyEndpointSource, /MapGroup\("\/api\/v1\/identity\/registration-policy"\)/u);
  assert.match(waysEndpointSource, /MapGroup\("\/api\/v1\/identity\/registration-ways"\)/u);
  assert.match(publicEndpointSource, /\/api\/v1\/identity\/public\/registration-ways/u);

  const routeMarkers = new Map([
    ['GET /api/v1/identity/registration-policy', 'MapGet("/",'],
    ['PUT /api/v1/identity/registration-policy', 'MapPut("/",'],
    ['GET /api/v1/identity/registration-ways', 'MapGet("/",'],
    ['POST /api/v1/identity/registration-ways', 'MapPost("/",'],
    ['GET /api/v1/identity/registration-ways/{wayId}', 'MapGet("/{wayId:guid}",'],
    ['PUT /api/v1/identity/registration-ways/{wayId}', 'MapPut("/{wayId:guid}",'],
    ['DELETE /api/v1/identity/registration-ways/{wayId}', 'MapDelete("/{wayId:guid}",'],
    ['GET /api/v1/identity/public/registration-ways', 'MapGet("/api/v1/identity/public/registration-ways",']
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `缺少路由标记：${key}`);
      const source = key.includes('registration-policy')
        ? policyEndpointSource
        : key.includes('/public/')
          ? publicEndpointSource
          : waysEndpointSource;
      assert.ok(source.includes(marker), `缺少路由标记：${key} -> ${marker}`);
    }
  }
});
