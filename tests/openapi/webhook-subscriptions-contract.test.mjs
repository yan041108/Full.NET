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
 assert.equal('signingSecret' in fixture.components.schemas.WebhookSubscriptionResponse.properties, false);
});
test('Webhook 实际 API、共享模型及页面调用点完整登记', async () => {
 const [contracts, calls] = await Promise.all([validateVueClientContractCoverage(), validateVueApiCallSiteCoverage()]);
 const relevant = value => /webhook-subscriptions|WebhookSubscriptionsView/.test(value);
 assert.deepEqual(contracts.filter(relevant), []); assert.deepEqual(calls.filter(relevant), []);
});
