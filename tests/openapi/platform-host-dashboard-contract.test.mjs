import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/platform-host-dashboard-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/HostDashboardContracts.cs'
);
const authorizationSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/GetHostDashboardSummary/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 工作台 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'platform-host-dashboard-v1');
  assert.equal(contract.paths.length, 1);

  const [pathEntry] = contract.paths;
  assert.equal(pathEntry.path, '/api/v1/platform/host-dashboard-summary');
  assert.equal(pathEntry.operations.length, 1);
  assert.deepEqual(pathEntry.operations[0], {
    method: 'GET',
    permission: 'platform.dashboard.read',
    successStatus: 200,
    responseSchema: 'HostDashboardSummaryResponse'
  });
});

test('Host 工作台 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const authorizationSource = await readFile(authorizationSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(authorizationSource, /DashboardRead = "platform\.dashboard\.read"/u);
  assert.match(
    endpointSource,
    /MapGet\(\s*"\/api\/v1\/platform\/host-dashboard-summary"/u
  );
  assert.match(endpointSource, /WithTags\("PlatformHostDashboard"\)/u);
  assert.match(endpointSource, /WithName\("platformGetHostDashboardSummary"\)/u);
  assert.match(endpointSource, /Produces<HostDashboardSummaryResponse>/u);

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    assert.match(contractsSource, new RegExp(`record ${schemaName}\\b`, 'u'));
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
