import {
  isCreateOidcClientResult,
  isOidcClient,
  isOidcClientPage,
  isRotateOidcClientSecretResult,
  type CreateOidcClientRequest,
  type CreateOidcClientResult,
  type OidcClient,
  type OidcClientListQuery,
  type OidcClientPage,
  type RotateOidcClientSecretResult,
  type UpdateOidcClientRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: OidcClientListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.clientIdContains?.trim()) {
    params.set('clientIdContains', query.clientIdContains.trim());
  }
  return params.toString();
}

export async function listOidcClients(
  query: OidcClientListQuery = {},
  signal?: AbortSignal
): Promise<OidcClientPage> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-clients?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isOidcClientPage(value)) {
    throw new Error('client.invalid_oidc_client_page');
  }
  return value;
}

export async function getOidcClient(
  id: string,
  signal?: AbortSignal
): Promise<OidcClient> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-clients/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isOidcClient(value)) {
    throw new Error('client.invalid_oidc_client');
  }
  return value;
}

export async function createOidcClient(
  body: CreateOidcClientRequest,
  signal?: AbortSignal
): Promise<CreateOidcClientResult> {
  const value = await request<unknown>(
    '/api/v1/identity/oidc-clients',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isCreateOidcClientResult(value)) {
    throw new Error('client.invalid_create_oidc_client_result');
  }
  return value;
}

export async function updateOidcClient(
  id: string,
  body: UpdateOidcClientRequest,
  signal?: AbortSignal
): Promise<OidcClient> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-clients/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isOidcClient(value)) {
    throw new Error('client.invalid_oidc_client');
  }
  return value;
}

export async function disableOidcClient(
  id: string,
  signal?: AbortSignal
): Promise<OidcClient> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-clients/${encodeURIComponent(id)}/disable`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' },
    signal
  );
  if (!isOidcClient(value)) {
    throw new Error('client.invalid_oidc_client');
  }
  return value;
}

export async function rotateOidcClientSecret(
  id: string,
  signal?: AbortSignal
): Promise<RotateOidcClientSecretResult> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-clients/${encodeURIComponent(id)}/rotate`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' },
    signal
  );
  if (!isRotateOidcClientSecretResult(value)) {
    throw new Error('client.invalid_rotate_oidc_client_secret_result');
  }
  return value;
}

export type {
  CreateOidcClientRequest,
  CreateOidcClientResult,
  OidcClient,
  OidcClientListQuery,
  OidcClientPage,
  RotateOidcClientSecretResult,
  UpdateOidcClientRequest
};
