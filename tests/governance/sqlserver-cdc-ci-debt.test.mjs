import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

test('sqlserver-cdc-ci-debt 文档与测试/工作流保持一致', async () => {
  const debtDoc = await read('docs/verification/sqlserver-cdc-ci-debt.md');
  const support = await read(
    'tests/Full.NET.IntegrationTests/Messaging/SqlServerCdcTestSupport.cs'
  );
  const workflow = await read('.github/workflows/sqlserver-cdc-nightly.yml');

  for (const token of [
    'SqlServerCdcDebeziumInboxE2ETests',
    'SqlServerCdcShadowTests',
    'KafkaOutboxCdcCapacityRunnerTests',
    'SQL Server Agent',
    'Inconclusive',
    'FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING',
    'messaging-heavy'
  ]) {
    assert.match(debtDoc, new RegExp(token.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }

  assert.match(
    support,
    /FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING/
  );

  assert.match(workflow, /FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING/);
  assert.match(workflow, /SqlServerCdc/);
});
