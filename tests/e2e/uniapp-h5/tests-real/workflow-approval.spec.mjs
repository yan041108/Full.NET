import { expect, test } from '@playwright/test';
import {
  loginAccessTokenWithPassword,
  provisionLimitedHostUserViaApi
} from '../../admin-real-stack/tests/support/real-stack-auth.mjs';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const apiBaseUrl = process.env.FULLNET_E2E_API_URL
  ?? `http://localhost:${process.env.FULLNET_E2E_API_PORT ?? '5159'}`;
const h5Origin = 'http://localhost:5175';

test('管理员可用真实动态表单完成同意、幂等重放与驳回', async ({ page, request }) => {
  test.setTimeout(90_000);
  const accessToken = await loginAccessToken(request);
  const definitionVersionId = await publishApprovalAssets(request, accessToken);
  const approved = await startInstance(request, accessToken, definitionVersionId, 'annual leave');
  const rejected = await startInstance(request, accessToken, definitionVersionId, 'schedule conflict');

  await login(page, username, password);
  const approvedAction = await openTodoAndAct(
    page,
    approved.instanceId,
    'approved',
    '同意',
    { retryOnce: true }
  );

  expect(approvedAction.url()).toBe(`${apiBaseUrl}/api/v1/workflow/todos/${approved.todoId}/approve`);
  const approvedRequest = approvedAction.postDataJSON();
  expect(approvedRequest).toMatchObject({
    expectedRevision: 1,
    fieldPatch: { decision: 'approved' },
    comment: 'mobile approved'
  });
  expect(approvedRequest.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/iu);
  expect(approvedAction.attempts).toHaveLength(2);
  expect(approvedAction.attempts[1]?.postDataJSON()).toEqual(approvedRequest);

  const replay = await request.post(approvedAction.url(), {
    data: approvedRequest,
    headers: apiHeaders(accessToken)
  });
  expect(replay.status(), await replay.text()).toBe(200);
  expect((await replay.json()).statusKey).toBe('completed');

  await openTodoAndAct(page, rejected.instanceId, 'rejected', '驳回');
  const rejectedInstance = await getInstance(request, accessToken, rejected.instanceId);
  expect(rejectedInstance.statusKey).toBe('rejected');
});

