import { createHash, randomBytes } from 'node:crypto';
import { expect } from '@playwright/test';

export const OIDC_CLIENT_A = {
  clientId: 'e2e-oidc-rp-a',
  origin: 'http://localhost:5173',
  redirectUri: 'http://localhost:5173/'
};

export const OIDC_CLIENT_B = {
  clientId: 'e2e-oidc-rp-b',
  origin: 'http://localhost:5174',
  redirectUri: 'http://localhost:5174/',
  clientSecret: 'e2e-oidc-rp-b-secret'
};

export const CENTER_COOKIE_NAME = 'fullnet-oidc-center';

export const EXTERNAL_OIDC_REDIRECT_URI = 'http://localhost:5175/signin-oidc-external';

export function resolveApiBase() {
  const apiBase = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  return apiBase.replace(/\/$/, '');
}

export function resolveRpUrl(client, apiBase = resolveApiBase()) {
  return `${client.origin}/?api=${encodeURIComponent(apiBase)}`;
}

export function createPkcePair() {
  const verifier = base64UrlEncode(randomBytes(32));
  const challenge = base64UrlEncode(
    createHash('sha256').update(verifier).digest()
  );
  return { verifier, challenge };
}

export function buildAuthorizeUrl({
  apiBase,
  clientId,
  redirectUri,
  challenge,
  scope = 'openid profile',
  extraParams = {}
}) {
  const params = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope,
    state: 'state',
    nonce: 'nonce',
    code_challenge: challenge,
    code_challenge_method: 'S256',
    ...extraParams
  });
  return `${apiBase}/connect/authorize?${params.toString()}`;
}

function base64UrlEncode(buffer) {
  return buffer
    .toString('base64')
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/u, '');
}

function decodeHtmlAttribute(value) {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&#x27;', "'")
    .replaceAll('&amp;', '&');
}

export async function createExternalOidcClientViaApi(request, adminAccessToken, {
  clientId,
  redirectUri = EXTERNAL_OIDC_REDIRECT_URI,
  scopes = ['openid', 'profile']
}) {
  const response = await request.post(`${resolveApiBase()}/api/v1/identity/oidc-clients`, {
    headers: {
      authorization: `Bearer ${adminAccessToken}`,
      'content-type': 'application/json'
    },
    data: {
      clientId,
      displayName: 'E2E external OIDC client',
      redirectUris: [redirectUri],
      postLogoutRedirectUris: [],
      scopes,
      isConfidential: false,
      isFirstParty: false,
      resourceAudience: null
    }
  });
  expect(response.status()).toBe(201);
  const body = await response.json();
  return {
    clientId,
    redirectUri,
    resourceId: body.client.id
  };
}

export async function disableOidcClientViaApi(request, adminAccessToken, resourceId) {
  const response = await request.post(
    `${resolveApiBase()}/api/v1/identity/oidc-clients/${resourceId}/disable`,
    {
      headers: { authorization: `Bearer ${adminAccessToken}` }
    }
  );
  expect(response.status()).toBe(200);
}

export async function expectAuthorizeRejectsDisabledClient(request, {
  apiBase = resolveApiBase(),
  clientId,
  redirectUri,
  scope = 'openid profile offline_access'
}) {
  const { challenge } = createPkcePair();
  const response = await request.get(
    buildAuthorizeUrl({
      apiBase,
      clientId,
      redirectUri,
      challenge,
      scope
    }),
    { maxRedirects: 0 }
  );
  expect([302, 303].includes(response.status())).toBeTruthy();
  const location = response.headers().location ?? '';
  expect(location).toContain('error=unauthorized_client');
}

export async function expectRefreshTokenRejects(request, {
  apiBase = resolveApiBase(),
  clientId,
  refreshToken,
  clientSecret = null
}) {
  const body = new URLSearchParams({
    grant_type: 'refresh_token',
    refresh_token: refreshToken,
    client_id: clientId
  });
  if (clientSecret) {
    body.set('client_secret', clientSecret);
  }

  const response = await request.post(`${apiBase}/connect/token`, {
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    data: body.toString()
  });
  expect(response.ok()).toBeFalsy();
  const payload = await response.text();
  expect(payload).toMatch(/unauthorized_client|invalid_grant/u);
}

