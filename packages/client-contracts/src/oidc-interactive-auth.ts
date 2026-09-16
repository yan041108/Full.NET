import type { TokenResponse } from './identity.js';

export interface OidcPkcePair {
  verifier: string;
  challenge: string;
}

export interface OidcAuthorizationRequest extends OidcPkcePair {
  state: string;
  nonce: string;
}

export interface OidcTokenEndpointResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  refresh_token?: string;
  id_token?: string;
}

export interface ExchangeOidcAuthorizationCodeOptions {
  apiBase: string;
  clientId: string;
  redirectUri: string;
  code: string;
  verifier: string;
  clientSecret?: string | null;
}

export interface BuildOidcAuthorizeUrlOptions {
  apiBase: string;
  clientId: string;
  redirectUri: string;
  challenge: string;
  state: string;
  nonce: string;
  scope?: string;
  extraParams?: Record<string, string>;
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/u, '');
}

function createRandomUrlSafeToken(byteLength = 16): string {
  const bytes = crypto.getRandomValues(new Uint8Array(byteLength));
  return base64UrlEncode(bytes);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

/** 生成 OIDC PKCE verifier/challenge（S256）。 */
export async function createOidcPkcePair(): Promise<OidcPkcePair> {
  const verifier = createRandomUrlSafeToken(32);
  const digest = await crypto.subtle.digest(
    'SHA-256',
    new TextEncoder().encode(verifier)
  );
  const challenge = base64UrlEncode(new Uint8Array(digest));
  return { verifier, challenge };
}

/** 生成授权码流程所需的 PKCE、state 与 nonce。 */
export async function createOidcAuthorizationRequest(): Promise<OidcAuthorizationRequest> {
  const { verifier, challenge } = await createOidcPkcePair();
  return {
    verifier,
    challenge,
    state: createRandomUrlSafeToken(16),
    nonce: createRandomUrlSafeToken(16)
  };
}

/** 构造标准授权码 + PKCE 授权 URL。 */
export function buildOidcAuthorizeUrl(options: BuildOidcAuthorizeUrlOptions): string {
  const apiBase = options.apiBase.replace(/\/$/u, '');
  const params = new URLSearchParams({
    client_id: options.clientId,
    redirect_uri: options.redirectUri,
    response_type: 'code',
    scope: options.scope ?? 'openid profile',
    state: options.state,
    nonce: options.nonce,
    code_challenge: options.challenge,
    code_challenge_method: 'S256',
    ...options.extraParams
  });
  return `${apiBase}/connect/authorize?${params.toString()}`;
}

/** 校验 OIDC token endpoint 响应最小契约。 */
export function isOidcTokenEndpointResponse(
  value: unknown
): value is OidcTokenEndpointResponse {
  return isRecord(value)
    && typeof value.access_token === 'string'
    && value.access_token.length > 0
    && typeof value.token_type === 'string'
    && typeof value.expires_in === 'number'
    && Number.isFinite(value.expires_in);
}

/** 将 OIDC token endpoint 响应映射为管理端会话令牌契约。 */
export function mapOidcTokenEndpointToTokenResponse(
  value: OidcTokenEndpointResponse
): TokenResponse {
  const expiresAtUtc = new Date(
    Date.now() + Math.max(0, value.expires_in) * 1000
  ).toISOString();
  return {
    accessToken: value.access_token,
    tokenType: 'Bearer',
    expiresAtUtc
  };
}

/** 使用授权码与 PKCE verifier 兑换访问令牌。 */
export async function exchangeOidcAuthorizationCode(
  options: ExchangeOidcAuthorizationCodeOptions
): Promise<TokenResponse> {
  const apiBase = options.apiBase.replace(/\/$/u, '');
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code: options.code,
    redirect_uri: options.redirectUri,
    client_id: options.clientId,
    code_verifier: options.verifier
  });
  if (options.clientSecret) {
    body.set('client_secret', options.clientSecret);
  }

  const response = await fetch(`${apiBase}/connect/token`, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: body.toString()
  });
  const payload: unknown = await response.json().catch(() => undefined);
  if (!response.ok) {
    throw new TypeError('OIDC authorization code exchange failed.');
  }

  if (!isOidcTokenEndpointResponse(payload)) {
    throw new TypeError('OIDC token endpoint response is invalid.');
  }

  return mapOidcTokenEndpointToTokenResponse(payload);
}

/** 校验回调 state，防止 CSRF 与授权响应替换。 */
export function validateOidcCallbackState(
  expectedState: string,
  returnedState: string | null | undefined
): boolean {
  return typeof returnedState === 'string'
    && returnedState.length > 0
    && returnedState === expectedState;
}