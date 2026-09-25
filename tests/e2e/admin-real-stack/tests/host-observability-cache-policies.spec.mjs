import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  findSeedTenantViaApi,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const tenantResolutionEntry = 'tenancy.tenant-resolution';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可列出缓存策略并执行租户解析精确失效（清单 29）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };

  const listResponse = await request.get(`${apiBaseUrl}/api/v1/observability/cache-policies`, {
    headers
  });
  expect(listResponse.ok()).toBeTruthy();
  const policies = await listResponse.json();
  expect(Array.isArray(policies)).toBe(true);
  const tenantPolicy = policies.find(entry => entry.entryName === tenantResolutionEntry);
  expect(tenantPolicy).toBeTruthy();
  expect(tenantPolicy.canInvalidate).toBe(true);

  const serialized = JSON.stringify(policies);
  expect(serialized).not.toMatch(/connectionstring/i);
  expect(serialized).not.toMatch(/password\s*=/i);

  const detailResponse = await request.get(
    `${apiBaseUrl}/api/v1/observability/cache-policies/${encodeURIComponent(tenantResolutionEntry)}`,
    { headers }
  );
  expect(detailResponse.ok()).toBeTruthy();

  const tenant = await findSeedTenantViaApi(request, clientKind, 'local', accessToken);
  const invalidateResponse = await request.post(
    `${apiBaseUrl}/api/v1/observability/cache-policies/${encodeURIComponent(tenantResolutionEntry)}/invalidations`,
    {
      headers: { ...headers, 'Content-Type': 'application/json' },
      data: {
        operationKey: 'by-tenant',
        parameters: {
          tenantId: tenant.id,
          domain: 'localhost'
        },
        scope: 'all_layers_synchronous'
      }
    }
  );
  expect(invalidateResponse.ok()).toBeTruthy();
  const result = await invalidateResponse.json();
  expect(result.entryName).toBe(tenantResolutionEntry);
  expect(Array.isArray(result.invalidatedTargets)).toBe(true);
  expect(result.invalidatedTargets.length).toBeGreaterThan(0);
});

test('Vue 缓存管理页可查看已登记策略（清单 29）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '缓存管理页仅验收 Vue');
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /缓存管理/);

  const view = page.locator('.observability-cache-policies');
  await expect(view.getByRole('heading', { name: '缓存管理', exact: true })).toBeVisible({
    timeout: 15_000
  });
  await expect(view.getByText(tenantResolutionEntry, { exact: true })).toBeVisible();
  await view.getByText(tenantResolutionEntry, { exact: true }).click();
  await expect(view.getByRole('heading', { name: tenantResolutionEntry, exact: true })).toBeVisible();
});

test('受限 Host 账号访问缓存策略 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(`${apiBaseUrl}/api/v1/observability/cache-policies`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  expect(response.status()).toBe(403);
  expect((await response.json()).code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /缓存管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'observability/cache-policies'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
