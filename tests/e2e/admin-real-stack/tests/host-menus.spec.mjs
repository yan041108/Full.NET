import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
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

test('Host 管理员可从真实 API 加载菜单树表', async ({ page }) => {
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /菜单管理/);

  await expect(page.getByRole('heading', { name: '菜单管理', exact: true })).toBeVisible();
  await expect(page.getByTestId('menus-tree-table')).toBeVisible();
  await expect(page.getByText('工作台', { exact: true })).toBeVisible();
  await expect(page.locator('.el-pagination')).toHaveCount(0);
  await expect(page.getByRole('button', { name: '全部展开' })).toBeVisible();
});

test('受限 Host 账号访问菜单 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/menus?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /菜单管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/menus'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
