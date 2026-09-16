import {
  buildOidcAuthorizeUrl,
  createOidcAuthorizationRequest,
  exchangeOidcAuthorizationCode,
  resolveFullNetApiUrl,
  validateOidcCallbackState,
  type TokenResponse
} from '@fullnet/client-contracts';
import { apiBaseUrl } from '../api/http';
import { resolveAdminOidcClientId } from '../config/identity-auth';

export const ADMIN_OIDC_PKCE_STORAGE_KEY = 'fullnet.admin.oidc.pkce';

export interface AdminOidcPkcePending {
  verifier: string;
  state: string;
  nonce: string;
}

export function resolveAdminOidcRedirectUri(): string {
  const { origin, pathname, search } = window.location;
  return `${origin}${pathname}${search}#/identity/oidc/callback`;
}

function resolveOidcApiBase(): string {
  return resolveFullNetApiUrl(apiBaseUrl, '');
}

export function readAdminOidcPkcePending(): AdminOidcPkcePending | undefined {
  const raw = sessionStorage.getItem(ADMIN_OIDC_PKCE_STORAGE_KEY);
  if (raw === null) {
    return undefined;
  }

  try {
    const parsed: unknown = JSON.parse(raw);
    if (!isAdminOidcPkcePending(parsed)) {
      return undefined;
    }

    return parsed;
  } catch {
    return undefined;
  }
}

export function clearAdminOidcPkcePending(): void {
  sessionStorage.removeItem(ADMIN_OIDC_PKCE_STORAGE_KEY);
}

export async function beginAdminOidcCenterLogin(): Promise<void> {
  const request = await createOidcAuthorizationRequest();
  sessionStorage.setItem(ADMIN_OIDC_PKCE_STORAGE_KEY, JSON.stringify(request));
  const url = buildOidcAuthorizeUrl({
    apiBase: resolveOidcApiBase(),
    clientId: resolveAdminOidcClientId(),
    redirectUri: resolveAdminOidcRedirectUri(),
    challenge: request.challenge,
    state: request.state,
    nonce: request.nonce,
    scope: 'openid profile offline_access'
  });
  window.location.assign(url);
}

export async function completeAdminOidcCallback(
  query: Record<string, string | string[] | undefined | null>
): Promise<TokenResponse> {
  const error = readQueryValue(query.error);
  if (error !== undefined) {
    throw new Error(error);
  }

  const code = readQueryValue(query.code);
  const returnedState = readQueryValue(query.state);
  const pending = readAdminOidcPkcePending();
  if (code === undefined || pending === undefined) {
    throw new Error('oidc_invalid_callback');
  }

  if (!validateOidcCallbackState(pending.state, returnedState)) {
    clearAdminOidcPkcePending();
    throw new Error('oidc_invalid_state');
  }

  try {
    return await exchangeOidcAuthorizationCode({
      apiBase: resolveOidcApiBase(),
      clientId: resolveAdminOidcClientId(),
      redirectUri: resolveAdminOidcRedirectUri(),
      code,
      verifier: pending.verifier
    });
  } finally {
    clearAdminOidcPkcePending();
  }
}

function readQueryValue(
  value: string | string[] | undefined | null
): string | undefined {
  if (typeof value === 'string' && value.length > 0) {
    return value;
  }

  return undefined;
}

function isAdminOidcPkcePending(value: unknown): value is AdminOidcPkcePending {
  return typeof value === 'object'
    && value !== null
    && typeof (value as AdminOidcPkcePending).verifier === 'string'
    && typeof (value as AdminOidcPkcePending).state === 'string'
    && typeof (value as AdminOidcPkcePending).nonce === 'string';
}