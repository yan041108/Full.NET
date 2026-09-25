import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

function buildE2eProviderPayload(stamp) {
  return {
    providerKey: `e2e50-${stamp}`,
    displayName: `E2E50 IdP ${stamp}`,
    authority: 'https://login.e2e50.invalid/',
    clientId: 'e2e50-client',
    clientSecret: 'E2e50-Secret-Not-Returned',
    scopes: null,
    redirectPath: null,
    isEnabled: false
  };
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：OAuth 公开目录、回调 fail-closed、凭据不回显、我的绑定列表（清单 50）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const authHeaders = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const publicResponse = await request.get(`${apiBaseUrl}/api/v1/identity/oauth/providers`, {
    headers: { Origin: origin }
  });
  expect(publicResponse.ok()).toBeTruthy();
  const publicProviders = await publicResponse.json();
  expect(Array.isArray(publicProviders)).toBeTruthy();

  const missingAuthorize = await request.get(
    `${apiBaseUrl}/api/v1/identity/oauth/e2e50-missing/authorize?mode=login&returnUrl=/account/security`,
    { headers: { Origin: origin }, maxRedirects: 0 }
  );
  expect(missingAuthorize.status()).toBe(302);
  expect(missingAuthorize.headers().location).toContain('oauth_error=oauth_provider_unavailable');

  const bindAuthorize = await request.get(
    `${apiBaseUrl}/api/v1/identity/oauth/e2e50-missing/authorize?mode=bind&returnUrl=/account/security`,
    { headers: { Origin: origin }, maxRedirects: 0 }
  );
  expect(bindAuthorize.status()).toBe(302);
  expect(bindAuthorize.headers().location).toContain('oauth_error=');

  const callbackResponse = await request.get(`${apiBaseUrl}/api/v1/identity/oauth/callback`, {
    maxRedirects: 0
  });
  expect(callbackResponse.status()).toBe(302);
  expect(callbackResponse.headers().location).toContain('oauth_error=oauth_invalid_state');

  const linksResponse = await request.get(`${apiBaseUrl}/api/v1/identity/me/oauth-links`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(linksResponse.ok()).toBeTruthy();
  expect(Array.isArray(await linksResponse.json())).toBeTruthy();

  const stamp = Date.now().toString(36);
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/identity/oauth-providers`, {
    headers: authHeaders,
    data: buildE2eProviderPayload(stamp)
  });
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.clientSecret).toBeUndefined();
  expect(created.providerKey).toBe(`e2e50-${stamp}`);

  const deleteResponse = await request.delete(
    `${apiBaseUrl}/api/v1/identity/oauth-providers/${created.id}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(deleteResponse.status()).toBe(204);
});

test('UI：OAuth 提供程序与外部身份绑定（清单 50，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'OAuth 页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /OAuth 提供程序/);
  await expect(page.getByText('OAuth 提供程序', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });

  await page.goto('/#/account/security');
  await expect(page.getByRole('heading', { name: '安全设置', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('外部身份绑定', { exact: true })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
