import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const disableScenarioKey = 'serial_numbers.host_rule.disable';
const updateScenarioKey = 'serial_numbers.host_rule.update';

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

async function listScenarios(request, headers) {
  const response = await request.get(`${apiBaseUrl}/api/v1/data-approvals/scenarios`, { headers });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

/** 若禁用场景未启用，则复用已启用的更新场景工作流版本进行绑定（与 real-stack 种子一致）。 */
async function ensureDisableScenarioEnabled(request, headers) {
  const scenarios = await listScenarios(request, headers);
  const disable = scenarios.find((item) => item.scenarioKey === disableScenarioKey);
  const update = scenarios.find((item) => item.scenarioKey === updateScenarioKey);
  expect(disable).toBeTruthy();
  expect(update).toBeTruthy();
  if (disable.isEnabled) {
    return;
  }
  expect(update.isEnabled).toBeTruthy();
  expect(update.workflowDefinitionVersionId).toBeTruthy();
  const response = await request.put(
    `${apiBaseUrl}/api/v1/data-approvals/scenarios/${disableScenarioKey}`,
    {
      headers,
      data: {
        isEnabled: true,
        workflowDefinitionVersionId: update.workflowDefinitionVersionId,
        version: disable.version ?? null
      }
    }
  );
  expect(response.ok()).toBeTruthy();
}

async function createHostRule(request, headers, ruleKey) {
  const response = await request.post(`${apiBaseUrl}/api/v1/serial-numbers/rules`, {
    headers,
    data: {
      ruleKey,
      displayName: `E2E disable ${ruleKey}`,
      description: null,
      scope: 1,
      resetInterval: 1,
      pattern: 'DIS-{sequence:4}',
      minimumValue: 1,
      maximumValue: 9999,
      displayOrder: 1,
      isEnabled: true
    }
  });
  expect(response.status()).toBe(201);
  return response.json();
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：第二场景目录与禁用审批提交（清单 46）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);

  await ensureDisableScenarioEnabled(request, headers);

  const stamp = Date.now().toString(36);
  const rule = await createHostRule(request, headers, `e2e.da.disable.${stamp}`);

  const previewResponse = await request.post(
    `${apiBaseUrl}/api/v1/serial-numbers/rules/${rule.id}/disable-approval-preview`,
    { headers, data: { version: rule.version } }
  );
  expect(previewResponse.ok()).toBeTruthy();

  const submitResponse = await request.post(
    `${apiBaseUrl}/api/v1/serial-numbers/rules/${rule.id}/disable-approval-requests`,
    {
      headers,
      data: {
        statusChange: { version: rule.version },
        idempotencyKey: `e2e-disable-${stamp}`
      }
    }
  );
  expect(submitResponse.status()).toBe(201);
  const submitted = await submitResponse.json();
  expect(submitted.requestId).toBeTruthy();

  const detailResponse = await request.get(
    `${apiBaseUrl}/api/v1/data-approvals/requests/${submitted.requestId}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(detailResponse.ok()).toBeTruthy();
  const detail = await detailResponse.json();
  expect(detail.scenarioKey).toBe(disableScenarioKey);
});

test('Host 管理员可提交流水号规则禁用审批并打开审批请求（清单 46）', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '流水号禁用审批仅验收 Vue');
  test.setTimeout(120_000);

  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  await ensureDisableScenarioEnabled(request, authHeaders(token, origin));

  const stamp = Date.now().toString(36);
  const ruleKey = `e2e.dis.approval.${stamp}`;

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /流水号规则/, '流水号');
  const view = page.locator('.serial-number-rules-view');
  await view.getByTestId('serial-rule-key').fill(ruleKey);
  await view.getByTestId('serial-rule-display-name').fill(`E2E 禁用审批 ${stamp}`);
  await view.getByTestId('serial-rule-pattern').fill('DISAPR-{sequence:4}');
  await view.getByTestId('serial-rule-minimum').fill('1');
  await view.getByTestId('serial-rule-maximum').fill('9999');
  await view.getByTestId('serial-rule-create').click();
  await expect(page.getByText('规则已创建')).toBeVisible({ timeout: 15_000 });
  await view.getByTestId('serial-rule-filter-key').fill(ruleKey);
  await view.getByTestId('serial-rule-filter-apply').click();
  await view.locator('[data-testid="serial-rule-load"]').filter({ hasText: ruleKey }).first().click();

  await expect(view.getByTestId('serial-rule-submit-disable-approval')).toBeVisible({ timeout: 15_000 });
  await expect(view.getByTestId('serial-rule-disable')).toHaveCount(0);

  await view.getByTestId('serial-rule-submit-disable-approval').click();
  await view.getByTestId('serial-rule-disable-approval-confirm').click();
  await expect(page.getByText('已提交禁用审批请求')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('data-approval-detail-status')).toBeVisible({ timeout: 15_000 });

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/data-approvals/requests?page=1&pageSize=20`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(listResponse.status()).toBe(200);
  const items = (await listResponse.json()).items ?? [];
  expect(items.some((item) => item.scenarioKey === disableScenarioKey)).toBeTruthy();
});
