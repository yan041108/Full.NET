import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

const receiptBody = {
  receiptIdempotencyKey: `e2e-receipt-${Date.now()}`,
  providerMessageId: 'e2e-msg-1',
  externalStatusKey: 'delivered',
  mappedStatusKey: 'delivered'
};

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：SMTP 无回执声明且匿名 Webhook fail-closed（清单 44）', async ({ request }, testInfo) => {
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
  const smtpType = types.find((item) => item.providerTypeKey === 'email.smtp');

  const receiptResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/provider-receipts/email.smtp`,
    {
      headers: { 'Content-Type': 'application/json' },
      data: receiptBody
    }
  );

  if (smtpType) {
    expect(smtpType.receiptModeKey).toBe('none');
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
        'real-stack 未启用 Notifications:Providers:Smtp:Enabled；SMTP none 回执以单元/集成断言为准'
    });
  }
});

test('UI：SMTP 渠道展示「无回执」标签（清单 44，需 Provider 目录非空）', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '通知渠道配置仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /渠道配置/, '通知');

  const emptyCatalog = page.getByTestId('notification-profiles-empty-catalog');
  if (await emptyCatalog.isVisible()) {
    test.skip(
      true,
      'SMTP Provider 未在 real-stack 启用；回执标签由 NotificationProviderProfilesView.test.ts 覆盖'
    );
  }

  const createButton = page.getByTestId('notification-profiles-create');
  await expect(createButton).toBeVisible();
  await createButton.click();

  await page.getByTestId('notification-profiles-type').click();
  await page.getByRole('option', { name: 'email.smtp' }).click();
  await expect(page.getByTestId('notification-profiles-receipt-mode')).toContainText('无回执');
});
