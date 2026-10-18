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
  'aiGetKnowledgeIndexStatus', 'aiGetKnowledgeIndexReconciliation', 'aiEnqueueKnowledgeIndex', 'aiRetryKnowledgeIndex', 'aiRebuildKnowledgeIndex',
  'aiRecoverKnowledgeIndexChanges', 'aiListKnowledgeIndexChanges',
  'aiListKnowledgeRoleCandidates', 'aiListKnowledgeDocumentRoleCandidates',
  'aiListKnowledgeOrganizationCandidates', 'aiListKnowledgeDocumentOrganizationCandidates',
  'aiGetKnowledgeRoles', 'aiSetKnowledgeRoles', 'aiGetKnowledgeDocumentRoles', 'aiSetKnowledgeDocumentRoles',
  'aiGetKnowledgeOrganizations', 'aiSetKnowledgeOrganizations', 'aiGetKnowledgeDocumentOrganizations', 'aiSetKnowledgeDocumentOrganizations'
];

test('知识库 39 个受保护 Operation 从标准快照进入共享生成客户端', async () => {
  const manifest = JSON.parse(await readFile(new URL('../../contracts/openapi/client-generation-manifest-v1.json', import.meta.url), 'utf8'));
  const entries = manifest.entries.filter(entry => entry.generatedGroup === 'ai-knowledge');
  assert.deepEqual(entries.map(entry => entry.operationId).sort(), [...operationIds].sort());
  assert.ok(entries.every(entry => entry.apiModule === 'packages/client-contracts/src/ai-knowledge.ts' && entry.status === 'generated'));
  const snapshot = JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
  const operations = new Map(Object.values(snapshot.paths).flatMap(item => Object.values(item))
    .filter(item => operationIds.includes(item.operationId)).map(item => [item.operationId, item]));
  assert.equal(operations.size, 39);
  for (const id of operationIds) {
    const operation = operations.get(id);
    assert.equal(operation.tags.length, 1);
    assert.ok(operation.security.length > 0);
    assert.ok(operation.responses['401'] && operation.responses['403']);
    assert.ok(!manifest.publicOperationIds.includes(id));
  }
  const rebuild = operations.get('aiRebuildKnowledgeIndex');
  assert.deepEqual(rebuild.tags, ['AiKnowledgeIndexing']);
  assert.equal(rebuild.requestBody.required, true);
  assert.equal(rebuild.requestBody.content['application/json'].schema.$ref, '#/components/schemas/AiKnowledgeIndexRebuildRequest');
  const rebuildSchema = snapshot.components.schemas.AiKnowledgeIndexRebuildRequest;
  assert.equal(rebuildSchema.additionalProperties, false);
  assert.deepEqual([...rebuildSchema.required].sort(), ['clientRequestId', 'confirmAdditionalCost', 'expectedDocumentVersion', 'expectedKnowledgeBaseVersion', 'expectedTaskId']);
  assert.equal(rebuild.responses['202'].content['application/json'].schema.$ref, '#/components/schemas/AiKnowledgeIndexEnqueueResponse');
  const upload = operations.get('aiUploadKnowledgeVersion');
  assert.equal(upload.requestBody.required, true);
  assert.deepEqual(Object.keys(upload.requestBody.content).sort(), ['application/pdf',
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 'text/markdown', 'text/plain']);
  const citation = snapshot.components.schemas.AiKnowledgeCitationResponse;
  assert.ok(!citation.required.includes('docxLocation'));
  assert.deepEqual(citation.properties.docxLocation.oneOf, [{ type: 'null' },
    { $ref: '#/components/schemas/AiKnowledgeDocxLocationResponse' }]);
  assert.deepEqual(upload.parameters.map(item => item.name).sort(), ['documentId', 'fileName', 'knowledgeBaseId', 'requestKey', 'version']);
  assert.ok(upload.responses['202'].content['application/json']);
  assert.ok(operations.get('aiDownloadKnowledgeSource').responses['200'].content['application/octet-stream']);
  assert.equal(operations.get('aiDeleteKnowledgeDocument').responses['204'].content, undefined);
  for (const id of ['aiGetKnowledgeIndexStatus', 'aiGetKnowledgeIndexReconciliation', 'aiEnqueueKnowledgeIndex', 'aiRetryKnowledgeIndex']) {
    const operation = operations.get(id);
    assert.deepEqual(operation.tags, ['AiKnowledgeIndexing']);
    assert.equal(operation.requestBody, undefined);
    const expected = ['knowledgeBaseId', 'documentId', 'versionId', ...(id === 'aiRetryKnowledgeIndex' ? ['taskId'] : [])];
    assert.deepEqual(operation.parameters.map(parameter => parameter.name).sort(), expected.sort());
    assert.ok(operation.parameters.every(parameter => parameter.in === 'path' && parameter.required && parameter.schema.format === 'uuid'));
    const read = id.startsWith('aiGet');
    const response = id === 'aiGetKnowledgeIndexReconciliation' ? 'AiKnowledgeIndexReconciliationResponse'
      : id === 'aiGetKnowledgeIndexStatus' ? 'AiKnowledgeIndexStatusResponse' : 'AiKnowledgeIndexEnqueueResponse';
    const success = read ? '200' : '202';
    assert.equal(operation.responses[success].content['application/json'].schema.$ref,
      `#/components/schemas/${response}`);
    for (const status of ['400', '404', '409']) assert.ok(operation.responses[status]);
  }
  assert.deepEqual(Object.keys(snapshot.components.schemas.AiKnowledgeIndexStatusResponse.properties).sort(), ['failureCode', 'state', 'taskId', 'versionId']);
  assert.deepEqual(Object.keys(snapshot.components.schemas.AiKnowledgeIndexReconciliationResponse.properties).sort(),
    ['documentVersion', 'knowledgeBaseVersion', 'reason', 'recoveryAction', 'state', 'taskId', 'versionId']);
  assert.deepEqual(Object.keys(snapshot.components.schemas.AiKnowledgeIndexEnqueueResponse.properties).sort(), ['taskId', 'versionId']);
  for (const id of ['aiListKnowledgeRoleCandidates', 'aiListKnowledgeDocumentRoleCandidates', 'aiListKnowledgeOrganizationCandidates', 'aiListKnowledgeDocumentOrganizationCandidates']) {
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
  for (const id of ['aiGetKnowledgeOrganizations', 'aiSetKnowledgeOrganizations', 'aiGetKnowledgeDocumentOrganizations', 'aiSetKnowledgeDocumentOrganizations']) {
    const operation = operations.get(id);
    assert.deepEqual(operation.tags, ['AiKnowledgeOrganizationGrants']);
    assert.equal(operation.responses['200'].content['application/json'].schema.$ref,
      '#/components/schemas/AiKnowledgeOrganizationGrantsResponse');
    for (const status of ['400', '404', '409', '422']) assert.ok(operation.responses[status]);
    assert.ok(operation.parameters.every(parameter => parameter.in === 'path' && parameter.required));
    if (id.startsWith('aiSet')) {
      assert.equal(operation.requestBody.required, true);
      assert.equal(operation.requestBody.content['application/json'].schema.$ref,
        '#/components/schemas/SetAiKnowledgeOrganizationGrantsRequest');
    } else assert.equal(operation.requestBody, undefined);
  }
  const organizationRequest = snapshot.components.schemas.SetAiKnowledgeOrganizationGrantsRequest;
  const organizationResponse = snapshot.components.schemas.AiKnowledgeOrganizationGrantsResponse;
  assert.deepEqual(Object.keys(organizationRequest.properties).sort(), ['organizationUnitIds', 'version']);
  assert.deepEqual(Object.keys(organizationResponse.properties).sort(), ['organizationUnitIds', 'resourceId', 'version']);
  for (const schema of [organizationRequest, organizationResponse]) {
    assert.equal(schema.properties.organizationUnitIds.type, 'array');
    assert.equal(schema.properties.organizationUnitIds.items.format, 'uuid');
    assert.equal(schema.properties.version.format, 'int32');
    assert.deepEqual([...schema.required].sort(), Object.keys(schema.properties).sort());
  }
  assert.equal(organizationResponse.properties.resourceId.format, 'uuid');
  assert.equal(organizationRequest.additionalProperties, false);
});
