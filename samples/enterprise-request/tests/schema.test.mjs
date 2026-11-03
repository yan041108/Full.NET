import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { generateFullNetClient } from '../../../scripts/openapi/generate-fullnet-client.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

test('application-owned aggregate client matches the frozen business OpenAPI', async () => {
  await generateFullNetClient({
    inputPath: path.join(root, 'contracts/openapi/enterprise-requests.openapi.json'),
    manifestPath: path.join(root, 'contracts/openapi/client-generation-manifest.json'),
    outputDirectory: path.resolve(root, '../../ui/admin/src/api/enterprise-request-client'),
    httpModuleSpecifier: '@fullnet/client-contracts',
    check: true
  });
});

test('enterprise-request schema declares master.detail header-line relationship', async () => {
  const raw = await readFile(path.join(root, 'schema.json'), 'utf8');
  const schema = JSON.parse(raw);
  assert.equal(schema.scene, 'master.detail');
  assert.equal(schema.ownerKey, 'demo');
  assert.equal(schema.entityKey, 'enterprise_request');
  assert.equal(schema.apiResourceName, 'enterprise-requests');
  assert.equal(schema.databaseTableName, 'demo_enterprise_request_enterprise_request');
  assert.ok(Array.isArray(schema.relationships) && schema.relationships.length === 1);
  const rel = schema.relationships[0];
  assert.equal(rel.principalEntityKey, 'enterprise_request');
  assert.equal(rel.dependentEntityKey, 'enterprise_request_line');
  assert.equal(rel.dependentColumnName, 'RequestId');
  assert.deepEqual(rel.compositeKeyColumnNames, ['RequestId', 'Id']);
  assert.equal(rel.dependentSchema.entityKey, rel.dependentEntityKey);
  assert.equal(rel.dependentSchema.entityCapabilities.ownershipMode, 'organization.unit');
  assert.ok(rel.dependentSchema.columns.some(column => column.databaseName === 'OrganizationUnitId'));
});
