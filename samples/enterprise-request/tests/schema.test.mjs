import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

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
});
