import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { validateVueClientContractCoverage } from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';

test('付费订阅页面通过共享契约、生成操作和实际消费绑定校验', async () => {
  const violations = await validateVueClientContractCoverage();
  assert.deepEqual(violations.filter(value => value.includes('subscription-billing')), []);
});

test('HTTP 付款输入不接收金额、商户或成功标记', async () => {
  const document = JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
  const create = document.components.schemas.CreatePaidTenantSubscriptionRequest;
  assert.deepEqual(Object.keys(create.properties).sort(), ['effectiveFromUtc', 'idempotencyKey', 'offerVersion', 'packageId']);
  const checkout = document.paths['/api/v1/payments/subscriptions/{tenantId}/{subscriptionId}/checkout'].post;
  assert.equal(checkout.requestBody, undefined);
  assert.ok(checkout.security.length > 0);
});
