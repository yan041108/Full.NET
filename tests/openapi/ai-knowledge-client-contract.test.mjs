import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const operationIds = [
  'aiGetKnowledgeMembers', 'aiSetKnowledgeMembers', 'aiListKnowledgeBases', 'aiGetKnowledgeBase',
  'aiCreateKnowledgeBase', 'aiUpdateKnowledgeBase', 'aiUpdateKnowledgePolicy',
  'aiListKnowledgeDocuments', 'aiGetKnowledgeDocument', 'aiCreateKnowledgeDocument',
  'aiUpdateKnowledgeDocument', 'aiDeleteKnowledgeDocument', 'aiGetKnowledgeDocumentMembers',
  'aiSetKnowledgeDocumentMembers', 'aiUploadKnowledgeVersion', 'aiListKnowledgeVersions',
  'aiDownloadKnowledgeSource', 'aiGetKnowledgeImport', 'aiRetryKnowledgeImport', 'aiCleanupKnowledgeImports'
];

test('知识库 20 个受保护 Operation 从标准快照进入共享生成客户端', async () => {
  const manifest = JSON.parse(await readFile(new URL('../../contracts/openapi/client-generation-manifest-v1.json', import.meta.url), 'utf8'));
  const entries = manifest.entries.filter(entry => entry.generatedGroup === 'ai-knowledge');
  assert.deepEqual(entries.map(entry => entry.operationId).sort(), [...operationIds].sort());
  assert.ok(entries.every(entry => entry.apiModule === 'packages/client-contracts/src/ai-knowledge.ts' && entry.status === 'generated'));
  const snapshot = JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
  const operations = new Map(Object.values(snapshot.paths).flatMap(item => Object.values(item))
    .filter(item => operationIds.includes(item.operationId)).map(item => [item.operationId, item]));
  assert.equal(operations.size, 20);
  for (const id of operationIds) {
    const operation = operations.get(id);
    assert.equal(operation.tags.length, 1);
    assert.ok(operation.security.length > 0);
    assert.ok(operation.responses['401'] && operation.responses['403']);
    assert.ok(!manifest.publicOperationIds.includes(id));
  }
  const upload = operations.get('aiUploadKnowledgeVersion');
  assert.equal(upload.requestBody.required, true);
  assert.deepEqual(Object.keys(upload.requestBody.content).sort(), ['application/pdf', 'text/markdown', 'text/plain']);
  assert.deepEqual(upload.parameters.map(item => item.name).sort(), ['documentId', 'fileName', 'knowledgeBaseId', 'requestKey', 'version']);
  assert.ok(upload.responses['202'].content['application/json']);
  assert.ok(operations.get('aiDownloadKnowledgeSource').responses['200'].content['application/octet-stream']);
  assert.equal(operations.get('aiDeleteKnowledgeDocument').responses['204'].content, undefined);
  for (const name of ['AiKnowledgeVersionResponse', 'AiKnowledgeImportResponse']) {
    const properties = snapshot.components.schemas[name].properties;
    assert.ok(!('fileId' in properties) && !('storagePath' in properties) && !('tenantId' in properties));
  }
});
