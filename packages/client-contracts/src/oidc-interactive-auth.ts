export interface OidcPkcePair {
  verifier: string;
  challenge: string;
}

export interface OidcAuthorizationRequest extends OidcPkcePair {
  state: string;
  nonce: string;
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

/** 校验回调 state，防止 CSRF 与授权响应替换。 */
export function validateOidcCallbackState(
  expectedState: string,
  returnedState: string | null | undefined
): boolean {
  return typeof returnedState === 'string'
    && returnedState.length > 0
    && returnedState === expectedState;
}