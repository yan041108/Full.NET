import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import {
  getMyTodo,
  openTodo,
  post,
  publishApprovalAssets,
  startInstance
} from './support/workflow-approval-fixtures.mjs';

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

test('已办历史详情展示真实审批快照并保持只读', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作流待办仅在 Vue 交付线验收');
  test.setTimeout(120_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const instance = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'history detail real-stack evidence'
  );
  const todo = await getMyTodo(request, clientKind, accessToken, instance.id);
  await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/todos/${todo.id}/approve`,
    {
      expectedRevision: todo.revision,
      fieldPatch: { decision: 'approved history snapshot' },
      comment: 'complete a real todo before opening its history detail',
      idempotencyKey: crypto.randomUUID()
    }
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我的待办/, '工作流');
  const view = page.locator('.workflow-todos');
  const tabs = view.getByRole('tablist');
  await tabs.getByRole('tab', { name: '已办', exact: true }).click();
  await openTodo(page, instance, { definitionKey: assets.definitionKey });

  const snapshot = page.getByTestId('workflow-todo-history-snapshot');
  await expect(snapshot).toBeVisible();
  await expect(snapshot).toContainText('同意');
  await expect(page.getByTestId('workflow-form-renderer')).toBeVisible();
  await expect(page.getByTestId('workflow-todo-approve')).toHaveCount(0);
  await expect(page.getByTestId('workflow-todo-reject')).toHaveCount(0);
});
