import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/organization-host-user-management-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Organization.Contracts/HostUserManagementOrganizationContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Organization/Features/HostUserManagementReference/Endpoint.cs');

test('Host 用户组织参考 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'organization-host-user-management-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/organization\/host-user-management"\)/u);
  assert.match(endpointSource, /WithTags\("OrganizationHostUserManagement"\)/u);
  assert.match(endpointSource, /WithName\("organizationGetHostUserManagementReference"\)/u);
  assert.match(endpointSource, /WithName\("organizationCreateHostUserManagementUserUnit"\)/u);
  assert.match(endpointSource, /WithName\("organizationUpdateHostUserManagementUserUnit"\)/u);
  assert.match(endpointSource, /WithName\("organizationDisableHostUserManagementUserUnit"\)/u);
  assert.match(endpointSource, /WithName\("organizationCreateHostUserManagementUserPosition"\)/u);
  assert.match(endpointSource, /WithName\("organizationUpdateHostUserManagementUserPosition"\)/u);
  assert.match(endpointSource, /WithName\("organizationDisableHostUserManagementUserPosition"\)/u);
  assert.match(contractsSource, /HostUserManagementOrganizationReferenceResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/reference')));
});