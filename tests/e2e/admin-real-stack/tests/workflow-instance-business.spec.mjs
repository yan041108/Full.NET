import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import { ensureSerialRuleUpdateApprovalScenario } from './support/workflow-approval-fixtures.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const businessType = 'data_approval.serial_rule.update';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

async function createRuleAndSubmitUpdateApproval(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
  await ensureSerialRuleUpdateApprovalScenario(request, clientKind, accessToken);
  const stamp = Date.now().toString(36);
  const ruleKey = `e2e.biz.${stamp}`;
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/serial-numbers/rules`, {
    data: {
      ruleKey,
      displayName: `E2E 业务跳转 ${stamp}`,
      description: null,
      scope: 1,
      resetInterval: 1,
      pattern: 'BIZ-{sequence:4}',
      minimumValue: 1,
      maximumValue: 9999,
      displayOrder: 1,
      isEnabled: true
    },
    headers
  });
  expect(createResponse.status()).toBe(201);
  const rule = await createResponse.json();
  const updatedDisplayName = `E2E 业务跳转已改 ${stamp}`;
  const submitResponse = await request.post(
    `${apiBaseUrl}/api/v1/serial-numbers/rules/${rule.id}/update-approval-requests`,
    {
      data: {
        update: {
          displayName: updatedDisplayName,
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
        idempotencyKey: crypto.randomUUID()
      },
      headers
    }
  );
  expect(submitResponse.status(), await submitResponse.text()).toBe(201);
  const submission = await submitResponse.json();
  expect(submission.requestId).toBeTruthy();
  return { requestId: submission.requestId, ruleId: rule.id, ruleKey, headers, accessToken };
}

async function waitForWorkflowInstance(request, headers, requestId) {
  const businessId = String(requestId);
  for (let attempt = 0; attempt < 45; attempt += 1) {
    const listResponse = await request.get(
      `${apiBaseUrl}/api/v1/workflow/instances/mine?page=1&pageSize=50`,
      { headers }
    );
    expect(listResponse.status()).toBe(200);
    const body = await listResponse.json();
    const match = body.items?.find(item =>
      item.businessType === businessType && item.businessId === businessId);
    if (match?.id) {
      return match;
    }
    await new Promise(resolve => setTimeout(resolve, 2_000));
  }
  throw new Error(`workflow instance not found for businessId ${businessId}`);
}

test('工作流实例详情可跳转到数据审批请求', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '业务单据跳转仅在 Vue 交付线验收');
  test.setTimeout(180_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const { requestId, ruleId, headers } = await createRuleAndSubmitUpdateApproval(request, clientKind);
  const instance = await waitForWorkflowInstance(request, headers, requestId);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /工作流实例/, '工作流');

  const view = page.locator('.workflow-instances');
  await expect(view.getByRole('heading', { name: '工作流实例', exact: true })).toBeVisible();
  await view.getByTestId('workflow-instance-tabs').getByRole('tab', { name: '我发起的' }).click();
  const instanceRow = view.getByRole('row').filter({ hasText: String(ruleId) });
  await expect(instanceRow.first()).toBeVisible({ timeout: 30_000 });
  await instanceRow.first().click();
  await expect(view.getByTestId('workflow-instance-summary')).toBeVisible({ timeout: 15_000 });
  await view.getByTestId('workflow-instance-open-business').click();
  await expect(page).toHaveURL(new RegExp(`requestId=${requestId}`, 'i'));
  await expect(page.getByTestId('data-approval-detail-status')).toBeVisible({ timeout: 15_000 });
});
