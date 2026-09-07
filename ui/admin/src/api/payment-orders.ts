import {
  isPaymentOrder,
  isPaymentOrderPage,
  isPaymentRefund,
  type CreatePaymentOrderRequest,
  type CreatePaymentRefundRequest,
  type PaymentOrder,
  type PaymentOrderListQuery,
  type PaymentOrderPage,
  type PaymentRefund
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: PaymentOrderListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId) {
    params.set('tenantId', query.tenantId);
  }
  if (query.tradeStateKey) {
    params.set('tradeStateKey', query.tradeStateKey);
  }
  if (query.outTradeNoContains) {
    params.set('outTradeNoContains', query.outTradeNoContains);
  }
  return params.toString();
}

export async function listPaymentOrders(
  query: PaymentOrderListQuery = {},
  signal?: AbortSignal
): Promise<PaymentOrderPage> {
  const value = await request<unknown>(
    `/api/v1/payments/orders?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentOrderPage(value)) {
    throw new Error('client.invalid_payment_order_page');
  }
  return value;
}

export async function getPaymentOrder(
  id: string,
  signal?: AbortSignal
): Promise<PaymentOrder> {
  const value = await request<unknown>(
    `/api/v1/payments/orders/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentOrder(value)) {
    throw new Error('client.invalid_payment_order');
  }
  return value;
}

export async function createPaymentOrder(
  body: CreatePaymentOrderRequest,
  signal?: AbortSignal
): Promise<PaymentOrder> {
  const value = await request<unknown>(
    '/api/v1/payments/orders',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isPaymentOrder(value)) {
    throw new Error('client.invalid_payment_order');
  }
  return value;
}

export async function reconcilePaymentOrder(
  id: string,
  signal?: AbortSignal
): Promise<PaymentOrder> {
  const value = await request<unknown>(
    `/api/v1/payments/orders/${encodeURIComponent(id)}/reconcile`,
    { method: 'POST' },
    signal
  );
  if (!isPaymentOrder(value)) {
    throw new Error('client.invalid_payment_order');
  }
  return value;
}

export async function createPaymentRefund(
  orderId: string,
  body: CreatePaymentRefundRequest,
  signal?: AbortSignal
): Promise<PaymentRefund> {
  const value = await request<unknown>(
    `/api/v1/payments/orders/${encodeURIComponent(orderId)}/refunds`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isPaymentRefund(value)) {
    throw new Error('client.invalid_payment_refund');
  }
  return value;
}
