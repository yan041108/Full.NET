import {
  isPaymentRefund,
  isPaymentRefundPage,
  type CreatePaymentRefundRequest,
  type PaymentRefund,
  type PaymentRefundListQuery,
  type PaymentRefundPage
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: PaymentRefundListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId) {
    params.set('tenantId', query.tenantId);
  }
  if (query.orderId) {
    params.set('orderId', query.orderId);
  }
  if (query.refundStateKey) {
    params.set('refundStateKey', query.refundStateKey);
  }
  return params.toString();
}

export async function listPaymentRefunds(
  query: PaymentRefundListQuery = {},
  signal?: AbortSignal
): Promise<PaymentRefundPage> {
  const value = await request<unknown>(
    `/api/v1/payments/refunds?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentRefundPage(value)) {
    throw new Error('client.invalid_payment_refund_page');
  }
  return value;
}

export async function getPaymentRefund(
  id: string,
  signal?: AbortSignal
): Promise<PaymentRefund> {
  const value = await request<unknown>(
    `/api/v1/payments/refunds/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentRefund(value)) {
    throw new Error('client.invalid_payment_refund');
  }
  return value;
}

export type { CreatePaymentRefundRequest, PaymentRefund, PaymentRefundListQuery, PaymentRefundPage };
