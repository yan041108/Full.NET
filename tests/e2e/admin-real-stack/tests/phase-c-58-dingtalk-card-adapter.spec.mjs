import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const dingTalkReceiptSignatureHeader = 'X-DingTalk-Receipt-Signature';

const forgedReceiptBody = {
  outTrackId: 'e2e-forged-track',
  userId: 'manager001',
  eventTime: '2026-09-06T10:00:01Z',
  status: 'delivered',
  errorCode: 'OK'
};

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：im.dingtalk 互动卡片 Provider 目录与伪造回执 fail-closed（清单 58）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const typesResponse = await request.get(`${apiBaseUrl}/api/v1/notifications/provider-types`, {
    headers
  });
  expect(typesResponse.ok()).toBeTruthy();
  const types = await typesResponse.json();
  const dingTalkType = types.find((item) => item.providerTypeKey === 'im.dingtalk');

  const receiptResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/provider-receipts/im.dingtalk`,
    {
      headers: {
        'Content-Type': 'application/json',
        [dingTalkReceiptSignatureHeader]: '00'
      },
      data: forgedReceiptBody
    }
  );

  if (dingTalkType) {
    expect(dingTalkType.receiptModeKey).toBe('signed');
    expect(dingTalkType.supportedChannelKeys).toContain('dingtalk');
    const fieldNames = (dingTalkType.nonSecretFields ?? []).map((field) => field.name);
    expect(fieldNames).toEqual(
      expect.arrayContaining(['appKey', 'agentId', 'cardTemplateId', 'robotCode'])
    );
    expect(receiptResponse.status()).toBe(400);
    const problem = await receiptResponse.json();
    expect(problem.code).toBe('notifications.receipt_invalid');
  } else {
    expect(receiptResponse.status()).toBe(404);
    const problem = await receiptResponse.json();
    expect(problem.code).toBe('notifications.receipt_provider_unknown');
    test.info().annotations.push({
      type: 'note',
      description:
        'real-stack 未启用 Notifications:Providers:DingTalk:Enabled；闭环以 DingTalk* 单元测试为准'
    });
  }
});

test('UI：钉钉渠道 Profile 与投递/偏好页冒烟（清单 58，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind === 'layui', '通知平台控制面仅验证 Vue 管理端');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /渠道配置/, '通知');

  const emptyCatalog = page.getByTestId('notification-profiles-empty-catalog');
  if (!(await emptyCatalog.isVisible())) {
    const createButton = page.getByTestId('notification-profiles-create');
    await expect(createButton).toBeVisible();
    await createButton.click();
    await page.getByTestId('notification-profiles-type').click();
    const dingTalkOption = page.getByRole('option', { name: 'im.dingtalk' });
    if (await dingTalkOption.isVisible()) {
      await dingTalkOption.click();
      await expect(page.getByTestId('notification-profiles-receipt-mode')).toContainText('验签回执');
    }
  }

  await clickMainNavLink(page, /投递运维/, '通知');
  await expect(page.getByRole('heading', { name: '投递运维', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);

  await clickMainNavLink(page, /通知偏好/, '通知');
  await expect(page.getByTestId('notification-preferences-endpoint-list')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
