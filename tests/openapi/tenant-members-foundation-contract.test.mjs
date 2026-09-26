import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/tenant-members-v1.json');
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/ManageTenantMembers/Endpoint.cs'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/TenantMembershipContracts.cs'
);

test('租户成员 OpenAPI 夹具覆盖当前成员与自行退出路径', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  assert.ok(contract.paths['/api/v1/identity/tenant-members']);
  assert.ok(contract.paths['/api/v1/identity/tenant-members/me']);
  assert.ok(contract.paths['/api/v1/identity/tenant-members/me/leave']);
  assert.match(endpointSource, /WithName\("identityGetCurrentTenantMember"\)/u);
  assert.match(endpointSource, /WithName\("identityLeaveCurrentTenant"\)/u);
  assert.match(contractsSource, /record LeaveTenantMembershipRequest/u);
});
