import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/identity-my-tenant-invitations-v1.json'
);

test('我的租户邀请 OpenAPI 夹具覆盖列表与接受路径', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const meEntry = contract.paths.find(
    item => item.path === '/api/v1/me/tenant-invitations'
  );
  assert.ok(meEntry);
  assert.ok(meEntry.operations.some(operation => operation.method === 'GET'));
  const tokenAccept = contract.paths.find(
    item => item.path === '/api/v1/identity/tenant-invitations/accept'
  );
  assert.ok(tokenAccept);
  assert.ok(tokenAccept.operations.some(operation => operation.method === 'POST'));
});
