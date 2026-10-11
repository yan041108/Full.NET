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

test('版本凭据追加到既有字段之后，保留四个原有字段', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  for (const schema of Object.values(contract.schemas)) {
    assert.deepEqual(schema.properties, ['minimumRetainedVersionsPerItem', 'maximumRetainedHistoryVersions', 'pollSeconds', 'batchSize', 'version']);
  }
});

test('标准快照要求响应版本，旧请求可省略初始化凭据，并公开409', async () => {
  const document = JSON.parse(await readFile(path.join(repositoryRoot, 'contracts/openapi/fullnet-client-v1.openapi.json'), 'utf8'));
  const response = document.components.schemas.HostDocumentVersionRetentionSettingsResponse;
  const request = document.components.schemas.UpdateHostDocumentVersionRetentionRequest;
  assert.equal(response.properties.version?.format, 'int64');
  assert.ok(response.required.includes('version'));
  assert.equal(request.properties.version?.format, 'int64');
  assert.ok(!request.required.includes('version'));
  assert.ok(document.paths['/api/v1/document/host/version-retention'].put.responses['409']);
});
