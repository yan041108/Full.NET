import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/document-host-version-retention-v1.json'
);

test('Host 版本保留策略 OpenAPI 夹具包含 GET 与 PUT', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const entry = contract.paths.find(
    item => item.path === '/api/v1/document/host/version-retention'
  );
  assert.ok(entry);
  const methods = entry.operations.map(operation => operation.method);
  assert.ok(methods.includes('GET'));
  assert.ok(methods.includes('PUT'));
});
