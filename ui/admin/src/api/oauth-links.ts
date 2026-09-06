import {
  isOAuthUserLinkList,
  resolveFullNetApiUrl,
  type OAuthUserLink
} from '@fullnet/client-contracts';
import { apiBaseUrl, request } from './http';

export async function listOAuthUserLinks(signal?: AbortSignal): Promise<OAuthUserLink[]> {
  const value = await request<unknown>(
    '/api/v1/identity/me/oauth-links',
    { method: 'GET' },
    signal
  );
  if (!isOAuthUserLinkList(value)) {
    throw new Error('client.invalid_oauth_user_link_list');
  }
  return value;
}

export async function deleteOAuthUserLink(linkId: string, signal?: AbortSignal): Promise<void> {
  await request<unknown>(
    `/api/v1/identity/me/oauth-links/${encodeURIComponent(linkId)}`,
    { method: 'DELETE' },
    signal
  );
}

export function buildOAuthAuthorizeUrl(
  providerKey: string,
  mode: 'login' | 'bind',
  returnUrl: string
): string {
  const params = new URLSearchParams({
    mode,
    returnUrl
  });
  return resolveFullNetApiUrl(
    apiBaseUrl,
    `/api/v1/identity/oauth/${encodeURIComponent(providerKey)}/authorize?${params.toString()}`
  );
}
