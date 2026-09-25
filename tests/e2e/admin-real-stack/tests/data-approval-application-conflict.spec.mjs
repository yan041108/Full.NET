import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import {
  apiHeaders,
  ensureSerialRuleUpdateApprovalScenario,
  post
} from './support/workflow-approval-fixtures.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const updateScenarioKey = 'serial_numbers.host_rule.update';
const workflowBusinessType = 'data_approval.serial_rule.update';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

async function getJson(request, clientKind, accessToken, path) {
  const response = await request.get(`${apiBaseUrl}${path}`, {
    headers: apiHeaders(clientKind, accessToken)
  });
  expect(response.status(), await response.text()).toBe(200);
  return response.json();
}

async function createRule(request, clientKind, accessToken, stamp) {
  return post(request, clientKind, accessToken, '/api/v1/serial-numbers/rules', {
    ruleKey: `e2e.apply.conflict.${stamp}`,
    displayName: `E2E 应用冲突 ${stamp}`,
    description: null,
    scope: 1,
    resetInterval: 1,
    pattern: 'APC-{sequence:4}',
    minimumValue: 1,
    maximumValue: 9999,
    displayOrder: 1,
    isEnabled: true
  }, 201);
}

async function submitUpdateApproval(request, clientKind, accessToken, rule, displayName, stamp) {
  return post(
    request,
    clientKind,
    accessToken,
    `/api/v1/serial-numbers/rules/${rule.id}/update-approval-requests`,
    {
      update: {
        displayName,
        description: rule.description,
        scope: rule.scope,
        resetInterval: rule.resetInterval,
        pattern: rule.pattern,
        minimumValue: rule.minimumValue,
        maximumValue: rule.maximumValue,
        displayOrder: rule.displayOrder,
        isEnabled: rule.isEnabled,
        version: rule.version
      },
      idempotencyKey: `e2e-apply-conflict-${stamp}`
    },
    201
  );
}

async function waitForWorkflowInstance(request, clientKind, accessToken, requestId) {
  for (let attempt = 0; attempt < 90; attempt += 1) {
    const page = await getJson(
      request,
      clientKind,
      accessToken,
      '/api/v1/workflow/instances/mine?page=1&pageSize=100'
    );
    const instance = page.items?.find(item =>
      item.businessType === workflowBusinessType
      && item.businessId === String(requestId));
    if (instance) return instance;
    await new Promise(resolve => setTimeout(resolve, 1_000));
  }
  throw new Error(`workflow instance not found for DataApproval request ${requestId}`);
}

async function waitForDataApproval(request, clientKind, accessToken, requestId, predicate) {
  let current;
  for (let attempt = 0; attempt < 60; attempt += 1) {
    current = await getJson(
      request,
      clientKind,
      accessToken,
      `/api/v1/data-approvals/requests/${requestId}`
    );
    if (predicate(current)) return current;
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error(`DataApproval request ${requestId} did not reach the expected application state: ${JSON.stringify(current)}`);
}

async function approveWorkflowRequest(request, clientKind, accessToken, instance, comment) {
  const todos = await getJson(request, clientKind, accessToken, '/api/v1/workflow/todos/mine');
  const todo = todos.items?.find(item => item.instanceId === instance.id);
  expect(todo, `workflow instance ${instance.id} should have a todo for the host admin`).toBeTruthy();
  return post(
    request,
    clientKind,
    accessToken,
    `/api/v1/workflow/todos/${todo.id}/approve`,
    {
      expectedRevision: todo.revision,
      fieldPatch: { decision: 'approved' },
      comment,
      idempotencyKey: crypto.randomUUID()
    }
  );
}

test('审批快照发生版本冲突后可在真实栈中查看并重试应用', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '数据审批应用冲突页面仅验收 Vue');
  test.setTimeout(240_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  await ensureSerialRuleUpdateApprovalScenario(request, clientKind, accessToken);

  const stamp = `${Date.now().toString(36)}-${crypto.randomUUID()}`;
  const rule = await createRule(request, clientKind, accessToken, stamp);
  const firstSubmission = await submitUpdateApproval(
    request,
    clientKind,
    accessToken,
    rule,
    `E2E first approved update ${stamp}`,
    `${stamp}-first`
  );
  const staleSubmission = await submitUpdateApproval(
    request,
    clientKind,
    accessToken,
    rule,
    `E2E stale approved update ${stamp}`,
    `${stamp}-stale`
  );

  const firstInstance = await waitForWorkflowInstance(
    request,
    clientKind,
    accessToken,
    firstSubmission.requestId
  );
  const staleInstance = await waitForWorkflowInstance(
    request,
    clientKind,
    accessToken,
    staleSubmission.requestId
  );

  await approveWorkflowRequest(
    request,
    clientKind,
    accessToken,
    firstInstance,
    'Apply first snapshot before the stale approved snapshot.'
  );
  const applied = await waitForDataApproval(
    request,
    clientKind,
    accessToken,
    firstSubmission.requestId,
    item => item.statusKey === 'approved' && item.applicationStatusKey === 'applied'
  );
  expect(applied.applicationAttemptCount).toBe(1);

  await approveWorkflowRequest(
    request,
    clientKind,
    accessToken,
    staleInstance,
    'Apply a snapshot whose serial rule version is stale.'
  );
  const conflicted = await waitForDataApproval(
    request,
    clientKind,
    accessToken,
    staleSubmission.requestId,
    item => item.applicationStatusKey === 'failed_retryable'
  );
  expect(conflicted.statusKey).toBe('in_review');
  expect(conflicted.lastApplicationFailureCode).toBe('serial_numbers.rule.version_conflict');
  expect(conflicted.applicationAttemptCount).toBe(1);

  await loginAsHostAdmin(page);
  await page.goto(`${adminOrigin(clientKind)}/#/data-approvals/requests?requestId=${staleSubmission.requestId}`);
  await expect(page.getByTestId('data-approval-detail-status')).toContainText('in_review');
  await expect(page.getByTestId('data-approval-detail-application')).toContainText('可重试应用失败');
  await expect(page.getByTestId('data-approval-detail-application-failure'))
    .toContainText('serial_numbers.rule.version_conflict');
  const retryResponsePromise = page.waitForResponse(response =>
    response.url().endsWith(`/api/v1/data-approvals/requests/${staleSubmission.requestId}/retry-apply`)
    && response.request().method() === 'POST'
  );
  await page.getByTestId('data-approval-retry-apply').click();
  const retryResponse = await retryResponsePromise;
  expect(retryResponse.status(), await retryResponse.text()).toBe(200);
  const retried = await retryResponse.json();
  expect(retried.statusKey).toBe('in_review');
  expect(retried.applicationStatusKey).toBe('failed_retryable');
  expect(retried.lastApplicationFailureCode).toBe('serial_numbers.rule.version_conflict');
  expect(retried.applicationAttemptCount).toBe(2);
});
