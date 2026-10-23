import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginHostAdminAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test('个人归档通过真实 API 移出收件箱并保留未读事实，工作台组合公告与消息入口', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = { Authorization: `Bearer ${token}`, Origin: adminOrigin(clientKind) };
  const me = await request.get(`${apiBaseUrl}/api/v1/me`, { headers });
  expect(me.ok()).toBeTruthy();
  const title = `archive-${Date.now()}`;
  const sent = await request.post(`${apiBaseUrl}/api/v1/notifications/host-inbox-messages`, {
    headers, data: { recipientUserId: (await me.json()).id, title, content: '归档不意味着已读或业务撤回。' }
  });
  expect(sent.status()).toBe(201);
  const message = await sent.json();
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /消息中心/);
  await expect(page.getByRole('heading', { name: '统一工作台', exact: true })).toBeVisible();
  const row = page.locator('.inbox-messages-view .el-table__row').filter({ hasText: title });
  await expect(row).toBeVisible();
  const archived = page.waitForResponse(response => response.request().method() === 'POST'
    && new URL(response.url()).pathname.endsWith(`/${message.id}/archive`));
  await row.getByTestId('inbox-messages-archive').click();
  expect((await archived).status()).toBe(200);
  await expect(row).toHaveCount(0);
  await page.getByRole('button', { name: '已归档', exact: true }).click();
  await expect(row).toBeVisible();
  await expect(row).toContainText('未读');
});

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可从真实 API 加载站内信列表', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '站内信动作权限切片仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /消息中心/);

  const inboxView = page.locator('.inbox-messages-view');
  await expect(
    inboxView.getByRole('heading', { name: '消息中心', exact: true })
  ).toBeVisible();
  await expect(inboxView.getByTestId('inbox-messages-list-title')).toContainText('收件箱');
});

test('受限 Host 账号访问站内信 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '站内信动作权限切片仅验证 Vue 管理端'
  );

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/notifications/my-inbox-messages?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /消息中心/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'notifications/inbox-messages'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
