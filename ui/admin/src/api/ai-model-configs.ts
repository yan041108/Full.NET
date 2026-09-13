import {
  isAiModelConfig,
  isAiModelConfigPage,
  isAiTenantQuota,
  isAiTenantQuotaPage,
  isTestAiModelConfigResult,
  isTestAiModelEmbeddingResult,
  type AiModelConfig,
  type AiModelConfigListQuery,
  type AiModelConfigPage,
  type AiTenantQuota,
  type AiTenantQuotaListQuery,
  type AiTenantQuotaPage,
  type CreateAiModelConfigRequest,
  type TestAiModelConfigResult,
  type TestAiModelEmbeddingResult,
  type UpdateAiModelConfigRequest,
  type UpdateAiTenantQuotaRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildModelListQuery(query: AiModelConfigListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId?.trim()) {
    params.set('tenantId', query.tenantId.trim());
  }
  if (query.nameContains?.trim()) {
    params.set('nameContains', query.nameContains.trim());
  }
  if (query.isEnabled !== undefined) {
    params.set('isEnabled', String(query.isEnabled));
  }
  return params.toString();
}

function buildQuotaListQuery(query: AiTenantQuotaListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId?.trim()) {
    params.set('tenantId', query.tenantId.trim());
  }
  return params.toString();
}

export async function listAiModelConfigs(
  query: AiModelConfigListQuery = {},
  signal?: AbortSignal
): Promise<AiModelConfigPage> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs?${buildModelListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiModelConfigPage(value)) {
    throw new Error('client.invalid_ai_model_config_page');
  }
  return value;
}

export async function getAiModelConfig(
  id: string,
  signal?: AbortSignal
): Promise<AiModelConfig> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiModelConfig(value)) {
    throw new Error('client.invalid_ai_model_config');
  }
  return value;
}

export async function createAiModelConfig(
  body: CreateAiModelConfigRequest,
  signal?: AbortSignal
): Promise<AiModelConfig> {
  const value = await request<unknown>(
    '/api/v1/ai/model-configs',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiModelConfig(value)) {
    throw new Error('client.invalid_ai_model_config');
  }
  return value;
}

export async function updateAiModelConfig(
  id: string,
  body: UpdateAiModelConfigRequest,
  signal?: AbortSignal
): Promise<AiModelConfig> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiModelConfig(value)) {
    throw new Error('client.invalid_ai_model_config');
  }
  return value;
}

export async function disableAiModelConfig(
  id: string,
  signal?: AbortSignal
): Promise<AiModelConfig> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs/${encodeURIComponent(id)}/disable`,
    { method: 'POST' },
    signal
  );
  if (!isAiModelConfig(value)) {
    throw new Error('client.invalid_ai_model_config');
  }
  return value;
}

export async function testAiModelConfig(
  id: string,
  signal?: AbortSignal
): Promise<TestAiModelConfigResult> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs/${encodeURIComponent(id)}/test`,
    { method: 'POST' },
    signal
  );
  if (!isTestAiModelConfigResult(value)) {
    throw new Error('client.invalid_ai_model_config_test_result');
  }
  return value;
}

export async function testAiModelEmbeddings(
  id: string,
  input: string,
  signal?: AbortSignal
): Promise<TestAiModelEmbeddingResult> {
  const value = await request<unknown>(
    `/api/v1/ai/model-configs/${encodeURIComponent(id)}/test-embeddings`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ input, batchInputs: null })
    },
    signal
  );
  if (!isTestAiModelEmbeddingResult(value)) {
    throw new Error('client.invalid_ai_model_embedding_test_result');
  }
  return value;
}

export async function listAiTenantQuotas(
  query: AiTenantQuotaListQuery = {},
  signal?: AbortSignal
): Promise<AiTenantQuotaPage> {
  const value = await request<unknown>(
    `/api/v1/ai/tenant-quotas?${buildQuotaListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isAiTenantQuotaPage(value)) {
    throw new Error('client.invalid_ai_tenant_quota_page');
  }
  return value;
}

export async function upsertAiTenantQuota(
  tenantId: string,
  body: UpdateAiTenantQuotaRequest,
  signal?: AbortSignal
): Promise<AiTenantQuota> {
  const value = await request<unknown>(
    `/api/v1/ai/tenant-quotas/${encodeURIComponent(tenantId)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isAiTenantQuota(value)) {
    throw new Error('client.invalid_ai_tenant_quota');
  }
  return value;
}
