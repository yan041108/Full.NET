import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const approvalSyncSignatureHeader = 'X-DingTalk-Approval-Sync-Signature';

const forgedCallbackBody = {
  processInstanceId: 'e2e-forged-proc',
  status: 'COMPLETED',
  result: 'agree',
  eventTime: '2026-09-06T10:00:01Z'
};

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：钉钉审批镜像同步列表与匿名回调 fail-closed（清单 59）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const authHeaders = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/dingtalk/approval-sync?page=1&pageSize=5`,
    { headers: authHeaders }
  );

  const callbackResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/dingtalk/approval-sync/callback`,
    {
      headers: {
        'Content-Type': 'application/json',
        [approvalSyncSignatureHeader]: '00'
      },
      data: forgedCallbackBody
    }
  );

  if (listResponse.status() === 404) {
    expect(callbackResponse.status()).toBe(404);
    test.info().annotations.push({
      type: 'note',
      description:
        'real-stack 未同时启用 Notifications:Providers:DingTalk:Enabled 与 Workflow:Enabled；闭环以 DingTalkApprovalSync* 单元/OpenAPI 为准'
    });
    return;
  }

  expect(listResponse.ok()).toBeTruthy();
  const pageResult = await listResponse.json();
  expect(Array.isArray(pageResult.items)).toBe(true);
  expect(typeof pageResult.total).toBe('number');

  expect(callbackResponse.status()).toBe(400);
  const problem = await callbackResponse.json();
  expect(problem.code).toBe('notifications.receipt_invalid');
});

test('UI：钉钉审批同步记录页与流程权威提示（清单 59，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '钉钉审批同步页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /钉钉审批同步/, '通知');

  await expect(page.getByRole('heading', { name: '钉钉审批同步记录', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(
    page.getByText('Full.NET Workflow 仍是流程权威', { exact: false })
  ).toBeVisible();
  await expect(page.getByTestId('dingtalk-sync-list')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
