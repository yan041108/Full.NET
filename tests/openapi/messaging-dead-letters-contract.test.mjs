import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/messaging-dead-letters-v1.json'
);

test('消费死信 OpenAPI 夹具包含分页 GET', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const entry = contract.paths.find(
    item => item.path === '/api/v1/messaging/dead-letters'
  );
  assert.ok(entry);
  const getOperation = entry.operations.find(operation => operation.method === 'GET');
  assert.ok(getOperation);
  assert.equal(getOperation.permission, 'messaging.dead_letters.read');
  assert.ok(contract.schemas.MessagingDeadLetterPage);
});
