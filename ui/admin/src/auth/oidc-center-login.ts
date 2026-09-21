import {
  buildOidcAuthorizeUrl,
  createOidcAuthorizationRequest,
  exchangeOidcAuthorizationCode,
  refreshOidcAccessToken,
  revokeOidcApplicationSession,
  revokeOidcCenterSession,
  resolveFullNetApiUrl,
  validateOidcCallbackState,
  type TokenResponse
} from '@fullnet/client-contracts';
import { apiBaseUrl } from '../api/http';
import { resolveAdminOidcClientId } from '../config/identity-auth';
import {
  clearOidcRefreshCredential,
  readOidcRefreshCredential,
  writeOidcRefreshCredential
} from './oidc-session-credentials';

export const ADMIN_OIDC_PKCE_STORAGE_KEY = 'fullnet.admin.oidc.pkce';

export interface AdminOidcPkcePending {
  verifier: string;
  state: string;
  nonce: string;
}

/** OpenIddict 要求 redirect_uri 不得含 fragment；与 E2E 种子 `http://localhost:25175/` 对齐。 */
export function resolveAdminOidcRedirectUri(): string {
  const { origin, pathname, search } = window.location;
  if (pathname === '/' && search === '') {
    return `${origin}/`;
  }

  return `${origin}${pathname}${search}`;
}

/**
 * IdP 回跳到站点根路径 query 时，提升到 hash 回调路由供 Vue Router 处理。
 * @returns 是否已触发整页跳转（调用方应中止后续启动）。
 */
export function promoteOidcAuthorizationResponseToHashRoute(): boolean {
  const url = new URL(window.location.href);
  if (!url.searchParams.has('code') && !url.searchParams.has('error')) {
    return false;
  }

  const callbackQuery = new URLSearchParams();
  for (const key of ['code', 'state', 'iss', 'error', 'error_description']) {
    const value = url.searchParams.get(key);
    if (value !== null) {
      callbackQuery.set(key, value);
    }
  }

  const query = callbackQuery.toString();
  const hashPath = query.length > 0
    ? `#/identity/oidc/callback?${query}`
    : '#/identity/oidc/callback';
  window.location.replace(`${url.origin}${url.pathname}${hashPath}`);
  return true;
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
  query: Record<string, string | (string | null)[] | undefined | null>
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
    const exchange = await exchangeOidcAuthorizationCode({
      apiBase: resolveOidcApiBase(),
      clientId: resolveAdminOidcClientId(),
      redirectUri: resolveAdminOidcRedirectUri(),
      code,
      verifier: pending.verifier
    });
    if (exchange.refreshToken !== undefined) {
      writeOidcRefreshCredential({
        refreshToken: exchange.refreshToken,
        clientId: resolveAdminOidcClientId()
      });
    }

    return exchange.token;
  } finally {
    clearAdminOidcPkcePending();
  }
}

/** 使用已持久化的 refresh token 续签访问令牌；失败时清理本地凭据。 */
export async function refreshAdminOidcAccessToken(): Promise<TokenResponse | undefined> {
  const credential = readOidcRefreshCredential();
  if (credential === undefined) {
    return undefined;
  }

  try {
    const exchange = await refreshOidcAccessToken({
      apiBase: resolveOidcApiBase(),
      clientId: credential.clientId,
      refreshToken: credential.refreshToken
    });
    if (exchange.refreshToken !== undefined) {
      writeOidcRefreshCredential({
        refreshToken: exchange.refreshToken,
        clientId: credential.clientId
      });
    }

    return exchange.token;
  } catch {
    clearOidcRefreshCredential();
    return undefined;
  }
}

/** 撤销当前管理端 OIDC 应用会话；失败时仍由调用方清理本地状态。 */
export async function revokeAdminOidcApplicationSession(accessToken?: string): Promise<boolean> {
  return revokeOidcApplicationSession({
    apiBase: resolveOidcApiBase(),
    clientId: resolveAdminOidcClientId(),
    accessToken
  });
}

/** 撤销中心登录会话及全部 OIDC grant；失败时仍由调用方清理本地状态。 */
export async function revokeAdminOidcCenterSession(accessToken?: string): Promise<boolean> {
  return revokeOidcCenterSession({
    apiBase: resolveOidcApiBase(),
    accessToken
  });
}

export function clearAdminOidcSessionCredentials(): void {
  clearAdminOidcPkcePending();
  clearOidcRefreshCredential();
}

function readQueryValue(
  value: string | (string | null)[] | undefined | null
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
