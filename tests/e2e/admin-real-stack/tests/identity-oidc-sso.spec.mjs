import { expect, test } from '@playwright/test';
import {
  CENTER_COOKIE_NAME,
  OIDC_CLIENT_A,
  OIDC_CLIENT_B,
  completeClientAuthorization,
  expectMeEndpointAcceptsToken,
  expectMeEndpointRejectsToken,
  readAccessTokenFingerprint,
  resolveApiBase,
  resolveRpUrl
} from './support/identity-oidc-fixtures.mjs';

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
    const response = await page.goto(
      `${apiBase}/connect/authorize?client_id=${OIDC_CLIENT_A.clientId}`
      + '&redirect_uri=http%3A%2F%2Fevil.example%2Fcallback'
      + '&response_type=code&scope=openid%20profile&state=state&nonce=nonce'
      + '&code_challenge=challenge&code_challenge_method=S256'
    );
    expect(response?.status()).toBeGreaterThanOrEqual(400);
  });
});