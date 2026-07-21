import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  enterTenantAccessToken,
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

test('Host 管理员在租户上下文中可从真实 API 加载用户机构隶属列表', async ({ page }) => {
  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '进入租户' }).click();
  await expect(navigation.getByRole('link', { name: /用户机构隶属/ })).toBeVisible();
  await navigation.getByRole('link', { name: /用户机构隶属/ }).click();

  await expect(page.getByRole('heading', { name: '用户机构隶属', exact: true })).toBeVisible();
  await expect(page.getByText('尚无用户机构隶属', { exact: true })).toBeVisible();
});

test('受限 Host 账号在租户上下文中访问用户机构隶属 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const hostToken = await loginAccessToken(request, clientKind);
  const tenantToken = await enterTenantAccessToken(request, clientKind, hostToken);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/organization/user-units?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${tenantToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '进入租户' }).click();
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /用户机构隶属/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'organization/user-units'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
