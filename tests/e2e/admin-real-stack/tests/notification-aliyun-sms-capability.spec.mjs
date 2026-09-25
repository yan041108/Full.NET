import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const aliyunReceiptSignatureHeader = 'X-Aliyun-Sms-Receipt-Signature';

const forgedReceiptBody = [
  {
    phone_number: '13800138000',
    send_time: '2026-09-06 10:00:00',
    report_time: '2026-09-06 10:00:01',
    success: true,
    err_code: 'DELIVERED',
    biz_id: 'e2e-forged-biz'
  }
];

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：sms.aliyun 目录与伪造回执 fail-closed（清单 45）', async ({ request }, testInfo) => {
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
  const aliyunType = types.find((item) => item.providerTypeKey === 'sms.aliyun');

  const receiptResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/provider-receipts/sms.aliyun`,
    {
      headers: {
        'Content-Type': 'application/json',
        [aliyunReceiptSignatureHeader]: '00'
      },
      data: forgedReceiptBody
    }
  );

  if (aliyunType) {
    expect(aliyunType.receiptModeKey).toBe('signed');
    expect(aliyunType.supportedChannelKeys).toContain('sms');
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
        'real-stack 未启用 Notifications:Providers:AliyunSms:Enabled；闭环以单元/集成断言为准'
    });
  }
});

test('UI：阿里云渠道展示「验签回执」且模板可选 sms（清单 45，需 Provider 目录非空）', async ({
  page
}, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '通知平台控制面仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /渠道配置/, '通知');

  const emptyCatalog = page.getByTestId('notification-profiles-empty-catalog');
  if (await emptyCatalog.isVisible()) {
    test.skip(
      true,
      'Aliyun SMS Provider 未在 real-stack 启用；验签回执标签由 NotificationProviderProfilesView.test.ts 覆盖'
    );
  }

  await page.getByTestId('notification-profiles-create').click();
  await page.getByTestId('notification-profiles-type').click();
  await page.getByRole('option', { name: 'sms.aliyun' }).click();
  await expect(page.getByTestId('notification-profiles-receipt-mode')).toContainText('验签回执');

  await clickMainNavLink(page, /通知模板/, '通知');
  await page.getByTestId('notification-templates-channel').click();
  await expect(page.getByRole('option', { name: 'sms', exact: true })).toBeVisible();
});
