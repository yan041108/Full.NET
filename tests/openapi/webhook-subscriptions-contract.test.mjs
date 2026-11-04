import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
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
