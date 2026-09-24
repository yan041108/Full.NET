import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可切换待办与已办历史页签', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作流待办仅在 Vue 交付线验收');
  test.setTimeout(60_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我的待办/, '工作流');

  const view = page.locator('.workflow-todos');
  await expect(view.getByRole('heading', { name: '我的工作流待办', exact: true })).toBeVisible();
  const tabs = view.getByRole('tablist');
  const historyResponsePromise = page.waitForResponse(response =>
    response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/workflow/todos/mine/history'
  );
  await tabs.getByRole('tab', { name: '已办', exact: true }).click();
  const historyResponse = await historyResponsePromise;
  expect(historyResponse.status()).toBe(200);
  const history = await historyResponse.json();
  await expect(tabs.getByRole('tab', { name: '已办', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(view.getByTestId('workflow-todo-result-action-filter')).toBeVisible();
  if (history.items.length === 0) {
    await expect(view.getByText('当前没有已办记录', { exact: true })).toBeVisible();
  } else {
    await expect(view.locator('.workflow-todos__table .el-table__row')).toHaveCount(history.items.length);
  }

  await tabs.getByRole('tab', { name: '待办', exact: true }).click();
  await expect(tabs.getByRole('tab', { name: '待办', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(view.getByTestId('workflow-todo-result-action-filter')).toHaveCount(0);
});
