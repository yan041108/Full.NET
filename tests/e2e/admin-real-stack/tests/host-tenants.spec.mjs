import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  createTenantPackageViaApi,
  findSeedTenantViaApi,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载租户列表', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /租户管理/);

  const tenantsView = clientKind === 'layui'
    ? page.locator('[data-route-view="tenants"]')
    : page.locator('.tenants-view');

  await expect(tenantsView.getByRole('heading', { name: '租户管理', exact: true })).toBeVisible();
  await expect(tenantsView.getByText('Full.NET Local', { exact: true })).toBeVisible();
  if (clientKind === 'vue') {
    await expect(tenantsView.getByRole('cell', { name: /local Full\.NET Local/u })).toBeVisible();
    await expect(tenantsView.getByRole('cell', { name: 'localhost', exact: true })).toBeVisible();
  } else {
    await expect(tenantsView.getByText(/local · localhost/u)).toBeVisible();
  }
});

test('Host 管理员可为种子租户分配套餐', async ({ page, request }, testInfo) => {
  test.setTimeout(60_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const packageCode = `e2e-rs-${Date.now().toString(36)}`;
  const packageName = `真实栈分配套餐 ${packageCode}`;
  const createdPackage = await createTenantPackageViaApi(request, clientKind, {
    code: packageCode,
    name: packageName
  });
  const localTenant = await findSeedTenantViaApi(request, clientKind);

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /租户管理/);

  const tenantsView = clientKind === 'layui'
    ? page.locator('[data-route-view="tenants"]')
    : page.locator('.tenants-view');
  const tenantRow = clientKind === 'vue'
    ? tenantsView.locator('.el-table__row').filter({ hasText: 'Full.NET Local' })
    : tenantsView.locator('article').filter({
        has: page.getByText('Full.NET Local', { exact: true })
      });
  await expect(tenantRow).toBeVisible();

  if (clientKind === 'vue') {
    await tenantRow.locator('.el-select').click();
    await page.getByRole('listbox').getByRole('option', { name: packageName, exact: true }).click();
  } else {
    await tenantRow.locator(`select[data-tenants-package="${localTenant.id}"]`)
      .selectOption(createdPackage.id);
  }

  const assignedPackageName = clientKind === 'vue' ? packageName : `套餐: ${packageName}`;
  await expect(tenantRow.getByText(assignedPackageName, { exact: true }))
    .toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号访问租户 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/tenancy/tenants?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /租户管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'tenants'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
