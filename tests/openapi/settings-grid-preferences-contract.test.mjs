import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/settings-grid-preferences-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings.Contracts/GridPreferenceContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Settings/Features/ManageMyGridPreferences/Endpoint.cs'
);

test('Grid 偏好契约冻结认证端点与 schema', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.equal(contract.id, 'settings-grid-preferences-v1');
  assert.equal(contract.paths.length, 1);
  assert.equal(
    contract.paths[0].path,
    '/api/v1/me/grid-preferences/{gridKey}'
  );
  assert.deepEqual(
    contract.paths[0].operations.map(operation => operation.method),
    ['GET', 'PUT', 'DELETE']
  );
  assert.ok(contract.paths[0].operations.every(
    operation => operation.authorization === 'authenticated'
  ));
  assert.deepEqual(
    contract.paths[0].operations.map(operation => operation.errorStatuses),
    [[401, 404], [400, 401, 404, 409], [401, 404]]
  );
  assert.match(
    endpointSource,
    /MapGroup\("\/api\/v1\/me\/grid-preferences"\)/u
  );
  assert.match(endpointSource, /RequireAuthorization\(\)/u);
  assert.match(endpointSource, /MapGet\("\/\{gridKey\}"/u);
  assert.match(endpointSource, /MapPut\("\/\{gridKey\}"/u);
  assert.match(endpointSource, /MapDelete\("\/\{gridKey\}"/u);
  for (const status of [400, 401, 404, 409]) {
    assert.match(
      endpointSource,
      new RegExp(`ProducesProblem\\(StatusCodes\\.Status${status}`, 'u')
    );
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    assert.match(contractsSource, new RegExp(`record ${schemaName}`, 'u'));
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(contractsSource, new RegExp(`\\b${pascal}\\b`, 'u'));
    }
  }
});
