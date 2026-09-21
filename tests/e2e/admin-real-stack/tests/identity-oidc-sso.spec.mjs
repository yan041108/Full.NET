import { expect, test } from '@playwright/test';
import {
  CENTER_COOKIE_NAME,
  EXTERNAL_OIDC_REDIRECT_URI,
  OIDC_CLIENT_A,
  OIDC_CLIENT_B,
  buildAuthorizeUrl,
  completeClientAuthorization,
  createExternalOidcClientViaApi,
  createPkcePair,
  disableOidcClientViaApi,
  expectAuthorizeRejectsDisabledClient,
  expectMeEndpointAcceptsToken,
  expectMeEndpointRejectsToken,
  expectProtectedEndpointRejectsToken,
  expectRefreshTokenRejects,
  expectTokenEndpointRejectsInvalidCode,
  expectTokenEndpointRejectsWrongVerifier,
  listAvailableTenants,
  decodeJwtClaim,
  readAccessTokenFingerprint,
  resolveApiBase,
  resolveRpUrl,
  requestAuthorizationCodeViaRequest,
  runAuthorizationCodeFlowViaRequest,
  switchTenantContext
} from './support/identity-oidc-fixtures.mjs';
import {
  loginHostAdminAccessToken,
  prepareHostUserCredentialsForOidc,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const apiBase = resolveApiBase();

test.describe('Identity OIDC browser SSO', () => {
  test('client A 登录后 client B 在同一会话中免密完成授权', async ({ page }) => {
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await expectMeEndpointAcceptsToken(page.request, clientA.token.access_token);

    await page.goto(resolveRpUrl(OIDC_CLIENT_B));
    await page.locator('#sign-in').click();
    await expect(page.getByTestId('oidc-authorized')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('heading', { name: 'Identity Center' })).toHaveCount(0);
  });

  test('仅保留 RP 辅助 Cookie 时仍要求中心登录', async ({ page, context }) => {
    await context.clearCookies();
    await context.addCookies([
      {
        name: 'rp-auxiliary',
        value: 'not-a-center-session',
        url: OIDC_CLIENT_A.origin
      }
    ]);
    await page.goto(resolveRpUrl(OIDC_CLIENT_A));
    await page.locator('#sign-in').click();
    await expect(page.getByRole('heading', { name: 'Identity Center' })).toBeVisible();
  });

  test('prompt=login 会强制重新认证', async ({ page }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await page.goto(resolveRpUrl(OIDC_CLIENT_B));
    await page.locator('#sign-in-prompt-login').click();
    await expect(page.getByRole('heading', { name: 'Identity Center' })).toBeVisible();
  });

  test('prompt=none 在无中心会话时返回 login_required', async ({ page }) => {
    await page.goto(resolveRpUrl(OIDC_CLIENT_A));
    await page.locator('#sign-in-prompt-none').click();
    await expect(page.getByTestId('oidc-error')).toHaveText('login_required');
  });

  test('A/B 客户端令牌彼此独立且可访问业务 API', async ({ page }) => {
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    const clientB = await completeClientAuthorization(page, OIDC_CLIENT_B, {
      username,
      password,
      expectLoginForm: false
    });
    const fingerprintA = await readAccessTokenFingerprint(clientA.token);
    const fingerprintB = await readAccessTokenFingerprint(clientB.token);
    expect(fingerprintA).not.toEqual(fingerprintB);
    await expectMeEndpointAcceptsToken(page.request, clientA.token.access_token);
    await expectMeEndpointAcceptsToken(page.request, clientB.token.access_token);
  });

  test('ID Token 不能访问业务 API', async ({ page }) => {
    const result = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await expectMeEndpointRejectsToken(page.request, result.token.id_token);
  });

  test('中心会话 Cookie 保持 HttpOnly 且 SameSite=Lax', async ({ page, context }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    const cookies = await context.cookies(apiBase);
    const centerCookie = cookies.find(cookie => cookie.name === CENTER_COOKIE_NAME);
    expect(centerCookie).toBeTruthy();
    expect(centerCookie.httpOnly).toBeTruthy();
    expect(centerCookie.sameSite).toBe('Lax');
  });

  test('无效 redirect_uri 不会发放授权码', async ({ page }) => {
    const { challenge } = createPkcePair();
    const response = await page.goto(
      buildAuthorizeUrl({
        apiBase,
        clientId: OIDC_CLIENT_A.clientId,
        redirectUri: 'http://evil.example/callback',
        challenge
      })
    );
    expect(response?.status()).toBeGreaterThanOrEqual(400);
  });

  test('未知 client_id 不会发放授权码', async ({ page }) => {
    const { challenge } = createPkcePair();
    const response = await page.goto(
      buildAuthorizeUrl({
        apiBase,
        clientId: 'e2e-oidc-unknown-client',
        redirectUri: OIDC_CLIENT_A.redirectUri,
        challenge
      })
    );
    expect(response?.status()).toBeGreaterThanOrEqual(400);
  });

  test('未注册 scope 不会发放授权码', async ({ page }) => {
    const { challenge } = createPkcePair();
    const response = await page.goto(
      buildAuthorizeUrl({
        apiBase,
        clientId: OIDC_CLIENT_A.clientId,
        redirectUri: OIDC_CLIENT_A.redirectUri,
        challenge,
        scope: 'orders.read'
      })
    );
    expect(response?.status()).toBeGreaterThanOrEqual(400);
  });

  test('畸形 Bearer 不能访问业务 API', async ({ request }) => {
    await expectMeEndpointRejectsToken(request, 'not-a-jwt');
  });

  test('无效授权码换票会被拒绝', async ({ request }) => {
    await expectTokenEndpointRejectsInvalidCode(request, {
      apiBase,
      client: OIDC_CLIENT_A
    });
  });

  test('外部 OIDC client 令牌不能访问身份管理 API', async ({ request }) => {
    const adminToken = await loginHostAdminAccessToken(request, 'vue');
    const clientId = `e2e-ext-${Date.now().toString(36)}`;
    await createExternalOidcClientViaApi(request, adminToken, {
      clientId,
      redirectUri: EXTERNAL_OIDC_REDIRECT_URI
    });
    const credentials = await prepareHostUserCredentialsForOidc(
      request,
      'vue',
      username,
      password
    );
    const { token } = await runAuthorizationCodeFlowViaRequest(request, {
      apiBase,
      clientId,
      redirectUri: EXTERNAL_OIDC_REDIRECT_URI,
      username: credentials.username,
      password: credentials.password
    });
    await expectMeEndpointAcceptsToken(request, token.access_token);
    await expectProtectedEndpointRejectsToken(
      request,
      token.access_token,
      '/api/v1/identity/users?page=1&pageSize=1'
    );
    await expectProtectedEndpointRejectsToken(
      request,
      token.access_token,
      '/api/v1/identity/online-sessions?page=1&pageSize=1'
    );
  });

  test('无权 first-party OIDC 用户可访问 profile 但不能访问用户目录', async ({
    page,
    request
  }) => {
    const limited = await provisionLimitedHostUserViaApi(request, 'vue', {
      permissionCodes: ['platform.dashboard.read']
    });
    const credentials = await prepareHostUserCredentialsForOidc(
      request,
      'vue',
      limited.username,
      limited.password
    );
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username: credentials.username,
      password: credentials.password,
      expectLoginForm: true
    });
    await expectMeEndpointAcceptsToken(page.request, clientA.token.access_token);
    await expectProtectedEndpointRejectsToken(
      page.request,
      clientA.token.access_token,
      '/api/v1/identity/users?page=1&pageSize=1'
    );
    await expectProtectedEndpointRejectsToken(
      page.request,
      clientA.token.access_token,
      '/api/v1/identity/oidc-clients?page=1&pageSize=1'
    );
  });

  test('OIDC 访问令牌可切换租户上下文并轮换旧令牌', async ({ page }) => {
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    const hostToken = clientA.token.access_token;
    await expectMeEndpointAcceptsToken(page.request, hostToken);

    const tenants = await listAvailableTenants(page.request, hostToken);
    const localTenant = tenants.find(entry => entry.identifier === 'local') ?? tenants[0];
    expect(localTenant?.id).toBeTruthy();

    const switched = await switchTenantContext(page.request, hostToken, localTenant.id);
    expect(switched.context?.tenantId).toBe(localTenant.id);
    await expectMeEndpointAcceptsToken(page.request, switched.accessToken);
    await expectMeEndpointRejectsToken(page.request, hostToken);

    const restored = await switchTenantContext(page.request, switched.accessToken, null);
    expect(restored.context?.tenantId ?? null).toBeNull();
    expect(restored.context?.scope).toBe('host');
    await expectMeEndpointAcceptsToken(page.request, restored.accessToken);
    await expectMeEndpointRejectsToken(page.request, switched.accessToken);
  });

  test('禁用外部 OIDC 客户端后上下文切换令牌被拒绝', async ({ request }) => {
    const adminToken = await loginHostAdminAccessToken(request, 'vue');
    const clientId = `e2e-ctx-gov-${Date.now().toString(36)}`;
    const created = await createExternalOidcClientViaApi(request, adminToken, {
      clientId,
      redirectUri: EXTERNAL_OIDC_REDIRECT_URI,
      scopes: ['openid', 'profile', 'offline_access'],
      isFirstParty: true
    });
    const credentials = await prepareHostUserCredentialsForOidc(
      request,
      'vue',
      username,
      password
    );
    const { token } = await runAuthorizationCodeFlowViaRequest(request, {
      apiBase,
      clientId: created.clientId,
      redirectUri: created.redirectUri,
      username: credentials.username,
      password: credentials.password,
      scope: 'openid profile offline_access'
    });
    const hostToken = token.access_token;
    expect(token.refresh_token).toBeTruthy();
    await expectMeEndpointAcceptsToken(request, hostToken);

    const tenants = await listAvailableTenants(request, adminToken);
    const localTenant = tenants.find(entry => entry.identifier === 'local') ?? tenants[0];
    expect(localTenant?.id).toBeTruthy();

    const switched = await switchTenantContext(request, hostToken, localTenant.id);
    await expectMeEndpointAcceptsToken(request, switched.accessToken);

    await disableOidcClientViaApi(request, adminToken, created.resourceId);

    await expectAuthorizeRejectsDisabledClient(request, {
      apiBase,
      clientId: created.clientId,
      redirectUri: created.redirectUri
    });
    await expectRefreshTokenRejects(request, {
      apiBase,
      clientId: created.clientId,
      refreshToken: token.refresh_token
    });
    await expectMeEndpointRejectsToken(request, hostToken);
    await expectMeEndpointRejectsToken(request, switched.accessToken);
  });

  test('prompt=none 在中心会话存在时静默授权', async ({ page }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await page.goto(resolveRpUrl(OIDC_CLIENT_B));
    await page.locator('#sign-in-prompt-none').click();
    await expect(page.getByTestId('oidc-authorized')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('heading', { name: 'Identity Center' })).toHaveCount(0);
  });

  test('max_age=0 在已有中心会话时强制重新认证', async ({ page }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await completeClientAuthorization(page, OIDC_CLIENT_B, {
      username,
      password,
      expectLoginForm: true,
      extraAuthorizeParams: { max_age: '0' }
    });
  });

  test('无效防伪令牌不能建立中心会话', async ({ request }) => {
    const { challenge } = createPkcePair();
    const authorizeGet = await request.get(
      buildAuthorizeUrl({
        apiBase,
        clientId: OIDC_CLIENT_A.clientId,
        redirectUri: OIDC_CLIENT_A.redirectUri,
        challenge
      }),
      { maxRedirects: 0 }
    );
    expect(authorizeGet.status()).toBe(200);
    const form = new URLSearchParams({
      client_id: OIDC_CLIENT_A.clientId,
      redirect_uri: OIDC_CLIENT_A.redirectUri,
      response_type: 'code',
      scope: 'openid profile',
      state: 'csrf-state',
      nonce: 'csrf-nonce',
      code_challenge: challenge,
      code_challenge_method: 'S256',
      username,
      password,
      __RequestVerificationToken: 'invalid-token'
    });
    const authorizePost = await request.post(`${apiBase}/connect/authorize`, {
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
      data: form.toString(),
      maxRedirects: 0
    });
    expect(authorizePost.status()).toBeGreaterThanOrEqual(400);
  });

  test('state 篡改在 RP 回调页展示错误', async ({ page }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    // 保留真实回调授权码，只篡改 state；缺少 code 会进入 ready 分支而非回调校验。
    const callback = new URL(page.url());
    expect(callback.searchParams.get('code')).toBeTruthy();
    callback.searchParams.set('state', 'tampered');
    await page.evaluate(() => sessionStorage.removeItem('oidc.auth.code'));
    await page.goto(callback.href);
    await expect(page.getByTestId('oidc-state-mismatch')).toBeVisible();
    expect(await page.evaluate(() => sessionStorage.getItem('oidc.auth.code'))).toBeNull();
  });

  test('清除中心 Cookie 后 prompt=none 返回 login_required', async ({ page, context }) => {
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await context.clearCookies();
    await page.goto(resolveRpUrl(OIDC_CLIENT_B));
    await page.locator('#sign-in-prompt-none').click();
    await expect(page.getByTestId('oidc-error')).toHaveText('login_required');
  });

  test('A/B 客户端共享 sub 且 application_session_id 独立', async ({ page }) => {
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    const clientB = await completeClientAuthorization(page, OIDC_CLIENT_B, {
      username,
      password,
      expectLoginForm: false
    });
    const subA = decodeJwtClaim(clientA.token.access_token, 'sub');
    const subB = decodeJwtClaim(clientB.token.access_token, 'sub');
    expect(subA).toBeTruthy();
    expect(subA).toBe(subB);
    const sessionA = decodeJwtClaim(clientA.token.access_token, 'application_session_id');
    const sessionB = decodeJwtClaim(clientB.token.access_token, 'application_session_id');
    expect(sessionA).toBeTruthy();
    expect(sessionB).toBeTruthy();
    expect(sessionA).not.toBe(sessionB);
  });

  test('错误的 code_verifier 无法兑换授权码', async ({ request }) => {
    const pending = await requestAuthorizationCodeViaRequest(request, {
      apiBase,
      clientId: OIDC_CLIENT_A.clientId,
      redirectUri: OIDC_CLIENT_A.redirectUri,
      username,
      password,
      scope: 'openid profile'
    });
    await expectTokenEndpointRejectsWrongVerifier(request, {
      apiBase,
      client: OIDC_CLIENT_A,
      code: pending.code,
      verifier: pending.verifier
    });
  });

  test('ID Token nonce 与授权请求一致', async ({ request }) => {
    const flow = await runAuthorizationCodeFlowViaRequest(request, {
      apiBase,
      clientId: OIDC_CLIENT_A.clientId,
      redirectUri: OIDC_CLIENT_A.redirectUri,
      username,
      password,
      scope: 'openid profile'
    });
    const idTokenNonce = decodeJwtClaim(flow.token.id_token, 'nonce');
    expect(idTokenNonce).toBeTruthy();
    expect(idTokenNonce).toBe(flow.nonce);
  });

  test('prompt=login 不会替换既有中心用户会话', async ({ page, request }) => {
    const clientA = await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    const limited = await provisionLimitedHostUserViaApi(request, 'vue', {
      permissionCodes: ['platform.dashboard.read']
    });
    const victimCredentials = await prepareHostUserCredentialsForOidc(
      request,
      'vue',
      limited.username,
      limited.password
    );
    const clientB = await completeClientAuthorization(page, OIDC_CLIENT_B, {
      username: victimCredentials.username,
      password: victimCredentials.password,
      expectLoginForm: true,
      buttonId: 'sign-in-prompt-login'
    });
    const subA = decodeJwtClaim(clientA.token.access_token, 'sub');
    const subB = decodeJwtClaim(clientB.token.access_token, 'sub');
    expect(subA).not.toBe(subB);
    await expectMeEndpointAcceptsToken(page.request, clientA.token.access_token);
    await expectMeEndpointAcceptsToken(page.request, clientB.token.access_token);
  });

  test('受限第三方 Cookie 场景在 CI 条件跳过', async ({ page, context, browserName }) => {
    test.skip(
      process.env.CI === 'true' && browserName === 'chromium',
      'V22：受限第三方 Cookie 依赖浏览器策略，CI Chromium 标注条件跳过。'
    );
    await completeClientAuthorization(page, OIDC_CLIENT_A, {
      username,
      password,
      expectLoginForm: true
    });
    await context.clearCookies({ name: CENTER_COOKIE_NAME });
    await page.goto(resolveRpUrl(OIDC_CLIENT_B));
    await page.locator('#sign-in-prompt-none').click();
    await expect(page.getByTestId('oidc-error')).toHaveText('login_required');
  });
});
