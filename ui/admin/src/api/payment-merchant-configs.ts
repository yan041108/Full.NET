import {
  isPaymentMerchantConfig,
  isPaymentMerchantConfigPage,
  type CreatePaymentMerchantConfigRequest,
  type PaymentMerchantConfig,
  type PaymentMerchantConfigListQuery,
  type PaymentMerchantConfigPage,
  type UpdatePaymentMerchantConfigRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: PaymentMerchantConfigListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId) {
    params.set('tenantId', query.tenantId);
  }
  if (query.channelKey) {
    params.set('channelKey', query.channelKey);
  }
  if (query.nameContains) {
    params.set('nameContains', query.nameContains);
  }
  if (query.isEnabled !== undefined) {
    params.set('isEnabled', String(query.isEnabled));
  }
  return params.toString();
}

export async function listPaymentMerchantConfigs(
  query: PaymentMerchantConfigListQuery = {},
  signal?: AbortSignal
): Promise<PaymentMerchantConfigPage> {
  const value = await request<unknown>(
    `/api/v1/payments/merchant-configs?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentMerchantConfigPage(value)) {
    throw new Error('client.invalid_payment_merchant_config_page');
  }
  return value;
}

export async function getPaymentMerchantConfig(
  id: string,
  signal?: AbortSignal
): Promise<PaymentMerchantConfig> {
  const value = await request<unknown>(
    `/api/v1/payments/merchant-configs/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isPaymentMerchantConfig(value)) {
    throw new Error('client.invalid_payment_merchant_config');
  }
  return value;
}

export async function createPaymentMerchantConfig(
  body: CreatePaymentMerchantConfigRequest,
  signal?: AbortSignal
): Promise<PaymentMerchantConfig> {
  const value = await request<unknown>(
    '/api/v1/payments/merchant-configs',
    { method: 'POST', body },
    signal
  );
  if (!isPaymentMerchantConfig(value)) {
    throw new Error('client.invalid_payment_merchant_config');
  }
  return value;
}

export async function updatePaymentMerchantConfig(
  id: string,
  body: UpdatePaymentMerchantConfigRequest,
  signal?: AbortSignal
): Promise<PaymentMerchantConfig> {
  const value = await request<unknown>(
    `/api/v1/payments/merchant-configs/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isPaymentMerchantConfig(value)) {
    throw new Error('client.invalid_payment_merchant_config');
  }
  return value;
}

export async function disablePaymentMerchantConfig(
  id: string,
  signal?: AbortSignal
): Promise<PaymentMerchantConfig> {
  const value = await request<unknown>(
    `/api/v1/payments/merchant-configs/${encodeURIComponent(id)}/disable`,
    { method: 'POST' },
    signal
  );
  if (!isPaymentMerchantConfig(value)) {
    throw new Error('client.invalid_payment_merchant_config');
  }
  return value;
}
