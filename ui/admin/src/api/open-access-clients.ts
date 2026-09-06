import {
  isCreateOpenAccessClientResult,
  isOpenAccessClient,
  isOpenAccessClientPage,
  type CreateOpenAccessClientRequest,
  type CreateOpenAccessClientResult,
  type OpenAccessClient,
  type OpenAccessClientListQuery,
  type OpenAccessClientPage,
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

export type {
  CreateOpenAccessClientRequest,
  CreateOpenAccessClientResult,
  OpenAccessClient,
  OpenAccessClientPage,
  UpdateOpenAccessClientRequest
};
