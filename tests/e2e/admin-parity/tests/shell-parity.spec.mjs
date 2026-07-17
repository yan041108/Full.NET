import { expect, test } from '@playwright/test';

test('管理端壳暴露可审计的实现标识和公共能力', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  await page.goto('/');

  await expect(page).toHaveTitle(/Full\.NET/);
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  await expect(page.getByRole('button', { name: '检查会话' })).toBeVisible();
  await expect(page.getByText('星云科技', { exact: true })).toBeVisible();
  await expect(page.getByText('活跃租户', { exact: true })).toBeVisible();
  await expect(page.locator(`[data-client-kind="${clientKind}"]`)).toBeVisible();
});

test('403 状态页在两套管理端保持相同关键语义', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await page.goto('/#/403');

  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByText('没有访问权限', { exact: true })).toBeVisible();
});

test('ProblemDetails 错误码和 TraceId 在两套管理端一致呈现', async ({ page }) => {
  await mockAuthenticatedSession(page, { probeDenied: true });
  await page.goto('/');
  await page.getByTestId('load-current-user').click();

  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');
  await expect(page.getByTestId('trace-id')).toHaveText('trace-admin-parity');
});

test('刷新失败后可登录、进入控制台并安全退出', async ({ page }) => {
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      status: 401,
      code: 'identity.refresh_missing',
      title: '刷新会话不存在'
    })
  }));
  await page.route('**/api/v1/auth/login', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(tokenResponse())
  }));
  await page.route('**/api/v1/auth/logout', route => route.fulfill({ status: 204 }));
  await page.route('**/api/v1/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(currentUserResponse())
  }));

  await page.goto('/');
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await page.getByLabel('账号').fill('admin');
  await page.getByLabel('密码').fill('FullNet!2026Secure');
  await page.getByRole('button', { name: '进入控制台' }).click();

  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: '退出登录' }).click();
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
});

async function mockAuthenticatedSession(page, options = {}) {
  let meCalls = 0;
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(tokenResponse())
  }));
  await page.route('**/api/v1/me', route => {
    meCalls += 1;
    if (options.probeDenied && meCalls > 1) {
      return route.fulfill({
        status: 403,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 403,
          code: 'authorization.denied',
          title: '没有访问权限',
          traceId: 'trace-admin-parity'
        })
      });
    }

    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(currentUserResponse())
    });
  });
}

function tokenResponse() {
  return {
    accessToken: 'e2e-access-token',
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUserResponse() {
  return {
    id: 'e2e-user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    scope: 'host',
    permissions: [],
    sessionId: 'e2e-session-id'
  };
}
