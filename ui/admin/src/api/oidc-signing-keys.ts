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

/** 生命周期操作只引用已配置的 kid，不接收或返回私钥。 */
export async function changeOidcSigningKeyLifecycle(keyId: string, operation: 'publish' | 'retire'): Promise<OidcSigningKeyList> {
  const value = await request<unknown>(`/api/v1/identity/oidc-signing-keys/${encodeURIComponent(keyId)}/${operation}`, { method: 'POST' });
  if (!isOidcSigningKeyList(value)) throw new Error('client.invalid_oidc_signing_key_list');
  return value;
}
