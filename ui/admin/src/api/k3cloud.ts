import {
  isK3CloudConnectionConfig,
  isK3CloudDocumentSync,
  isK3CloudDocumentSyncPage,
  type CreateK3CloudConnectionConfigRequest,
  type CreateK3CloudDocumentSyncRequest,
  type K3CloudConnectionConfig,
  type K3CloudDocumentSync,
  type K3CloudDocumentSyncPage,
  type TestK3CloudConnectionConfigResult,
  type UpdateK3CloudConnectionConfigRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listK3CloudConnectionConfigs(
  signal?: AbortSignal
): Promise<K3CloudConnectionConfig[]> {
  const value = await request<unknown>('/api/v1/k3cloud/connection-configs', { method: 'GET' }, signal);
  if (!Array.isArray(value) || !value.every(isK3CloudConnectionConfig)) {
    throw new Error('client.invalid_k3cloud_connection_config_list');
  }
  return value;
}

export async function createK3CloudConnectionConfig(
  body: CreateK3CloudConnectionConfigRequest,
  signal?: AbortSignal
): Promise<K3CloudConnectionConfig> {
  const value = await request<unknown>('/api/v1/k3cloud/connection-configs', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
  if (!isK3CloudConnectionConfig(value)) {
    throw new Error('client.invalid_k3cloud_connection_config');
  }
  return value;
}

export async function updateK3CloudConnectionConfig(
  connectionConfigId: string,
  body: UpdateK3CloudConnectionConfigRequest,
  signal?: AbortSignal
): Promise<K3CloudConnectionConfig> {
  const value = await request<unknown>(
    `/api/v1/k3cloud/connection-configs/${encodeURIComponent(connectionConfigId)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isK3CloudConnectionConfig(value)) {
    throw new Error('client.invalid_k3cloud_connection_config');
  }
  return value;
}

export async function testK3CloudConnectionConfig(
  connectionConfigId: string,
  signal?: AbortSignal
): Promise<TestK3CloudConnectionConfigResult> {
  const value = await request<unknown>(
    `/api/v1/k3cloud/connection-configs/${encodeURIComponent(connectionConfigId)}/test`,
    { method: 'POST' },
    signal
  );
  if (
    typeof value !== 'object' ||
    value === null ||
    typeof (value as TestK3CloudConnectionConfigResult).succeeded !== 'boolean' ||
    typeof (value as TestK3CloudConnectionConfigResult).message !== 'string'
  ) {
    throw new Error('client.invalid_k3cloud_connection_test_result');
  }
  return value as TestK3CloudConnectionConfigResult;
}

export async function listK3CloudDocumentSyncs(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<K3CloudDocumentSyncPage> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const value = await request<unknown>(
    `/api/v1/k3cloud/document-syncs?${params.toString()}`,
    { method: 'GET' },
    signal
  );
  if (!isK3CloudDocumentSyncPage(value)) {
    throw new Error('client.invalid_k3cloud_document_sync_page');
  }
  return value;
}

export async function createK3CloudDocumentSync(
  body: CreateK3CloudDocumentSyncRequest,
  signal?: AbortSignal
): Promise<K3CloudDocumentSync> {
  const value = await request<unknown>('/api/v1/k3cloud/document-syncs', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
  if (!isK3CloudDocumentSync(value)) {
    throw new Error('client.invalid_k3cloud_document_sync');
  }
  return value;
}

export async function retryK3CloudDocumentSync(
  syncId: string,
  signal?: AbortSignal
): Promise<K3CloudDocumentSync> {
  const value = await request<unknown>(
    `/api/v1/k3cloud/document-syncs/${encodeURIComponent(syncId)}/retry`,
    { method: 'POST' },
    signal
  );
  if (!isK3CloudDocumentSync(value)) {
    throw new Error('client.invalid_k3cloud_document_sync');
  }
  return value;
}
