import {
  isOidcAuthorization,
  isOidcAuthorizationPage,
  type OidcAuthorization,
  type OidcAuthorizationListQuery,
  type OidcAuthorizationPage
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: OidcAuthorizationListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.applicationId?.trim()) {
    params.set('applicationId', query.applicationId.trim());
  }
  if (query.subject?.trim()) {
    params.set('subject', query.subject.trim());
  }
  if (query.status?.trim()) {
    params.set('status', query.status.trim());
  }
  if (query.clientIdContains?.trim()) {
    params.set('clientIdContains', query.clientIdContains.trim());
  }
  return params.toString();
}

export async function listOidcAuthorizations(
  query: OidcAuthorizationListQuery = {},
  signal?: AbortSignal
): Promise<OidcAuthorizationPage> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-authorizations?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isOidcAuthorizationPage(value)) {
    throw new Error('client.invalid_oidc_authorization_page');
  }
  return value;
}

export async function getOidcAuthorization(
  id: string,
  signal?: AbortSignal
): Promise<OidcAuthorization> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-authorizations/${encodeURIComponent(id)}`,
    { method: 'GET' },
    signal
  );
  if (!isOidcAuthorization(value)) {
    throw new Error('client.invalid_oidc_authorization');
  }
  return value;
}

export async function revokeOidcAuthorization(
  id: string,
  signal?: AbortSignal
): Promise<OidcAuthorization> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-authorizations/${encodeURIComponent(id)}/revoke`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' },
    signal
  );
  if (!isOidcAuthorization(value)) {
    throw new Error('client.invalid_oidc_authorization');
  }
  return value;
}

export type {
  OidcAuthorization,
  OidcAuthorizationListQuery,
  OidcAuthorizationPage
};
