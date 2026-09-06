import {
  isLdapConnection,
  isLdapConnectionPage,
  isPreviewLdapSyncResponse,
  isTestLdapAuthenticationResult,
  isTestLdapConnectionResult,
  type CreateLdapConnectionRequest,
  type LdapConnection,
  type LdapConnectionListQuery,
  type LdapConnectionPage,
  type PreviewLdapSyncRequest,
  type PreviewLdapSyncResponse,
  type TestLdapAuthenticationRequest,
  type TestLdapAuthenticationResult,
  type TestLdapConnectionResult,
  type UpdateLdapConnectionRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: LdapConnectionListQuery): string {
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

export async function listLdapConnections(
  query: LdapConnectionListQuery = {},
  signal?: AbortSignal
): Promise<LdapConnectionPage> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isLdapConnectionPage(value)) {
    throw new Error('client.invalid_ldap_connection_page');
  }
  return value;
}

export async function getLdapConnection(id: string, signal?: AbortSignal): Promise<LdapConnection> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isLdapConnection(value)) {
    throw new Error('client.invalid_ldap_connection');
  }
  return value;
}

export async function createLdapConnection(
  body: CreateLdapConnectionRequest,
  signal?: AbortSignal
): Promise<LdapConnection> {
  const value = await request<unknown>(
    '/api/v1/identity/ldap-connections',
    { method: 'POST', body },
    signal
  );
  if (!isLdapConnection(value)) {
    throw new Error('client.invalid_ldap_connection');
  }
  return value;
}

export async function updateLdapConnection(
  id: string,
  body: UpdateLdapConnectionRequest,
  signal?: AbortSignal
): Promise<LdapConnection> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isLdapConnection(value)) {
    throw new Error('client.invalid_ldap_connection');
  }
  return value;
}

export async function disableLdapConnection(id: string, signal?: AbortSignal): Promise<LdapConnection> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}/disable`,
    { method: 'POST' },
    signal
  );
  if (!isLdapConnection(value)) {
    throw new Error('client.invalid_ldap_connection');
  }
  return value;
}

export async function deleteLdapConnection(id: string, signal?: AbortSignal): Promise<void> {
  await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
}

export async function testLdapConnection(
  id: string,
  signal?: AbortSignal
): Promise<TestLdapConnectionResult> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}/test-connection`,
    { method: 'POST' },
    signal
  );
  if (!isTestLdapConnectionResult(value)) {
    throw new Error('client.invalid_ldap_connection_test_result');
  }
  return value;
}

export async function testLdapAuthentication(
  id: string,
  body: TestLdapAuthenticationRequest,
  signal?: AbortSignal
): Promise<TestLdapAuthenticationResult> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}/test-authentication`,
    { method: 'POST', body },
    signal
  );
  if (!isTestLdapAuthenticationResult(value)) {
    throw new Error('client.invalid_ldap_authentication_test_result');
  }
  return value;
}

export async function previewLdapSync(
  id: string,
  body: PreviewLdapSyncRequest = {},
  signal?: AbortSignal
): Promise<PreviewLdapSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/identity/ldap-connections/${encodeURIComponent(id)}/preview-sync`,
    { method: 'POST', body },
    signal
  );
  if (!isPreviewLdapSyncResponse(value)) {
    throw new Error('client.invalid_ldap_sync_preview');
  }
  return value;
}
