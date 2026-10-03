import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const operationIds = [
  'aiGetKnowledgeMembers', 'aiSetKnowledgeMembers', 'aiListKnowledgeBases', 'aiGetKnowledgeBase',
  'aiCreateKnowledgeBase', 'aiUpdateKnowledgeBase', 'aiUpdateKnowledgePolicy',
  'aiListKnowledgeDocuments', 'aiGetKnowledgeDocument', 'aiCreateKnowledgeDocument',
  'aiUpdateKnowledgeDocument', 'aiDeleteKnowledgeDocument', 'aiGetKnowledgeDocumentMembers',
  'aiSetKnowledgeDocumentMembers', 'aiUploadKnowledgeVersion', 'aiListKnowledgeVersions',
  'aiDownloadKnowledgeSource', 'aiGetKnowledgeImport', 'aiRetryKnowledgeImport', 'aiCleanupKnowledgeImports',
  'aiListKnowledgeRoleCandidates', 'aiListKnowledgeDocumentRoleCandidates',
  'aiGetKnowledgeRoles', 'aiSetKnowledgeRoles', 'aiGetKnowledgeDocumentRoles', 'aiSetKnowledgeDocumentRoles'
];

test('知识库 26 个受保护 Operation 从标准快照进入共享生成客户端', async () => {
  const manifest = JSON.parse(await readFile(new URL('../../contracts/openapi/client-generation-manifest-v1.json', import.meta.url), 'utf8'));
  const entries = manifest.entries.filter(entry => entry.generatedGroup === 'ai-knowledge');
  assert.deepEqual(entries.map(entry => entry.operationId).sort(), [...operationIds].sort());
  assert.ok(entries.every(entry => entry.apiModule === 'packages/client-contracts/src/ai-knowledge.ts' && entry.status === 'generated'));
  const snapshot = JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
  const operations = new Map(Object.values(snapshot.paths).flatMap(item => Object.values(item))
    .filter(item => operationIds.includes(item.operationId)).map(item => [item.operationId, item]));
  assert.equal(operations.size, 26);
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
  for (const id of ['aiListKnowledgeRoleCandidates', 'aiListKnowledgeDocumentRoleCandidates']) {
    assert.ok(operations.get(id).responses['200'].content['application/json']);
    assert.ok(operations.get(id).responses['400'] && operations.get(id).responses['404']);
    assert.equal(operations.get(id).requestBody, undefined);
  }
  for (const name of ['AiKnowledgeVersionResponse', 'AiKnowledgeImportResponse']) {
    const properties = snapshot.components.schemas[name].properties;
    assert.ok(!('fileId' in properties) && !('storagePath' in properties) && !('tenantId' in properties));
  }
  for (const id of ['aiGetKnowledgeRoles', 'aiSetKnowledgeRoles', 'aiGetKnowledgeDocumentRoles', 'aiSetKnowledgeDocumentRoles']) {
    const operation = operations.get(id);
    assert.deepEqual(operation.tags, ['AiKnowledgeRoleGrants']);
    assert.equal(operation.responses['200'].content['application/json'].schema.$ref,
      '#/components/schemas/AiKnowledgeRoleGrantsResponse');
    for (const status of ['400', '404', '409', '422']) assert.ok(operation.responses[status]);
    assert.ok(operation.parameters.every(parameter => parameter.in === 'path' && parameter.required));
    if (id.startsWith('aiSet')) {
      assert.equal(operation.requestBody.required, true);
      assert.equal(operation.requestBody.content['application/json'].schema.$ref,
        '#/components/schemas/SetAiKnowledgeRoleGrantsRequest');
    } else assert.equal(operation.requestBody, undefined);
  }
  const request = snapshot.components.schemas.SetAiKnowledgeRoleGrantsRequest;
  const response = snapshot.components.schemas.AiKnowledgeRoleGrantsResponse;
  assert.deepEqual(Object.keys(request.properties).sort(), ['roleIds', 'version']);
  assert.deepEqual(Object.keys(response.properties).sort(), ['resourceId', 'roleIds', 'version']);
  for (const schema of [request, response]) {
    assert.equal(schema.properties.roleIds.type, 'array');
    assert.equal(schema.properties.roleIds.items.format, 'uuid');
    assert.equal(schema.properties.version.format, 'int32');
    assert.deepEqual([...schema.required].sort(), Object.keys(schema.properties).sort());
  }
  assert.equal(response.properties.resourceId.format, 'uuid');
  assert.equal(request.additionalProperties, false);
});
