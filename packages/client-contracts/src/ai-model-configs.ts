export interface AiModelConfigListItem {
  id: string;
  tenantId: string | null;
  name: string;
  providerKey: string;
  maskedEndpointBaseUrl: string;
  modelId: string;
  hasApiKey: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiModelConfig {
  id: string;
  tenantId: string | null;
  name: string;
  providerKey: string;
  endpointBaseUrl: string;
  modelId: string;
  organizationId: string | null;
  hasApiKey: boolean;
  isDefault: boolean;
  isEnabled: boolean;
  lastTestedAtUtc: string | null;
  lastTestStatusKey: string | null;
  lastTestMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiModelConfigPage {
  items: AiModelConfigListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateAiModelConfigRequest {
  tenantId?: string | null;
  name: string;
  providerKey: string;
  endpointBaseUrl: string;
  modelId: string;
  apiKey?: string | null;
  organizationId?: string | null;
  isDefault: boolean;
  isEnabled: boolean;
}

export interface UpdateAiModelConfigRequest {
  name: string;
  providerKey: string;
  endpointBaseUrl: string;
  modelId: string;
  apiKey?: string | null;
  clearApiKey: boolean;
  organizationId?: string | null;
  isDefault: boolean;
  isEnabled: boolean;
  version: number;
}

export interface TestAiModelConfigResult {
  succeeded: boolean;
  message: string;
}

export interface AiModelConfigListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
  nameContains?: string;
  isEnabled?: boolean;
}

export interface AiTenantQuotaListItem {
  id: string;
  tenantId: string;
  monthlyTokenLimit: number | null;
  monthlyRequestLimit: number | null;
  usedTokensThisMonth: number;
  usedRequestsThisMonth: number;
  quotaMonthKey: string;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiTenantQuota {
  id: string;
  tenantId: string;
  monthlyTokenLimit: number | null;
  monthlyRequestLimit: number | null;
  usedTokensThisMonth: number;
  usedRequestsThisMonth: number;
  quotaMonthKey: string;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface AiTenantQuotaPage {
  items: AiTenantQuotaListItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface UpdateAiTenantQuotaRequest {
  monthlyTokenLimit?: number | null;
  monthlyRequestLimit?: number | null;
  isEnabled: boolean;
  version: number;
}

export interface AiTenantQuotaListQuery {
  page?: number;
  pageSize?: number;
  tenantId?: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || typeof value === 'number';
}

export function isAiModelConfigListItem(value: unknown): value is AiModelConfigListItem {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.providerKey === 'string'
    && typeof value.maskedEndpointBaseUrl === 'string'
    && typeof value.modelId === 'string'
    && typeof value.hasApiKey === 'boolean'
    && typeof value.isDefault === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && (value.lastTestedAtUtc === null || typeof value.lastTestedAtUtc === 'string')
    && isNullableString(value.lastTestStatusKey)
    && isNullableString(value.lastTestMessage)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isAiModelConfig(value: unknown): value is AiModelConfig {
  return isRecord(value)
    && isGuid(value.id)
    && (value.tenantId === null || isGuid(value.tenantId))
    && typeof value.name === 'string'
    && typeof value.providerKey === 'string'
    && typeof value.endpointBaseUrl === 'string'
    && typeof value.modelId === 'string'
    && isNullableString(value.organizationId)
    && typeof value.hasApiKey === 'boolean'
    && typeof value.isDefault === 'boolean'
    && typeof value.isEnabled === 'boolean'
    && (value.lastTestedAtUtc === null || typeof value.lastTestedAtUtc === 'string')
    && isNullableString(value.lastTestStatusKey)
    && isNullableString(value.lastTestMessage)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isAiModelConfigPage(value: unknown): value is AiModelConfigPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAiModelConfigListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isTestAiModelConfigResult(value: unknown): value is TestAiModelConfigResult {
  return isRecord(value)
    && typeof value.succeeded === 'boolean'
    && typeof value.message === 'string';
}

export function isAiTenantQuotaListItem(value: unknown): value is AiTenantQuotaListItem {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.tenantId)
    && isNullableNumber(value.monthlyTokenLimit)
    && isNullableNumber(value.monthlyRequestLimit)
    && typeof value.usedTokensThisMonth === 'number'
    && typeof value.usedRequestsThisMonth === 'number'
    && typeof value.quotaMonthKey === 'string'
    && typeof value.isEnabled === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && typeof value.version === 'number';
}

export function isAiTenantQuota(value: unknown): value is AiTenantQuota {
  return isAiTenantQuotaListItem(value);
}

export function isAiTenantQuotaPage(value: unknown): value is AiTenantQuotaPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAiTenantQuotaListItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}
