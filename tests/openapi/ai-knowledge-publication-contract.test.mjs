import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const read = async name => JSON.parse(await readFile(new URL(`../../contracts/openapi/${name}`, import.meta.url), 'utf8'));
const ids = ['aiGetKnowledgePublicationHead', 'aiGetKnowledgePublication', 'aiRequestKnowledgePublication',
  'aiReviewKnowledgePublication', 'aiPublishKnowledgePublication', 'aiWithdrawKnowledgePublication',
  'aiListKnowledgeFolders', 'aiCreateKnowledgeFolder', 'aiUpdateKnowledgeFolder', 'aiDeleteKnowledgeFolder',
  'aiListKnowledgeDirectory', 'aiSetKnowledgeDirectoryBatch'];

test('知识治理完整生成十二个受保护操作，人工审核及目录权限不混用', async () => {
  const manifest = await read('client-generation-manifest-v1.json');
  const entries = manifest.entries.filter(entry => entry.generatedGroup === 'ai-knowledge-governance');
  assert.deepEqual(entries.map(entry => entry.operationId).sort(), [...ids].sort());
  const snapshot = await read('fullnet-client-v1.openapi.json');
  const operations = Object.values(snapshot.paths).flatMap(item => Object.values(item)).filter(item => ids.includes(item.operationId));
  assert.equal(operations.length, ids.length);
  for (const operation of operations) {
    assert.ok(operation.security.length > 0, operation.operationId);
    assert.ok(operation.responses['401'] && operation.responses['403'], operation.operationId);
    assert.ok(!manifest.publicOperationIds.includes(operation.operationId));
  }
  const directory = operations.find(item => item.operationId === 'aiListKnowledgeDirectory');
  assert.deepEqual(directory.parameters.map(item => item.name).sort(), ['folderId', 'knowledgeBaseId', 'page', 'pageSize', 'responsibleUserId', 'state', 'tag']);
});

test('发布请求冻结原身份、头版本与有效期，禁止客户端提交已批准状态或来源证明', async () => {
  const { schemas } = (await read('fullnet-client-v1.openapi.json')).components;
  const request = schemas.RequestAiKnowledgePublication;
  assert.deepEqual(Object.keys(request.properties).sort(), ['effectiveFromUtc', 'effectiveToUtc', 'expectedHeadVersion', 'requestKey', 'versionId']);
  assert.equal(request.additionalProperties, false);
  assert.equal(request.properties.requestKey.format, 'uuid');
  assert.equal(request.properties.effectiveToUtc.format, 'date-time');
  for (const name of ['ReviewAiKnowledgePublication', 'PublishAiKnowledgePublication']) {
    assert.equal(schemas[name].additionalProperties, false);
    assert.ok(schemas[name].required.includes('snapshotHash'));
    assert.ok(schemas[name].required.includes('version'));
  }
  assert.deepEqual(Object.keys(schemas.AiKnowledgePublicationHeadResponse.properties).sort(), ['documentId', 'publicationId', 'version']);
  const response = Object.keys(schemas.AiKnowledgePublicationResponse.properties);
  for (const secret of ['snapshotJson', 'receiptJson', 'securityStamp', 'content', 'apiKeyProtected']) assert.ok(!response.includes(secret));
  const item = schemas.SetAiKnowledgeDirectoryItem;
  assert.equal(item.additionalProperties, false);
  assert.ok(item.required.includes('requestKey') && item.required.includes('version'));
});
