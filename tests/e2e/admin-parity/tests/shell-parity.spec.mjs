import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.route('**/api/v1/me', async (route) => {
    await route.fulfill({
      status: 403,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        status: 403,
        code: 'authorization.denied',
        title: '没有访问权限',
        traceId: 'trace-admin-parity'
      })
    });
  });
});

test('管理端壳暴露可审计的实现标识和公共能力', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await page.goto('/');

  await expect(page).toHaveTitle(/Full\.NET/);
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  await expect(page.getByRole('button', { name: '检查会话' })).toBeVisible();
  await expect(page.getByText('星云科技', { exact: true })).toBeVisible();
  await expect(page.getByText('活跃租户', { exact: true })).toBeVisible();
  await expect(page.locator(`[data-client-kind="${clientKind}"]`)).toBeVisible();
});

test('403 状态页在两套管理端保持相同关键语义', async ({ page }) => {
  await page.goto('/#/403');

  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByText('没有访问权限', { exact: true })).toBeVisible();
});

test('ProblemDetails 错误码和 TraceId 在两套管理端一致呈现', async ({ page }) => {
  await page.goto('/');
  await page.getByTestId('load-current-user').click();

  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');
  await expect(page.getByTestId('trace-id')).toHaveText('trace-admin-parity');
});
