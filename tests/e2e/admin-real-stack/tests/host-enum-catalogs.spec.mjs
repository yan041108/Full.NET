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

test('Host 管理员可从真实 API 加载枚举常量目录', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /枚举常量/ })).toBeVisible();
  await navigation.getByRole('link', { name: /枚举常量/ }).click();

  const enumCatalogsView = clientKind === 'layui'
    ? page.locator('[data-route-view="enum-catalogs"]')
    : page.locator('.enum-catalogs-view');

  await expect(enumCatalogsView.getByRole('heading', { name: '枚举常量', exact: true })).toBeVisible();
  await expect(enumCatalogsView.getByText('settings.config_value_kind', { exact: true })).toBeVisible();
  await enumCatalogsView.getByRole('button', { name: '查看', exact: true }).click();
  await expect(enumCatalogsView.locator('code', { hasText: 'string' })).toBeVisible();
});

test('受限 Host 账号访问枚举目录 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs`,
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
  await expect(navigation.getByRole('link', { name: /枚举常量/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'settings/enum-catalogs'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
