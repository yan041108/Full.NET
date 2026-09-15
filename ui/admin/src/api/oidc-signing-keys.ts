import {
  isOidcSigningKeyList,
  type OidcSigningKeyList
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listOidcSigningKeys(signal?: AbortSignal): Promise<OidcSigningKeyList> {
  const value = await request<unknown>(
    '/api/v1/identity/oidc-signing-keys',
    { method: 'GET' },
    signal
  );
  if (!isOidcSigningKeyList(value)) {
    throw new Error('client.invalid_oidc_signing_key_list');
  }
  return value;
}

export async function activateOidcSigningKey(
  keyId: string,
  signal?: AbortSignal
): Promise<OidcSigningKeyList> {
  const value = await request<unknown>(
    `/api/v1/identity/oidc-signing-keys/${encodeURIComponent(keyId)}/activate`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' },
    signal
  );
  if (!isOidcSigningKeyList(value)) {
    throw new Error('client.invalid_oidc_signing_key_list');
  }
  return value;
}

export type { OidcSigningKeyList };