// 领取与兑换分开，负向 PKCE 测试必须使用尚未消费的授权码。
export async function requestAuthorizationCodeViaRequest(request, {
  apiBase,
  clientId,
  redirectUri,
  username,
  password,
  scope = 'openid profile'
}) {
  const state = randomBytes(16).toString('hex');
  const nonce = randomBytes(16).toString('hex');
  const { verifier, challenge } = createPkcePair();
  const authorizeGet = await request.get(
    buildAuthorizeUrl({
      apiBase,
      clientId,
      redirectUri,
      challenge,
      scope,
      extraParams: { state, nonce }
    }),
    { maxRedirects: 0 }
  );
  expect([200, 302, 303].includes(authorizeGet.status())).toBeTruthy();

  let code;
  if (authorizeGet.status() === 302 || authorizeGet.status() === 303) {
    const location = authorizeGet.headers().location;
    code = new URL(location, apiBase).searchParams.get('code');
    // 复用中心会话的重定向与提交登录表单一样，必须绑定本次授权请求。
    expect(new URL(location, apiBase).searchParams.get('state')).toBe(state);
  } else {
    const loginPage = await authorizeGet.text();
    const match = loginPage.match(/name="__RequestVerificationToken" value="([^"]+)"/u);
    expect(match).toBeTruthy();
    const form = new URLSearchParams({
      client_id: clientId,
      redirect_uri: redirectUri,
      response_type: 'code',
      scope,
      state,
      nonce,
      code_challenge: challenge,
      code_challenge_method: 'S256',
      username,
      password,
      __RequestVerificationToken: decodeHtmlAttribute(match[1])
    });
    const authorizePost = await request.post(`${apiBase}/connect/authorize`, {
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      data: form.toString(),
      maxRedirects: 0
    });
    expect(authorizePost.status()).toBe(302);
    const location = authorizePost.headers().location;
    code = new URL(location, apiBase).searchParams.get('code');
    expect(new URL(location, apiBase).searchParams.get('state')).toBe(state);
  }

  expect(code).toBeTruthy();
  return { code, verifier, state, nonce };
}

export async function runAuthorizationCodeFlowViaRequest(request, options) {
  const pending = await requestAuthorizationCodeViaRequest(request, options);
  const token = await exchangeAuthorizationCode(request, {
    ...options,
    code: pending.code,
    verifier: pending.verifier
  });
  return { ...pending, token };
}

export async function exchangeAuthorizationCode(request, {
  apiBase,
  clientId,
  redirectUri,
  code,
  verifier,
  clientSecret = null
}) {
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: redirectUri,
    client_id: clientId,
    code_verifier: verifier
  });
  if (clientSecret) {
    body.set('client_secret', clientSecret);
  }

  const response = await request.post(`${apiBase}/connect/token`, {
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    data: body.toString()
  });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

export async function signInThroughCenter(page, {
  username,
  password,
  expectLoginForm = true
}) {
  if (expectLoginForm) {
    await expect(page.getByRole('heading', { name: 'Identity Center' })).toBeVisible();
    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Password').fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();
  }
  await expect(page.getByTestId('oidc-authorized')).toBeVisible({ timeout: 30_000 });
}

