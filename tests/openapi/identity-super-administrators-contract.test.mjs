import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/identity-super-administrators-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Identity.Contracts/SuperAdministratorContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Identity/Features/ManageSuperAdministrators/Endpoint.cs');

test('超级管理员 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'identity-super-administrators-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/identity\/super-administrators"\)/u);
  assert.match(endpointSource, /WithName\("identityListSuperAdministrators"\)/u);
  assert.match(endpointSource, /WithName\("identityListSuperAdministratorAudits"\)/u);
  assert.match(endpointSource, /WithName\("identityGrantSuperAdministrator"\)/u);
  assert.match(endpointSource, /WithName\("identityRevokeSuperAdministrator"\)/u);
  assert.match(endpointSource, /WithTags\("IdentitySuperAdministrators"\)/u);
  assert.match(endpointSource, /RequireRateLimiting\("identity-super-administrator-write"\)/u);
  assert.match(contractsSource, /record SuperAdministratorResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/grant')));
});
