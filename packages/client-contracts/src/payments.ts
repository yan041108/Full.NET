export interface PaymentMerchantConfigListItem {
  id: string;
  tenantId: string | null;
  name: string;
  channelKey: string;
  maskedAppId: string;
  maskedMerchantId: string;
  maskedCertificateSerialNo: string;
  maskedNotifyUrl: string;
  hasApiV3Key: boolean;
  hasPrivateKey: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface PaymentMerchantConfig {
  id: string;
  tenantId: string | null;
  name: string;
  channelKey: string;
  appId: string;
  merchantId: string;
  certificateSerialNo: string;
  notifyUrl: string;
  hasApiV3Key: boolean;
  hasPrivateKey: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface PaymentMerchantConfigPage {
  items: PaymentMerchantConfigListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreatePaymentMerchantConfigRequest {
  tenantId?: string | null;
  name: string;
  channelKey: string;
  appId: string;
  merchantId: string;
  certificateSerialNo: string;
  notifyUrl: string;
  apiV3Key?: string | null;
  privateKeyPem?: string | null;
  isDefault: boolean;
  isEnabled: boolean;
}

export interface UpdatePaymentMerchantConfigRequest {
  name: string;
  channelKey: string;
  appId: string;
  merchantId: string;
  certificateSerialNo: string;
  notifyUrl: string;
  apiV3Key?: string | null;
  clearApiV3Key: boolean;
  privateKeyPem?: string | null;
  clearPrivateKey: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  version: number;
}

export interface PaymentOrderListItem {
  id: string;
  tenantId: string;
  merchantConfigId: string;
  channelKey: string;
  outTradeNo: string;
  tradeStateKey: string;
  amountMinor: number;
  currency: string;
  subject: string;
  description: string | null;
  codeUrl: string | null;
  providerTransactionId: string | null;
  failMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  paidAtUtc: string | null;
  version: number;
}

export interface PaymentOrder {
  id: string;
  tenantId: string;
  merchantConfigId: string;
  channelKey: string;
  outTradeNo: string;
  tradeStateKey: string;
  amountMinor: number;
  currency: string;
  subject: string;
  description: string | null;
  codeUrl: string | null;
  providerTransactionId: string | null;
  failMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  paidAtUtc: string | null;
  version: number;
}

export interface PaymentOrderPage {
  items: PaymentOrderListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreatePaymentOrderRequest {
  tenantId: string;
  merchantConfigId?: string | null;
  amountMinor: number;
  currency: string;
  subject: string;
  description?: string | null;
}

export interface PaymentMerchantConfigListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  channelKey?: string;
  nameContains?: string;
  isEnabled?: boolean;
}

export interface PaymentOrderListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  tradeStateKey?: string;
  outTradeNoContains?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isPaymentMerchantConfigListItem(value: unknown): value is PaymentMerchantConfigListItem {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.channelKey === 'string'
    && typeof value.isDefault === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && typeof value.version === 'number';
}

export function isPaymentMerchantConfig(value: unknown): value is PaymentMerchantConfig {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.channelKey === 'string'
    && typeof value.appId === 'string'
    && typeof value.version === 'number';
}

export function isPaymentMerchantConfigPage(value: unknown): value is PaymentMerchantConfigPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isPaymentMerchantConfigListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isPaymentOrderListItem(value: unknown): value is PaymentOrderListItem {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && isGuid(value.merchantConfigId)
    && typeof value.outTradeNo === 'string'
    && typeof value.tradeStateKey === 'string'
    && typeof value.amountMinor === 'number';
}

export function isPaymentOrder(value: unknown): value is PaymentOrder {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && typeof value.outTradeNo === 'string'
    && typeof value.tradeStateKey === 'string';
}

export function isPaymentOrderPage(value: unknown): value is PaymentOrderPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isPaymentOrderListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}
