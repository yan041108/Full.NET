import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  findSeedTenantViaApi,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：默认关闭注册与公开注册方式（清单 48）', async ({ request }, testInfo) => {
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
  expect(policy.registrationMode).toBe(0);
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
  expect(publicProblem.code).toBe('identity.registration.disabled');

  const registerResponse = await request.post(`${apiBaseUrl}/api/v1/identity/register`, {
    headers: { Origin: origin, 'Content-Type': 'application/json' },
    data: {
      email: 'e2e48@example.com',
      displayName: 'E2E 48',
      password: 'FullNet!2026Secure',
      challengeId: '00000000-0000-4000-8000-000000000099',
      challengeCode: '000000'
    }
  });
  expect(registerResponse.status()).toBe(403);
  const registerProblem = await registerResponse.json();
  expect(registerProblem.code).toBe('identity.registration.disabled');
});

test('UI：注册策略与注册方式管理页（清单 48，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '注册方式页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /注册方式/);

  await expect(page.getByRole('heading', { name: '注册方式', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('注册策略', { exact: true })).toBeVisible();
  await expect(page.getByText('默认关闭公开注册', { exact: true })).toBeVisible();
  await expect(page.getByTestId('registration-policy-toggle')).toBeVisible();
  await expect(page.getByTestId('registration-ways-action-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