test('只有待办读取权限的真实用户看不到审批动作且直调失败关闭', async ({ page, request }) => {
  test.setTimeout(90_000);
  const limited = await provisionLimitedHostUserViaApi(request, 'vue', {
    permissionCodes: [
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
    'vue',
    limited.username,
    limited.password
  );
  const definitionVersionId = await publishApprovalAssets(request, accessToken);
  const instance = await startInstance(request, accessToken, definitionVersionId, 'restricted task');

  await login(page, limited.username, limited.password);
  await page.locator('.card').filter({ hasText: instance.instanceId }).click();
  await expect(page.locator('.approve')).toHaveCount(0);
  await expect(page.locator('.reject')).toHaveCount(0);

  const bypass = await request.post(
    `${apiBaseUrl}/api/v1/workflow/todos/${instance.todoId}/approve`,
    {
      data: {
        expectedRevision: 1,
        fieldPatch: { decision: 'bypass' },
        comment: 'must be rejected',
        idempotencyKey: crypto.randomUUID()
      },
      headers: apiHeaders(accessToken)
    }
  );
  expect(bypass.status()).toBe(403);
  expect((await bypass.json()).code).toBe('authorization.permission_denied');
});

test('待办被并发处理后页面刷新最新修订并关闭失效动作', async ({ page, request }) => {
  test.setTimeout(90_000);
  const accessToken = await loginAccessToken(request);
  const definitionVersionId = await publishApprovalAssets(request, accessToken);
  const instance = await startInstance(request, accessToken, definitionVersionId, 'concurrent approval');

  await login(page, username, password);
  const card = page.locator('.card').filter({ hasText: instance.instanceId });
  await expect(card).toBeVisible();
  await card.click();
  const decisionField = page.locator('.fullnet-workflow-form__field').filter({ hasText: 'decision' });
  await decisionField.locator('input').fill('stale decision');

  await post(request, accessToken, `/api/v1/workflow/todos/${instance.todoId}/approve`, {
    expectedRevision: 1,
    fieldPatch: { decision: 'concurrent decision' },
    comment: 'completed by concurrent actor',
    idempotencyKey: crypto.randomUUID()
  });

  await page.locator('.approve').click();
  await expect(page.getByRole('alert')).toHaveText('待办已被更新，已刷新为最新内容，请重新确认。');
  await expect(page.locator('.meta')).toContainText('completed');
  await expect(page.locator('.approve')).toHaveCount(0);
  await expect(page.locator('.reject')).toHaveCount(0);
});

async function login(page, loginUsername, loginPassword) {
  await page.goto('/');
  const inputs = page.getByRole('textbox');
  await inputs.nth(0).fill(loginUsername);
  await inputs.nth(1).fill(loginPassword);
  await page.locator('.primary').click();
  await expect(page.getByRole('banner').getByText('我的待办', { exact: true })).toBeVisible();
}

async function openTodoAndAct(page, instanceId, decision, buttonName, options = {}) {
  const card = page.locator('.card').filter({ hasText: instanceId });
  await expect(card).toBeVisible();
  await card.click();

  const reason = page.locator('.fullnet-workflow-form__field').filter({ hasText: 'reason' });
  const decisionField = page.locator('.fullnet-workflow-form__field').filter({ hasText: 'decision' });
  await expect(reason.locator('input')).toBeDisabled();
  await expect(reason.locator('input')).not.toHaveValue('');

  const actionButton = page.locator(buttonName === '同意' ? '.approve' : '.reject');
  await actionButton.click();
  await expect(page.getByRole('alert')).toHaveText('请完成必填字段。');

  await decisionField.locator('input').fill(decision);
  await page.getByRole('textbox').last().fill(`mobile ${decision}`);
  const actionSuffix = `/${buttonName === '同意' ? 'approve' : 'reject'}`;
  const attempts = [];
  const captureAttempt = value => {
    if (value.url().endsWith(actionSuffix) && value.method() === 'POST') {
      attempts.push(value);
    }
  };
  page.on('request', captureAttempt);
  if (options.retryOnce) {
    await page.route(`**${actionSuffix}`, route => route.abort('failed'), { times: 1 });
  }
  await actionButton.click();
  if (options.retryOnce) {
    await expect(page.getByRole('alert')).toHaveText('网络或服务暂时不可用，可安全重试本次操作。');
    await actionButton.click();
  }
  await expect(page.getByRole('banner').getByText('我的待办', { exact: true })).toBeVisible();
  await expect(page.locator('.card').filter({ hasText: instanceId })).toHaveCount(0);
  page.off('request', captureAttempt);
  return { attempts, url: () => attempts.at(-1)?.url(), postDataJSON: () => attempts.at(-1)?.postDataJSON() };
}

async function loginAccessToken(request) {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { username, password },
    headers: { Origin: h5Origin, 'Content-Type': 'application/json' }
  });
  expect(response.status(), await response.text()).toBe(200);
  const body = await response.json();
  expect(typeof body.accessToken).toBe('string');
  return body.accessToken;
}

async function publishApprovalAssets(request, accessToken) {
  const stamp = `${Date.now().toString(36)}-${crypto.randomUUID()}`;
  const createForm = await post(request, accessToken, '/api/v1/workflow/forms', {
    formKey: `mobile.approval.${stamp}`,
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
  const form = await createForm.json();
  const publishForm = await post(
    request,
    accessToken,
    `/api/v1/workflow/forms/${form.id}/publish`,
    { expectedRevision: form.draftRevision }
  );
  const formVersion = await publishForm.json();

  const createDefinition = await post(request, accessToken, '/api/v1/workflow/definitions', {
    definitionKey: `mobile.approval.${stamp}`,
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
  }, 201);
  const definition = await createDefinition.json();
  const publishDefinition = await post(
    request,
    accessToken,
    `/api/v1/workflow/definitions/${definition.id}/publish`,
    { expectedRevision: definition.draftRevision, formVersionId: formVersion.id }
  );
  return (await publishDefinition.json()).id;
}

async function startInstance(request, accessToken, definitionVersionId, reason) {
  const response = await post(request, accessToken, '/api/v1/workflow/instances', {
    definitionVersionId,
    businessType: 'mobile.approval',
    businessId: crypto.randomUUID(),
    initialValues: { reason, secret: 'must remain hidden' },
    idempotencyKey: `start-${crypto.randomUUID()}`
  }, 201);
  const body = await response.json();
  return { instanceId: body.id, todoId: body.activeTodoId };
}

async function getInstance(request, accessToken, instanceId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/workflow/instances/${instanceId}`, {
    headers: apiHeaders(accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  return response.json();
}

async function post(request, accessToken, path, data, expectedStatus = 200) {
  const response = await request.post(`${apiBaseUrl}${path}`, {
    data,
    headers: apiHeaders(accessToken)
  });
  expect(response.status(), await response.text()).toBe(expectedStatus);
  return response;
}

function apiHeaders(accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: h5Origin,
    'Content-Type': 'application/json'
  };
}
