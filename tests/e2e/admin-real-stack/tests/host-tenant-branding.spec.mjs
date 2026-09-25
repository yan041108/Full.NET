import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  enterDevelopmentTenant,
  enterTenantAccessToken,
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

test('Host 管理员可通过 API 读写种子租户品牌（清单 22）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const tenant = await findSeedTenantViaApi(request, clientKind, 'local', accessToken);
  const authHeaders = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };

  const getResponse = await request.get(
    `${apiBaseUrl}/api/v1/tenancy/tenants/${tenant.id}/branding`,
    { headers: authHeaders }
  );
  expect(getResponse.ok()).toBeTruthy();
  const current = await getResponse.json();
  expect(current.tenantId).toBe(tenant.id);

  const stamp = `E2E22-${Date.now().toString(36)}`;
  const putResponse = await request.put(
    `${apiBaseUrl}/api/v1/tenancy/tenants/${tenant.id}/branding`,
    {
      headers: { ...authHeaders, 'Content-Type': 'application/json' },
      data: {
        systemTitle: stamp,
        contactPhone: current.contactPhone,
        contactEmail: current.contactEmail,
        contactAddress: current.contactAddress,
        copyright: current.copyright,
        version: current.version
      }
    }
  );
  expect(putResponse.ok()).toBeTruthy();
  const updated = await putResponse.json();
  expect(updated.systemTitle).toBe(stamp);

  const tenantToken = await enterTenantAccessToken(request, clientKind, accessToken);
  const runtimeResponse = await request.get(`${apiBaseUrl}/api/v1/tenancy/branding/current`, {
    headers: { Authorization: `Bearer ${tenantToken}`, Origin: origin }
  });
  expect(runtimeResponse.ok()).toBeTruthy();
  const runtime = await runtimeResponse.json();
  expect(runtime.systemTitle).toBe(stamp);
});

test('进入租户后可打开品牌设置页（清单 22）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  if (clientKind !== 'vue') {
    test.skip();
  }

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await page.goto('/#/settings/tenant-branding');
  await expect(page.getByRole('heading', { name: '租户品牌', exact: true })).toBeVisible({
    timeout: 15_000
  });
});
