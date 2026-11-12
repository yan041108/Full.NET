import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
test('签名轮换准备和明确完成分开，回执不暴露准备材料或操作者会话',()=>{
 const f=JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json','utf8')),root='/api/v1/webhooks/subscriptions/{subscriptionId}/signing-key-rotations';
 assert.equal(f.paths[root].post.operationId,'webhooksPrepareSigningKeyRotation');assert.equal(f.paths[root+'/pending'].get.operationId,'webhooksGetPendingSigningKeyRotation');assert.equal(f.paths[root+'/{operationId}'].get.operationId,'webhooksGetSigningKeyRotation');
 for(const action of ['activate','cancel'])assert.ok(f.paths[root+'/{operationId}/'+action].post.responses['503']);
 assert.equal(f.components.schemas.PrepareWebhookSigningKeyRotationRequest.properties.signingSecret.writeOnly,true);assert.equal(f.components.schemas.PrepareWebhookSigningKeyRotationRequest.properties.signingSecret.maxLength,128);
 const properties=f.components.schemas.WebhookSigningKeyRotationReceipt.properties;for(const field of ['signingSecret','protectedSecret','protectedFingerprint','actorSessionId','completedBySessionId'])assert.equal(field in properties,false);
 assert.deepEqual(properties.stateKey.enum,['prepared','activated','cancelled']);
});
import { validateVueClientContractCoverage } from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';
import { validateVueApiCallSiteCoverage } from '../../scripts/openapi/validate-vue-api-call-site-coverage.mjs';
test('Webhook 状态契约保留版本、独立动作和不回显密钥', () => {
 const fixture = JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json', 'utf8'));
 const operation = fixture.paths['/api/v1/webhooks/subscriptions/{subscriptionId}/status'].post;
 assert.equal(operation.operationId, 'webhooksSetSubscriptionStatus'); assert.deepEqual(Object.keys(operation.responses).sort(), ['200','400','401','403','404','409']);
 assert.deepEqual(fixture.components.schemas.SetWebhookSubscriptionStatusRequest.required, ['isActive','version']);
 assert.equal(fixture.components.schemas.CreateWebhookSubscriptionRequest.properties.signingSecret.writeOnly, true);
 assert.equal(fixture.components.schemas.CreateWebhookSubscriptionRequest.properties.signingSecret.maxLength, 128);
 assert.equal(fixture.components.schemas.CreateWebhookSubscriptionRequest.properties.eventType.maxLength, 128);
 assert.equal('signingSecret' in fixture.components.schemas.WebhookSubscriptionResponse.properties, false);
 assert.ok(fixture.components.schemas.WebhookSubscriptionResponse.required.includes('signingKeyId'));
 assert.equal(fixture.components.schemas.WebhookSubscriptionResponse.properties.signingKeyId.format, 'uuid');
 assert.equal(fixture.components.schemas.WebhookSubscriptionResponse.properties.signingKeyId.readOnly, true);
});
test('Webhook 实际 API、共享模型及页面调用点完整登记', async () => {
 const [contracts, calls] = await Promise.all([validateVueClientContractCoverage(), validateVueApiCallSiteCoverage()]);
 const relevant = value => /webhook-subscriptions|WebhookSubscriptionsView|WebhookDelivery/.test(value);
 assert.deepEqual(contracts.filter(relevant), []); assert.deepEqual(calls.filter(relevant), []);
});

test('逐次记录公开确认状态而不暴露内部租约、正文或原始错误', () => {
 const fixture=JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json','utf8'));
 const op=fixture.paths['/api/v1/webhooks/subscriptions/{subscriptionId}/deliveries/{deliveryId}/attempts'].get;
 assert.equal(op.operationId,'webhooksListDeliveryAttempts'); assert.equal(op.parameters.find(p=>p.name==='pageSize').schema.maximum,100);
 const properties=fixture.components.schemas.WebhookDeliveryAttemptResponse.properties;
 assert.deepEqual(properties.outcomeKey.enum,['processing','unconfirmed','delivered','failed']);
 assert.deepEqual(properties.appliedToDelivery.type,['boolean','null']);
 for(const field of ['claimVersion','payloadBody','signingSecret','lastError'])assert.equal(field in properties,false);
});
test('投递历史契约分页有界，仅公开安全失败类别及当前计数', () => {
 const fixture=JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json','utf8'));
 const operation=fixture.paths['/api/v1/webhooks/subscriptions/{subscriptionId}/deliveries'].get;
 assert.equal(operation.operationId,'webhooksListSubscriptionDeliveries');assert.equal(operation.parameters.find(value=>value.name==='pageSize').schema.maximum,100);
 const properties=fixture.components.schemas.WebhookDeliveryHistoryResponse.properties;
 for(const key of ['payloadBody','signingSecret','signingSecretHash','lastError'])assert.equal(key in properties,false);
 assert.equal('lastErrorCode' in properties,true);assert.equal('attemptCount' in properties,true);
});

test('受控重放固定操作和原修订，回执不包含正文或操作者会话', () => {
 const f=JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json','utf8'));
 const path='/api/v1/webhooks/subscriptions/{subscriptionId}/deliveries/{deliveryId}/replays';
 assert.equal(f.paths[path].post.operationId,'webhooksReplayDelivery');assert.equal(f.paths[path+'/{operationId}'].get.operationId,'webhooksGetDeliveryReplayReceipt');
 assert.deepEqual(f.components.schemas.ReplayWebhookDeliveryRequest.required,['operationId','version']); assert.equal(f.components.schemas.ReplayWebhookDeliveryRequest.properties.version.maximum,2147483644);
 assert.ok('409' in f.paths[path].post.responses);assert.ok('503' in f.paths[path].post.responses);
 const fields=f.components.schemas.WebhookDeliveryReplayReceipt.properties;
 for(const key of ['payloadBody','payloadDigest','signingSecret','actorSessionId'])assert.equal(key in fields,false);
 assert.ok('previousVersion' in fields);assert.ok('queuedAtUtc' in fields);
});

test('恢复确认绑定原事件和两个摘要，仅公开对账回执', () => {
  const fixture = JSON.parse(fs.readFileSync('contracts/openapi/webhook-subscriptions-v1.json', 'utf8'));
  const path = '/api/v1/webhooks/subscriptions/{subscriptionId}/deliveries/{deliveryId}/reconciliations';
  const operation = fixture.paths[path].post;
  assert.equal(operation.operationId, 'webhooksConfirmRecoveredDelivery');
  assert.deepEqual(Object.keys(operation.responses), ['200', '400', '401', '403', '404', '409', '503']);
  const request = fixture.components.schemas.ConfirmWebhookDeliveryRequest;
  assert.deepEqual(request.required, ['operationId', 'version', 'eventId', 'payloadSha256', 'receiverReceiptSha256']);
  const receipt = fixture.components.schemas.WebhookDeliveryRecoveryReceipt;
  for (const field of ['payloadSha256', 'receiverReceiptSha256']) {
    const pattern = new RegExp(request.properties[field].pattern);
    assert.ok(pattern.test('Aa'.repeat(32)));
    assert.ok(!pattern.test('g'.repeat(64)));
    assert.ok(receipt.required.includes(field));
  }
  for (const field of ['payloadBody', 'signingSecret', 'actorUserId', 'actorSessionId'])
    assert.ok(!(field in receipt.properties));
});
