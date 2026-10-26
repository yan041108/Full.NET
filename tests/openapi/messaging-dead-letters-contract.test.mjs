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

test('双来源死信的读取和恢复权限独立，恢复必须提供理由', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  for (const [source, action, permission] of [
    ['outbox', 'requeue', 'messaging.outbox_dead_letters.requeue'],
    ['kafka', 'replay', 'messaging.dead_letters.replay']
  ]) {
    const root = `/api/v1/messaging/${source}/dead-letters`;
    for (const route of [root, `${root}/{id}`]) {
      const operation = contract.paths.find(item => item.path === route)?.operations.find(item => item.method === 'GET');
      assert.equal(operation?.permission, 'messaging.dead_letters.read');
    }
    const recovery = contract.paths.find(item => item.path === `${root}/{id}/${action}`)?.operations.find(item => item.method === 'POST');
    assert.equal(recovery?.permission, permission);
    assert.deepEqual(recovery?.requiredBody, ['reason']);
  }
  assert.equal(contract.schemas.OperationalDeadLetter.offsetEncoding, 'decimal-string');
  assert.ok(!contract.schemas.OperationalDeadLetter.properties.some(name => /payload|snapshot|lastError$/i.test(name)));
});
