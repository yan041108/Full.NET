import { expect, test } from '@playwright/test';
import {
  CENTER_COOKIE_NAME,
  OIDC_CLIENT_A,
  OIDC_CLIENT_B,
  buildAuthorizeUrl,
  completeClientAuthorization,
  createPkcePair,
  expectMeEndpointAcceptsToken,
  expectMeEndpointRejectsToken,
  expectProtectedEndpointRejectsToken,
  expectTokenEndpointRejectsInvalidCode,
  listAvailableTenants,
  readAccessTokenFingerprint,
  resolveApiBase,
  resolveRpUrl,
  switchTenantContext
} from './support/identity-oidc-fixtures.mjs';
import {
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
});