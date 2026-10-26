import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const load = async () => JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));

test('消息控制面使用稳定操作身份与明确的流或代次参数', async () => {
  const document = await load();
  const root = '/api/v1/messaging/delivery/';
  for (const [route, method, operationId] of [
    ['cutover-evidence', 'post', 'messagingVerifyCutoverEvidence'],
    ['rollback-operations', 'get', 'messagingGetLatestRollbackOperation'],
    ['rollback-operations/{generation}', 'get', 'messagingGetRollbackOperation'],
    ['rollback-operations/{generation}/recover', 'post', 'messagingRecoverRollbackOperation']
  ]) {
    const operation = document.paths[root + route]?.[method];
    assert.equal(operation?.operationId, operationId);
    assert.ok(operation.responses['200']);
    assert.ok(operation.security?.length);
  }
  const latest = document.paths[root + 'rollback-operations'].get.parameters;
  assert.ok(latest.some(item => item.name === 'eventType' && item.required));
  assert.ok(latest.some(item => item.name === 'schemaVersion' && item.required));
});

test('证明与恢复请求必填理由且不接收身份或就绪声明，查询不泄露内部资源', async () => {
  const schemas = (await load()).components.schemas;
  for (const name of ['VerifyCutoverRequest', 'RecoverRollbackRequest']) {
    assert.ok(schemas[name].required.includes('reason'));
    assert.ok(!Object.keys(schemas[name].properties).some(key => /actor|userId|ready|fingerprint|resources|payload/i.test(key)));
  }
  const response = schemas.RollbackOperationResponse;
  for (const key of ['generation', 'eventType', 'schemaVersion', 'phase']) assert.ok(response.properties[key]);
  assert.ok(!Object.keys(response.properties).some(key => /resources|config|payload|credential/i.test(key)));
});

test('持久重放和影子采集保留固定操作身份与正文边界', async () => {
  const document = await load();
  const operations = Object.entries(document.paths).flatMap(([path, item]) => Object.entries(item).filter(([method]) => ['get', 'post'].includes(method)).map(([method, operation]) => ({ path, method, ...operation })));
  for (const id of ['messagingCreateReplayOperation', 'messagingListReplayOperations', 'messagingGetReplayOperation', 'messagingExecuteReplayOperation', 'messagingCancelReplayOperation',
    'messagingGetDeliveryOverview', 'messagingCutoverDeliveryOwner', 'messagingRollbackDeliveryOwner', 'messagingGetCurrentShadowCapture', 'messagingStartShadowCapture', 'messagingCloseShadowCapture']) {
    const operation = operations.find(item => item.operationId === id);
    assert.ok(operation, id); assert.ok(operation.security?.length, id);
    assert.ok(operation.responses[id === 'messagingCreateReplayOperation' ? '201' : '200'], id);
  }
  const schemas = document.components.schemas;
  for (const name of ['StartShadowCaptureRequest', 'CloseShadowCaptureRequest', 'CreateKafkaReplayOperationRequest', 'ChangeKafkaReplayOperationRequest']) {
    assert.ok(schemas[name].required.includes('reason'), name);
    assert.ok(!Object.keys(schemas[name].properties).some(key => /actor|userId|ready|fingerprint|resources|payload/i.test(key)), name);
  }
  for (const key of ['requestId', 'expectedGeneration', 'maxEvents', 'windowSeconds']) assert.ok(schemas.StartShadowCaptureRequest.properties[key]);
  for (const key of ['generation', 'capturedCount', 'capturedBytes', 'missingMetadataCount']) assert.ok(schemas.ShadowCaptureResponse.properties[key]);
  assert.ok(schemas.DeliveryStatusResponse.properties.shadowCapturePolicy);
});
