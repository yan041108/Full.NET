import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';
import {
  apiHeaders,
  assertDangerousPatchesReturn422,
  fillDecision,
  getInstance,
  getMyTodo,
  openTodo,
  openTodoAndAct,
  post,
  publishApprovalAssets,
  startFromDefinitionsPage,
  startInstance,
  workflowReadOnlyTodoPermissions
} from './support/workflow-approval-fixtures.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('管理员可在 Vue 中发起流程并完成同意与驳回', async ({ page, request }, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);

  await loginAsHostAdmin(page);
  const approved = await startFromDefinitionsPage(page, assets, 'admin approved');
  const rejected = await startInstance(request, clientKind, accessToken, assets.versionId, 'admin rejected');

  await clickMainNavLink(page, /我的待办/, '工作流');
  await openTodoAndAct(page, approved.id, 'approved', 'approve');
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, approved.id)).statusKey
  ).toBe('completed');

  await openTodoAndAct(page, rejected.id, 'rejected', 'reject');
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, rejected.id)).statusKey
  ).toBe('rejected');
});

test('只有待办读取权限时动作按钮不进入 DOM 且直接 API 返回 403', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: workflowReadOnlyTodoPermissions
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const instance = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'restricted approval'
  );
  const todo = await getMyTodo(request, clientKind, accessToken, instance.id);

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /我的待办/, '工作流');
  await openTodo(page, instance.id);
  await expect(page.getByTestId('workflow-todo-approve')).toHaveCount(0);
  await expect(page.getByTestId('workflow-todo-reject')).toHaveCount(0);

  const bypass = await request.post(
    `${apiBaseUrl}/api/v1/workflow/todos/${todo.id}/approve`,
    {
      data: {
        expectedRevision: 1,
        fieldPatch: { decision: 'bypass' },
        comment: 'must be forbidden',
        idempotencyKey: crypto.randomUUID()
      },
      headers: apiHeaders(clientKind, accessToken)
    }
  );
  expect(bypass.status(), await bypass.text()).toBe(403);
  expect((await bypass.json()).code).toBe('authorization.permission_denied');
});

test('并发处理造成 409 后刷新权威待办并关闭过期动作', async ({
  page,
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const instance = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'concurrent approval'
  );
  const todo = await getMyTodo(request, clientKind, accessToken, instance.id);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我的待办/, '工作流');
  await openTodo(page, instance.id);
  await fillDecision(page, 'stale decision');

  await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/todos/${todo.id}/approve`,
    {
      expectedRevision: 1,
      fieldPatch: { decision: 'authoritative decision' },
      comment: 'completed concurrently',
      idempotencyKey: crypto.randomUUID()
    }
  );

  const conflictResponse = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/workflow/todos/${todo.id}/approve`)
    && response.request().method() === 'POST'
  );
  await page.getByTestId('workflow-todo-approve').click();
  expect((await conflictResponse).status()).toBe(409);
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page.getByTestId('workflow-todo-approve')).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: instance.id })).toHaveCount(0);
});

test('直接提交只读、隐藏或未知字段 Patch 返回 422 且不推进待办', async ({
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const instance = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'dangerous patch'
  );
  const todo = await getMyTodo(request, clientKind, accessToken, instance.id);
  await assertDangerousPatchesReturn422(request, clientKind, accessToken, todo.id);
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, instance.id)).statusKey
  ).toBe('active');
});
