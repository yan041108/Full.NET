import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

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
    permissionCodes: [
      'identity.navigation.read',
      'platform.dashboard.read',
      'workflow.forms.read',
      'workflow.forms.create',
      'workflow.forms.publish',
      'workflow.definitions.read',
      'workflow.definitions.create',
      'workflow.definitions.publish',
      'workflow.instances.read',
      'workflow.instances.start',
      'workflow.todos.read'
    ]
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

async function startFromDefinitionsPage(page, assets, reason) {
  await clickMainNavLink(page, /工作流定义/, '工作流');
  const view = page.locator('.workflow-definitions');
  const row = view.getByRole('row').filter({ hasText: assets.definitionKey });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('workflow-definition-versions').click();
  await view.getByTestId('workflow-definition-start').click();
  await view.getByTestId('workflow-business-type').fill('admin.approval');
  await view.getByTestId('workflow-business-id').fill(crypto.randomUUID());
  const reasonField = view.locator('.workflow-form__field').filter({ hasText: 'reason' });
  await reasonField.locator('input').fill(reason);
  const startedResponse = page.waitForResponse(response =>
    response.url().endsWith('/api/v1/workflow/instances')
    && response.request().method() === 'POST'
  );
  await view.getByTestId('workflow-start-submit').click();
  const response = await startedResponse;
  expect(response.status(), await response.text()).toBe(201);
  return response.json();
}

async function openTodoAndAct(page, instanceId, decision, action) {
  await openTodo(page, instanceId);
  const reasonField = page.locator('.workflow-form__field').filter({ hasText: 'reason' });
  await expect(reasonField.locator('input')).toHaveAttribute('readonly', '');
  await expect(page.locator('.workflow-form__field').filter({ hasText: 'secret' }))
    .toHaveCount(0);
  await fillDecision(page, decision);
  const actionButton = page.getByTestId(`workflow-todo-${action}`);
  await actionButton.click();
  await expect(page.getByRole('row').filter({ hasText: instanceId })).toHaveCount(0);
}

async function openTodo(page, instanceId) {
  const row = page.getByRole('row').filter({ hasText: instanceId });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('workflow-todo-open').click();
  await expect(page.getByTestId('workflow-form-renderer')).toBeVisible();
}

async function fillDecision(page, value) {
  const decisionField = page.locator('.workflow-form__field').filter({ hasText: 'decision' });
  await decisionField.locator('input').fill(value);
}

async function publishApprovalAssets(request, clientKind, accessToken) {
  const stamp = `${Date.now().toString(36)}-${crypto.randomUUID()}`;
  const form = await post(request, clientKind, accessToken, '/api/v1/workflow/forms', {
    formKey: `admin.approval.${stamp}`,
    draft: {
      schemaVersion: 1,
      adapterVersion: 1,
      sections: [{
        sectionKey: 'main',
        fields: [
          { fieldKey: 'reason', fieldTypeKey: 'text', required: true, constraints: {} },
          { fieldKey: 'secret', fieldTypeKey: 'text', required: false, constraints: {} },
          { fieldKey: 'decision', fieldTypeKey: 'text', required: false, constraints: {} }
        ]
      }]
    }
  }, 201);
  const formVersion = await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/forms/${form.id}/publish`,
    { expectedRevision: form.draftRevision }
  );
  const definitionKey = `admin.approval.${stamp}`;
  const definition = await post(
    request,
    clientKind,
    accessToken,
    '/api/v1/workflow/definitions',
    {
      definitionKey,
      draft: {
        schemaVersion: 1,
        nodes: [
          {
            nodeKey: 'start',
            nodeTypeKey: 'start',
            nodeSchemaVersion: 1,
            config: { nextNodeKeys: ['approve'] }
          },
          {
            nodeKey: 'approve',
            nodeTypeKey: 'human.approval',
            nodeSchemaVersion: 1,
            config: {
              nextNodeKeys: ['end'],
              fieldPolicies: { reason: 'readOnly', secret: 'hidden', decision: 'required' }
            }
          },
          {
            nodeKey: 'end',
            nodeTypeKey: 'end',
            nodeSchemaVersion: 1,
            config: { nextNodeKeys: [] }
          }
        ]
      }
    },
    201
  );
  const version = await post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/definitions/${definition.id}/publish`,
    { expectedRevision: definition.draftRevision, formVersionId: formVersion.id }
  );
  return { definitionKey, versionId: version.id };
}

async function startInstance(request, clientKind, accessToken, definitionVersionId, reason) {
  return post(request, clientKind, accessToken, '/api/v1/workflow/instances', {
    definitionVersionId,
    businessType: 'admin.approval',
    businessId: crypto.randomUUID(),
    initialValues: { reason, secret: 'must remain hidden' },
    idempotencyKey: `start-${crypto.randomUUID()}`
  }, 201);
}

async function getInstance(request, clientKind, accessToken, instanceId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/workflow/instances/${instanceId}`, {
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  return response.json();
}

async function getMyTodo(request, clientKind, accessToken, instanceId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/workflow/todos/mine`, {
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  const todo = (await response.json()).find(item => item.instanceId === instanceId);
  expect(todo, `实例 ${instanceId} 应产生当前用户待办`).toBeDefined();
  return todo;
}

async function post(request, clientKind, accessToken, path, data, expectedStatus = 200) {
  const response = await request.post(`${apiBaseUrl}${path}`, {
    data,
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(expectedStatus);
  return response.json();
}

function apiHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}
