import {
  isOAuthProvider,
  isOAuthProviderPage,
  isPublicOAuthProviderList,
  type CreateOAuthProviderRequest,
  type OAuthProvider,
  type OAuthProviderListQuery,
  type OAuthProviderPage,
  type PublicOAuthProvider,
  type UpdateOAuthProviderRequest
} from '@fullnet/client-contracts';
import { request } from './http';

/** OAuth 回调相对路径，供管理端表单默认值使用，页面不得直接拼接请求。 */
export const OAUTH_PROVIDER_CALLBACK_PATH = '/api/v1/identity/oauth/callback';

function buildListQuery(query: OAuthProviderListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.providerKeyContains?.trim()) {
    params.set('providerKeyContains', query.providerKeyContains.trim());
  }
  if (query.displayNameContains?.trim()) {
    params.set('displayNameContains', query.displayNameContains.trim());
  }
  if (query.isEnabled !== undefined) {
    params.set('isEnabled', String(query.isEnabled));
  }
  return params.toString();
}

export async function listOAuthProviders(
  query: OAuthProviderListQuery = {},
  signal?: AbortSignal
): Promise<OAuthProviderPage> {
  const value = await request<unknown>(
    `/api/v1/identity/oauth-providers?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isOAuthProviderPage(value)) {
    throw new Error('client.invalid_oauth_provider_page');
  }
  return value;
}

export async function getOAuthProvider(id: string, signal?: AbortSignal): Promise<OAuthProvider> {
  const value = await request<unknown>(
    `/api/v1/identity/oauth-providers/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isOAuthProvider(value)) {
    throw new Error('client.invalid_oauth_provider');
  }
  return value;
}

export async function createOAuthProvider(
  body: CreateOAuthProviderRequest,
  signal?: AbortSignal
): Promise<OAuthProvider> {
  const value = await request<unknown>(
    '/api/v1/identity/oauth-providers',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isOAuthProvider(value)) {
    throw new Error('client.invalid_oauth_provider');
  }
  return value;
}

export async function updateOAuthProvider(
  id: string,
  body: UpdateOAuthProviderRequest,
  signal?: AbortSignal
): Promise<OAuthProvider> {
  const value = await request<unknown>(
    `/api/v1/identity/oauth-providers/${encodeURIComponent(id)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isOAuthProvider(value)) {
    throw new Error('client.invalid_oauth_provider');
  }
  return value;
}

export async function deleteOAuthProvider(id: string, signal?: AbortSignal): Promise<void> {
  await request<unknown>(
    `/api/v1/identity/oauth-providers/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
}

export async function listPublicOAuthProviders(signal?: AbortSignal): Promise<PublicOAuthProvider[]> {
  const value = await request<unknown>(
    '/api/v1/identity/oauth/providers',
    { method: 'GET' },
    signal
  );
  if (!isPublicOAuthProviderList(value)) {
    throw new Error('client.invalid_public_oauth_provider_list');
  }
  return value;
}
