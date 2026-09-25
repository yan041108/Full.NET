import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

const forgedReceiptBody = {
  receiptIdempotencyKey: `e2e-wecom-receipt-${Date.now()}`,
  providerMessageId: 'e2e-msg-wecom',
  externalStatusKey: 'delivered',
  mappedStatusKey: 'delivered'
};

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：im.wecom 文本 Provider 目录与无回执 Webhook fail-closed（清单 60）', async ({
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
  const wecomType = types.find((item) => item.providerTypeKey === 'im.wecom');

  const receiptResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/provider-receipts/im.wecom`,
    {
      headers: { 'Content-Type': 'application/json' },
      data: forgedReceiptBody
    }
  );

  if (wecomType) {
    expect(wecomType.receiptModeKey).toBe('none');
    expect(wecomType.supportedChannelKeys).toContain('wecom');
    const fieldNames = (wecomType.nonSecretFields ?? []).map((field) => field.name);
    expect(fieldNames).toEqual(expect.arrayContaining(['corpId', 'agentId']));
    expect(receiptResponse.status()).toBe(404);
    const problem = await receiptResponse.json();
    expect(problem.code).toBe('notifications.receipt_not_supported');
  } else {
    expect(receiptResponse.status()).toBe(404);
    const problem = await receiptResponse.json();
    expect(problem.code).toBe('notifications.receipt_provider_unknown');
    test.info().annotations.push({
      type: 'note',
      description:
        'real-stack 未启用 Notifications:Providers:WeCom:Enabled；闭环以 WeCom* 单元测试为准'
    });
  }
});

test('UI：企业微信渠道 Profile 与投递/偏好页冒烟（清单 60，Vue）', async ({ page }, testInfo) => {
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
    const wecomOption = page.getByRole('option', { name: 'im.wecom' });
    if (await wecomOption.isVisible()) {
      await wecomOption.click();
      await expect(page.getByTestId('notification-profiles-receipt-mode')).toContainText('无回执');
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
