import { expect, test } from '@playwright/test';

const permissions = [
  'workflow.todos.read',
  'workflow.todos.approve',
  'workflow.todos.reject',
  'notifications.inbox.read',
  'notifications.inbox.mark_all_read'
];

const currentUser = {
  id: '00000000-0000-7000-8000-000000000081',
  username: 'e2e-uniapp',
  displayName: 'E2E UniApp',
  tenantId: null,
  actorScope: 'host',
  scope: 'host',
  isSuperAdministrator: false,
  passwordChangeRequired: false,
  permissions,
  sessionId: '00000000-0000-7000-8000-000000000082',
  preferredLocale: 'zh-CN',
  profileVersion: 1
};

function installApiMocks(page) {
  return page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url());
    const path = url.pathname;

    if (path === '/api/v1/auth/login' && route.request().method() === 'POST') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          accessToken: 'e2e-uniapp-access-token',
          tokenType: 'Bearer',
          expiresAtUtc: '2099-01-01T00:00:00Z'
        })
      });
      return;
    }

    if (path === '/api/v1/me') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(currentUser)
      });
      return;
    }

    if (path === '/api/v1/workflow/todos/mine') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 })
      });
      return;
    }

    if (path === '/api/v1/notifications/my-inbox-messages/unread-count') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ unreadCount: 0 })
      });
      return;
    }

    if (path.startsWith('/api/v1/notifications/my-inbox-messages')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 })
      });
      return;
    }

    await route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Not mocked', status: 404 })
    });
  });
}

test('H5：登录后待办与站内信链路（清单 81，微信小程序切片以 H5 代理验收）', async ({ page }) => {
  await installApiMocks(page);
  await page.goto('/#/pages/identity/login');

  const textboxes = page.getByRole('textbox');
  await textboxes.nth(0).fill('e2e-uniapp');
  await textboxes.nth(1).fill('secret');
  await page.getByTestId('login-submit').click();

  await expect(page.getByText('我的待办', { exact: true }).first()).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('当前没有待办任务。', { exact: true })).toBeVisible();

  await page.getByTestId('workflow-open-inbox').click();
  await expect(page.getByText('暂无站内信。', { exact: true })).toBeVisible({ timeout: 15_000 });
});
