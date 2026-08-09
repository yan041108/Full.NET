import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-organization-unit-projection-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity.Contracts/OrganizationUnitProjectionContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Identity/Features/OrganizationUnitProjection/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('organization unit projection reconcile OpenAPI fixture is structurally valid', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'identity-organization-unit-projection-v1');
  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/identity\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `duplicate operation: ${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^identity\.organization_unit_projections\.reconcile_(dry_run|apply)(\|identity\.organization_unit_projections\.reconcile_(dry_run|apply))?$/u
      );
    }
  }
});

test('organization unit projection reconcile OpenAPI fixture matches C# contracts and endpoint', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record ReconcileOrganizationUnitProjectionRequest/u);
  assert.match(contractsSource, /record ReconcileOrganizationUnitProjectionResponse/u);
  assert.match(contractsSource, /identity\.organization_unit_projections\.reconcile_dry_run/u);
  assert.match(contractsSource, /identity\.organization_unit_projections\.reconcile_apply/u);
  assert.match(
    endpointSource,
    /MapPost\(\s*"\/api\/v1\/identity\/organization-unit-projections\/reconcile"/u
  );

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`${pascal}`, 'u'),
        `${schemaName}.${property} missing from C# contracts`
      );
    }
  }
});
