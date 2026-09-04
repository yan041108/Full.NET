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
const adminUsername = process.env.FULLNET_E2E_USERNAME ?? 'admin';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载在线会话列表', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /在线用户/);

  const onlineSessionsView = clientKind === 'layui'
    ? page.locator('[data-route-view="online-sessions"]')
    : page.locator('.online-sessions-view');

  await expect(onlineSessionsView.getByRole('heading', { name: '在线用户', exact: true })).toBeVisible();
  await expect(onlineSessionsView.getByRole('cell', {
    name: new RegExp(` ${adminUsername}$`)
  }).first()).toBeVisible({
    timeout: 15_000
  });
});

test('受限 Host 账号访问在线会话 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/online-sessions?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /在线用户/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'identity/online-sessions'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
