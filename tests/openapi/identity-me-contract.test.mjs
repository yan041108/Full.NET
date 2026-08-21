import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/identity-me-v1.json');
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/GetCurrentUser/Endpoint.cs'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/CurrentUserResponse.cs'
);

test('identity-me-v1 OpenAPI 夹具包含 GET /api/v1/me', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  assert.ok(contract.paths['/api/v1/me']);
  assert.ok(contract.paths['/api/v1/me'].get);
  assert.equal(contract.paths['/api/v1/me'].get.operationId, 'identityGetCurrentUser');
});

test('identity-me-v1 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  const contractsSource = await readFile(contractsSourcePath, 'utf8');

  assert.match(endpointSource, /MapGet\("\/api\/v1\/me"/u);
  assert.match(endpointSource, /WithName\("identityGetCurrentUser"\)/u);
  assert.match(endpointSource, /WithTags\("IdentityMe"\)/u);
  assert.match(contractsSource, /record CurrentUserResponse/u);

  for (const property of contract.schemas.CurrentUserResponse.properties) {
    const pascal = property.charAt(0).toUpperCase() + property.slice(1);
    assert.match(
      contractsSource,
      new RegExp(pascal, 'u'),
      `CurrentUserResponse.${property} 未在 C# 契约中找到`
    );
  }
});
