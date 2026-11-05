import { beforeEach, expect, it, vi } from 'vitest';
import { getPaymentOrderCreationRequest } from './payment-orders';
const request = vi.hoisted(() => vi.fn());
vi.mock('./http', () => ({ http: { request }, request }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf201';
const snapshot = { idempotencyKey: id, readAtUtc: '2026-11-05T01:00:00Z', order: {
  id, tenantId: id, merchantConfigId: id, channelKey: 'wechat_native', outTradeNo: 'original', tradeStateKey: 'provider_unknown',
  amountMinor: '9223372036854775807', currency: 'CNY', subject: 'Original', description: null, codeUrl: null,
  providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 7 } };
beforeEach(() => vi.resetAllMocks());
it('生成查询显式绑定租户和原请求键，仅 GET 并透传取消信号', async () => {
  request.mockResolvedValue(snapshot); const signal = new AbortController().signal;
  expect(await getPaymentOrderCreationRequest(id, id, signal)).toEqual(snapshot);
  expect(request).toHaveBeenCalledExactlyOnceWith(`/api/v1/payments/orders/creation-requests/${id}?tenantId=${id}`, { method: 'GET' }, signal);
});
it('嵌套订单数值金额不接入核对结果', async () => {
  request.mockResolvedValue({ ...snapshot, order: { ...snapshot.order, amountMinor: 29 } });
  await expect(getPaymentOrderCreationRequest(id, id)).rejects.toThrow();
});
