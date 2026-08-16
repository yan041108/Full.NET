import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/identity-me-v1.json');

test('identity-me-v1 OpenAPI 夹具包含 GET /api/v1/me', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  assert.ok(contract.paths['/api/v1/me']);
  assert.ok(contract.paths['/api/v1/me'].get);
});
