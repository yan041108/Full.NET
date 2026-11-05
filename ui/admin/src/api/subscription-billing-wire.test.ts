import { beforeEach, expect, it, vi } from 'vitest';
import { checkoutSubscription, createPaidSubscription, getPackageOffer, savePackageOffer } from './subscription-billing';
const request = vi.hoisted(() => vi.fn());
vi.mock('./http', () => ({ http: { request } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf201';
const offer = { packageId: id, paymentTenantId: id, merchantConfigId: id, amountMinor: '9007199254740993', currency: 'CNY',
  seatLimit: '9223372036854775807', storageLimit: '9223372036854775806', durationDays: 30, entitlementIds: [], isActive: true, version: 1 };
beforeEach(() => vi.resetAllMocks());
it('生成操作读取完整字符串，保存正文不转换金额和限额', async () => {
  request.mockResolvedValue(offer); expect(await getPackageOffer(id)).toEqual(offer);
  const { packageId: _packageId, ...body } = offer;
  expect(await savePackageOffer(id, body)).toEqual(offer);
  const sent = JSON.parse(request.mock.lastCall![1].body);
  expect(sent.amountMinor).toBe('9007199254740993'); expect(sent.storageLimit).toBe('9223372036854775806');
});
it.each(['amountMinor', 'seatLimit', 'storageLimit'])('生成响应守卫拒绝数字字段 %s', async field => {
  request.mockResolvedValue({ ...offer, [field]: 100 }); await expect(getPackageOffer(id)).rejects.toThrow();
});
it('创建回执使用独立精确契约，原请求不附带可覆盖金额', async () => {
  request.mockResolvedValue({ subscriptionId: id, tenantId: id, paymentTenantId: id, paymentOperationId: id, merchantConfigId: id,
    packageId: id, offerVersion: 1, amountMinor: '9223372036854775807', currency: 'CNY', effectiveFromUtc: '2026-11-05T00:00:00Z',
    effectiveToUtc: '2026-12-05T00:00:00Z', subject: '原商品' });
  const body = { packageId: id, offerVersion: 1, effectiveFromUtc: '2026-11-05T00:00:00Z', idempotencyKey: id };
  expect((await createPaidSubscription(id, body)).amountMinor).toBe('9223372036854775807');
  expect(JSON.parse(request.mock.lastCall![1].body)).toEqual(body);
});
it('订阅下单的嵌套订单金额完整保留 Int64 字符串并拒绝数字回执', async () => {
  const reply = { subscriptionId: id, fulfillmentStateKey: 'Awaiting', errorCode: null, order: {
    id, tenantId: id, merchantConfigId: id, channelKey: 'wechat_native', outTradeNo: 'original', tradeStateKey: 'awaiting_payment',
    amountMinor: '9223372036854775807', currency: 'CNY', subject: 'Original', description: null, codeUrl: 'weixin://original',
    providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 1 } };
  request.mockResolvedValue(reply);
  expect((await checkoutSubscription(id, id)).order.amountMinor).toBe('9223372036854775807');
  request.mockResolvedValue({ ...reply, order: { ...reply.order, amountMinor: 29 } });
  await expect(checkoutSubscription(id, id)).rejects.toThrow();
});
