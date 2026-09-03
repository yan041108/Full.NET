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

test('Host 管理员可从真实 API 加载公告列表', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '公告动作权限切片仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /公告管理/);

  const announcementsView = page.locator('.host-announcements-view');
  await expect(
    announcementsView.getByRole('heading', { name: '公告管理', exact: true })
  ).toBeVisible();
  await expect(
    announcementsView.getByRole('columnheader', { name: '标题', exact: true })
  ).toBeVisible();
});

test('受限 Host 账号访问公告 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '公告动作权限切片仅验证 Vue 管理端'
  );

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/notifications/host-announcements?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /公告管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'notifications/host-announcements'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
