import { describe, expect, it } from 'vitest';
import { isPaymentOrder, isPaymentOrderListItem, isPaymentOrderPage, isPaymentRefund, isPaymentRefundListItem, isPaymentRefundPage } from '../src/payments';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf201';
const order = { id, tenantId: id, merchantConfigId: id, outTradeNo: 'original', tradeStateKey: 'succeeded', amountMinor: '29' };
const refund = { id, tenantId: id, orderId: id, outRefundNo: 'original-refund', refundStateKey: 'processing', amountMinor: '29' };
describe('支付金额严格字符串线格式', () => {
  it.each(['29', '9007199254740993', '9223372036854775807'])('完整整数 %s 保持原文本', amountMinor => {
    const payment = { ...order, amountMinor }; const returned = { ...refund, amountMinor };
    expect(isPaymentOrder(payment)).toBe(true); expect(isPaymentOrderListItem(payment)).toBe(true);
    expect(isPaymentRefund(returned)).toBe(true); expect(isPaymentRefundListItem(returned)).toBe(true);
    expect(payment.amountMinor).toBe(amountMinor); expect(returned.amountMinor).toBe(amountMinor);
  });
  it.each([29, Number.MAX_SAFE_INTEGER + 1, null, '0', '-1', '01', '1e3', '1.0', '9223372036854775808'])('错误金额 %s 拒绝整页与详情', amountMinor => {
    const payment = { ...order, amountMinor }; const returned = { ...refund, amountMinor };
    expect(isPaymentOrder(payment)).toBe(false); expect(isPaymentOrderListItem(payment)).toBe(false);
    expect(isPaymentRefund(returned)).toBe(false); expect(isPaymentRefundListItem(returned)).toBe(false);
    expect(isPaymentOrderPage({ items: [payment], page: 1, pageSize: 20, total: 1 })).toBe(false);
    expect(isPaymentRefundPage({ items: [returned], page: 1, pageSize: 20, total: 1 })).toBe(false);
  });
});
