import { beforeEach, expect, it, vi } from 'vitest';
import { createPaidSubscription, getPackageOffer, savePackageOffer } from './subscription-billing';
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
