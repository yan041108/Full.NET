import {
  isCreateOpenAccessClientResult,
  isOpenAccessClient,
  isOpenAccessClientAccessLogPage,
  isOpenAccessClientPage,
  isOpenAccessClientSignatureDebugResult,
  isOpenAccessClientUsage,
  type CreateOpenAccessClientRequest,
  type CreateOpenAccessClientResult,
  type OpenAccessClient,
  type OpenAccessClientAccessLogPage,
  type OpenAccessClientAccessLogQuery,
  type OpenAccessClientListQuery,
  type OpenAccessClientPage,
  type OpenAccessClientSignatureDebugRequest,
  type OpenAccessClientSignatureDebugResult,
  type OpenAccessClientUsage,
  type UpdateOpenAccessClientRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: OpenAccessClientListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.userId?.trim()) {
    params.set('userId', query.userId.trim());
  }
  if (query.nameContains?.trim()) {
    params.set('nameContains', query.nameContains.trim());
  }
  return params.toString();
}

function buildAccessLogQuery(query: OpenAccessClientAccessLogQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.succeeded !== undefined) {
    params.set('succeeded', String(query.succeeded));
  }
  if (query.fromUtc?.trim()) {
    params.set('fromUtc', query.fromUtc.trim());
  }
  if (query.toUtc?.trim()) {
    params.set('toUtc', query.toUtc.trim());
  }
  return params.toString();
}

export async function listOpenAccessClients(
  query: OpenAccessClientListQuery = {},
  signal?: AbortSignal
): Promise<OpenAccessClientPage> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isOpenAccessClientPage(value)) {
    throw new Error('client.invalid_open_access_client_page');
  }
  return value;
}

export async function createOpenAccessClient(
  body: CreateOpenAccessClientRequest,
  signal?: AbortSignal
): Promise<CreateOpenAccessClientResult> {
  const value = await request<unknown>(
    '/api/v1/identity/open-access-clients',
    { method: 'POST', body },
    signal
  );
  if (!isCreateOpenAccessClientResult(value)) {
    throw new Error('client.invalid_create_open_access_client_result');
  }
  return value;
}

export async function updateOpenAccessClient(
  id: string,
  body: UpdateOpenAccessClientRequest,
  signal?: AbortSignal
): Promise<OpenAccessClient> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isOpenAccessClient(value)) {
    throw new Error('client.invalid_open_access_client');
  }
  return value;
}

export async function disableOpenAccessClient(
  id: string,
  signal?: AbortSignal
): Promise<OpenAccessClient> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}/disable`,
    { method: 'POST', body: {} },
    signal
  );
  if (!isOpenAccessClient(value)) {
    throw new Error('client.invalid_open_access_client');
  }
  return value;
}

export async function rotateOpenAccessClient(
  id: string,
  signal?: AbortSignal
): Promise<CreateOpenAccessClientResult> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}/rotate`,
    { method: 'POST', body: {} },
    signal
  );
  if (!isCreateOpenAccessClientResult(value)) {
    throw new Error('client.invalid_create_open_access_client_result');
  }
  return value;
}

export async function listOpenAccessClientAccessLogs(
  id: string,
  query: OpenAccessClientAccessLogQuery = {},
  signal?: AbortSignal
): Promise<OpenAccessClientAccessLogPage> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}/access-logs?${buildAccessLogQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isOpenAccessClientAccessLogPage(value)) {
    throw new Error('client.invalid_open_access_client_access_log_page');
  }
  return value;
}

export async function getOpenAccessClientUsage(
  id: string,
  signal?: AbortSignal
): Promise<OpenAccessClientUsage> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}/usage`,
    { method: 'GET' },
    signal
  );
  if (!isOpenAccessClientUsage(value)) {
    throw new Error('client.invalid_open_access_client_usage');
  }
  return value;
}

export async function debugOpenAccessClientSignature(
  id: string,
  body: OpenAccessClientSignatureDebugRequest,
  signal?: AbortSignal
): Promise<OpenAccessClientSignatureDebugResult> {
  const value = await request<unknown>(
    `/api/v1/identity/open-access-clients/${encodeURIComponent(id)}/signature-debug`,
    { method: 'POST', body },
    signal
  );
  if (!isOpenAccessClientSignatureDebugResult(value)) {
    throw new Error('client.invalid_open_access_client_signature_debug_result');
  }
  return value;
}

export type {
  CreateOpenAccessClientRequest,
  CreateOpenAccessClientResult,
  OpenAccessClient,
  OpenAccessClientAccessLogPage,
  OpenAccessClientAccessLogQuery,
  OpenAccessClientPage,
  OpenAccessClientSignatureDebugRequest,
  OpenAccessClientSignatureDebugResult,
  OpenAccessClientUsage,
  UpdateOpenAccessClientRequest
};
