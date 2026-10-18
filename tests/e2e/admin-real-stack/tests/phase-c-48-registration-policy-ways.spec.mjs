import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  findSeedTenantViaApi,
  loginAsHostAdmin,
  loginHostAdminAccessToken,
  trackUiAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：默认仅邀请注册与公开注册方式（清单 48）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const tenant = await findSeedTenantViaApi(request, clientKind, 'local', token);
  const authHeaders = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const policyResponse = await request.get(`${apiBaseUrl}/api/v1/identity/registration-policy`, {
    headers: authHeaders
  });
  expect(policyResponse.ok()).toBeTruthy();
  const policy = await policyResponse.json();
  expect(policy.registrationMode).toBe(1);
  expect(policy.isPublicRegistrationEnabled).toBe(false);

  const waysResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/registration-ways?page=1&pageSize=20`,
    { headers: authHeaders }
  );
  expect(waysResponse.ok()).toBeTruthy();

  const publicWaysResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/public/registration-ways?tenantId=${tenant.id}`,
    { headers: { Origin: origin } }
  );
  expect(publicWaysResponse.status()).toBe(403);
  const publicProblem = await publicWaysResponse.json();
  expect(publicProblem.code).toBe('identity.registration.public_disabled');

  const registerResponse = await request.post(`${apiBaseUrl}/api/v1/auth/register`, {
    headers: { Origin: origin, 'Content-Type': 'application/json' },
    data: {
      email: 'e2e48@example.com',
      displayName: 'E2E 48',
      password: 'FullNet!2026Secure',
      challengeId: '00000000-0000-4000-8000-000000000099',
      challengeCode: '000000'
    }
  });
  expect(registerResponse.status()).toBe(400);
  const registerProblem = await registerResponse.json();
  expect(registerProblem.code).toBe('identity.registration_invitation.invalid');
});

test('UI：注册策略与注册方式管理页（清单 48，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '注册方式页仅 Vue 交付线');
  test.setTimeout(120_000);

  const currentToken = trackUiAccessToken(page);
  const pageErrors = [];
  page.on('pageerror', error => pageErrors.push(error.message));
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /注册方式/);

  await expect(page.getByRole('heading', { name: '注册方式', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('注册策略', { exact: true })).toBeVisible();
  const token = currentToken();
  const headers = { Authorization: 'Bearer ' + token, Origin: adminOrigin('vue') };
  const policyUrl = apiBaseUrl + '/api/v1/identity/registration-policy';
  const originalResponse = await page.request.get(policyUrl, { headers });
  expect(originalResponse.ok()).toBeTruthy();
  const original = await originalResponse.json();
  const selector = page.getByTestId('registration-policy-mode');
  await expect(selector).toBeVisible();
  try {
    for (const [mode, label] of [[0, '关闭注册'], [2, '公开注册'], [1, '仅邀请注册']]) {
      await selector.locator('.el-select__wrapper').click();
      const responsePromise = page.waitForResponse(response => response.url().includes('/registration-policy') && response.request().method() === 'PUT');
      await page.getByRole('option', { name: label, exact: true }).click();
      const response = await responsePromise;
      expect(response.status()).toBe(200);
      const saved = await response.json();
      expect(saved.registrationMode).toBe(mode);
      expect(saved.isPublicRegistrationEnabled).toBe(mode === 2);
      await expect(page.getByTestId('registration-policy-current')).toHaveText(label);
      const invalid = await page.request.put(policyUrl, { headers, data: { registrationMode: 255, isPublicRegistrationEnabled: true, version: saved.version } });
      expect(invalid.status()).toBe(400);
      expect((await invalid.json()).code).toBe('validation.failed');
    }
  } finally {
    const currentResponse = await page.request.get(policyUrl, { headers });
    expect(currentResponse.ok()).toBeTruthy();
    const current = await currentResponse.json();
    const restore = await page.request.put(policyUrl, { headers, data: {
      registrationMode: original.registrationMode, isPublicRegistrationEnabled: original.isPublicRegistrationEnabled, version: current.version
    } });
    expect(restore.ok()).toBeTruthy();
  }
  await expect(page.getByTestId('registration-ways-action-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
  expect(pageErrors).toEqual([]);
});
