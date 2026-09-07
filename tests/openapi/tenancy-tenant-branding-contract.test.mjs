import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/tenancy-tenant-branding-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantBrandingContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Tenancy/Features/TenantBranding/Endpoint.cs'
);

test('租户品牌 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'tenancy-tenant-branding-v1');
  assert.match(endpointSource, /MapGet\("\/\{tenantId:guid\}\/branding"/u);
  assert.match(endpointSource, /WithName\("tenancyGetHostTenantBranding"\)/u);
  assert.match(contractsSource, /record TenantBrandingResponse/u);
  assert.match(contractsSource, /tenancy\.tenant_branding\.read/u);
  assert.match(contractsSource, /tenancy\.tenant_branding\.update/u);
});
