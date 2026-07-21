import { expect, test } from '@playwright/test';
import {
  adminOrigin,
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

test('Host 管理员可从真实 API 加载用户列表', async ({ page }) => {
  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /用户管理/ })).toBeVisible();
  await navigation.getByRole('link', { name: /用户管理/ }).click();

  await expect(page.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('admin', { exact: true }).first()).toBeVisible();
});

test('受限 Host 账号访问用户 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /用户管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/users'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
