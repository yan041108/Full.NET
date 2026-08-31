import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  enterTenantAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginTenantAdminAccessToken,
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
  tenantWorkflowReadOnlyTodoPermissions
} from './support/workflow-approval-fixtures.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('租户管理员可在 Vue 中发起流程并完成同意、驳回与并发 409', async ({
  page
}, testInfo) => {
  test.setTimeout(180_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const request = page.request;
  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  const accessToken = await readLiveAccessToken(page);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const approved = await startFromDefinitionsPage(page, assets, 'tenant approved');
  const rejected = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'tenant rejected'
  );
  const concurrent = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'tenant concurrent approval'
  );
  const concurrentTodo = await getMyTodo(request, clientKind, accessToken, concurrent.id);

  await clickMainNavLink(page, /我的待办/, '工作流');
  await openTodoAndAct(page, approved.id, 'approved', 'approve');
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, approved.id)).statusKey
  ).toBe('completed');

  await openTodoAndAct(page, rejected.id, 'rejected', 'reject');
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, rejected.id)).statusKey
  ).toBe('rejected');

  await openTodo(page, concurrent.id);
  await fillDecision(page, 'stale decision');
  await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/todos/${concurrentTodo.id}/approve`,
    {
      expectedRevision: 1,
      fieldPatch: { decision: 'authoritative decision' },
      comment: 'completed concurrently',
      idempotencyKey: crypto.randomUUID()
    }
  );
  const conflictResponse = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/workflow/todos/${concurrentTodo.id}/approve`)
    && response.request().method() === 'POST'
  );
  await page.getByTestId('workflow-todo-approve').click();
  expect((await conflictResponse).status()).toBe(409);
  await expect(page.locator('.art-inline-alert')).toContainText('workflow.revision.conflict');
  await expect(page.getByTestId('workflow-todo-approve')).toHaveCount(0);
  await expect(page.getByTestId('workflow-todo-reject')).toHaveCount(0);
  await expect(page.getByRole('row').filter({ hasText: concurrent.id })).toHaveCount(0);
});

test('租户直接提交只读、隐藏或未知字段 Patch 返回 422 且不推进待办', async ({
  request
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const assets = await publishApprovalAssets(request, clientKind, accessToken);
  const instance = await startInstance(
    request,
    clientKind,
    accessToken,
    assets.versionId,
    'tenant dangerous patch'
  );
  const todo = await getMyTodo(request, clientKind, accessToken, instance.id);
  await assertDangerousPatchesReturn422(request, clientKind, accessToken, todo.id);
  await expect.poll(async () =>
    (await getInstance(request, clientKind, accessToken, instance.id)).statusKey
  ).toBe('active');
});

test('租户内只有待办读取权限时直接 API 返回 403', async ({
  playwright
}, testInfo) => {
  test.setTimeout(120_000);
  const clientKind = testInfo.project.metadata.clientKind;
  await withIsolatedApi(playwright, async request => {
    const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
      permissionCodes: tenantWorkflowReadOnlyTodoPermissions
    });
    const hostToken = await loginAccessTokenWithPassword(
      request,
      clientKind,
      limited.username,
      limited.password
    );
    const accessToken = await enterTenantAccessToken(request, clientKind, hostToken);
    const assets = await publishApprovalAssets(request, clientKind, accessToken);
    const instance = await startInstance(
      request,
      clientKind,
      accessToken,
      assets.versionId,
      'restricted tenant approval'
    );
    const todo = await getMyTodo(request, clientKind, accessToken, instance.id);
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
});

/**
 * 从 Vue Pinia 会话读取当前内存中的 Access Token。
 * 必须在进入租户的 UI 稳定之后读取，避免用已轮换的上下文切换响应令牌去调 API。
 */
async function readLiveAccessToken(page) {
  const token = await page.evaluate(() => {
    const app = document.querySelector('#app')?.__vue_app__;
    const pinia = app?.config?.globalProperties?.$pinia;
    if (!pinia?._s) {
      return undefined;
    }

    for (const store of pinia._s.values()) {
      if (typeof store.readAccessToken === 'function') {
        return store.readAccessToken();
      }
    }

    return undefined;
  });
  expect(typeof token).toBe('string');
  expect(token.length).toBeGreaterThan(0);
  return token;
}

async function withIsolatedApi(playwright, run) {
  const request = await playwright.request.newContext();
  try {
    return await run(request);
  } finally {
    await request.dispose();
  }
}