export async function completeClientAuthorization(page, client, {
  username,
  password,
  expectLoginForm = true,
  extraAuthorizeParams = {},
  buttonId = 'sign-in'
}) {
  const apiBase = resolveApiBase();
  await page.goto(resolveRpUrl(client, apiBase));
  await expect(page.getByTestId('oidc-ready')).toBeVisible();
  if (Object.keys(extraAuthorizeParams).length > 0) {
    const { verifier, challenge } = createPkcePair();
    const state = randomBytes(16).toString('hex');
    const nonce = randomBytes(16).toString('hex');
    await page.evaluate(({ verifier, state, nonce }) => {
      sessionStorage.setItem('oidc.pkce.verifier', verifier);
      sessionStorage.setItem('oidc.pkce.state', state);
      sessionStorage.setItem('oidc.pkce.nonce', nonce);
    }, { verifier, state, nonce });
    const params = new URLSearchParams({
      client_id: client.clientId,
      redirect_uri: client.redirectUri,
      response_type: 'code',
      scope: 'openid profile',
      state,
      nonce,
      code_challenge: challenge,
      code_challenge_method: 'S256',
      ...extraAuthorizeParams
    });
    await page.goto(`${apiBase}/connect/authorize?${params.toString()}`);
  } else {
    await page.locator(`#${buttonId}`).click();
  }

  await signInThroughCenter(page, { username, password, expectLoginForm });
  const code = await page.evaluate(() => sessionStorage.getItem('oidc.auth.code'));
  expect(code).toBeTruthy();
  const verifier = await page.evaluate(() => sessionStorage.getItem('oidc.pkce.verifier'));
  const token = await exchangeAuthorizationCode(page.request, {
    apiBase,
    clientId: client.clientId,
    redirectUri: client.redirectUri,
    code,
    verifier,
    clientSecret: client.clientSecret ?? null
  });
  return { code, verifier, token };
}

export async function readAccessTokenFingerprint(tokenResponse) {
  const hash = createHash('sha256')
    .update(tokenResponse.access_token)
    .digest('hex');
  return hash.slice(0, 16);
}

export function decodeJwtClaim(jwt, claimName) {
  const parts = jwt.split('.');
  if (parts.length < 2) {
    return null;
  }
  const payload = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf8'));
  return payload[claimName] ?? null;
}

export async function expectMeEndpointAcceptsToken(request, accessToken) {
  const response = await request.get(`${resolveApiBase()}/api/v1/me`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  expect(response.status()).toBe(200);
}

export async function expectMeEndpointRejectsToken(request, accessToken) {
  const response = await request.get(`${resolveApiBase()}/api/v1/me`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  expect(response.status()).toBe(401);
}

export async function expectProtectedEndpointRejectsToken(request, accessToken, path) {
  const response = await request.get(`${resolveApiBase()}${path}`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  expect(response.status()).toBe(403);
  const body = await response.json();
  expect(body.code).toBe('authorization.permission_denied');
}

export async function listAvailableTenants(request, accessToken) {
  const response = await request.get(`${resolveApiBase()}/api/v1/tenancy/available`, {
    headers: { authorization: `Bearer ${accessToken}` }
  });
  expect(response.status()).toBe(200);
  return response.json();
}

export async function switchTenantContext(request, accessToken, tenantId) {
  const response = await request.put(`${resolveApiBase()}/api/v1/tenancy/context`, {
    headers: {
      authorization: `Bearer ${accessToken}`,
      'content-type': 'application/json'
    },
    data: { tenantId }
  });
  expect(response.status()).toBe(200);
  const body = await response.json();
  expect(typeof body.accessToken).toBe('string');
  expect(body.accessToken.length).toBeGreaterThan(0);
  return body;
}

export async function expectTokenEndpointRejectsWrongVerifier(request, {
  apiBase,
  client,
  code,
  verifier
}) {
  const wrongVerifier = createPkcePair().verifier;
  expect(wrongVerifier).not.toBe(verifier);
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: client.redirectUri,
    client_id: client.clientId,
    code_verifier: wrongVerifier
  });
  if (client.clientSecret) {
    body.set('client_secret', client.clientSecret);
  }

  const response = await request.post(`${apiBase}/connect/token`, {
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    data: body.toString()
  });
  expect(response.status()).toBe(400);
  const payload = await response.json();
  expect(payload.error).toBe('invalid_grant');
}

export async function expectTokenEndpointRejectsInvalidCode(request, {
  apiBase,
  client,
  code = 'invalid-authorization-code'
}) {
  const { verifier } = createPkcePair();
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: client.redirectUri,
    client_id: client.clientId,
    code_verifier: verifier
  });
  if (client.clientSecret) {
    body.set('client_secret', client.clientSecret);
  }

  const response = await request.post(`${apiBase}/connect/token`, {
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    data: body.toString()
  });
  expect(response.ok()).toBeFalsy();
  const payload = await response.text();
  expect(payload).toContain('error');
}
