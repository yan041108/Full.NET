import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createPaymentRefundViaApi,
  getPaymentRefundViaApi,
  listPaymentRefundsViaApi,
  postWeChatNativeNotifyViaApi,
  reconcilePaymentOrderViaApi
} from './support/payments-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Payments 回调/退款页面仅 Vue 交付线');
}

test('API：微信 notify 头校验、对账/退款契约（清单 76）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const merchantConfigId = randomUUID();
  const { response: notifyMissingHeaders } = await postWeChatNativeNotifyViaApi(
    request,
    merchantConfigId
  );
  expect(notifyMissingHeaders.status()).toBe(500);
  const notifyAck = await notifyMissingHeaders.json();
  expect(notifyAck.code).toBe('FAIL');
  expect(String(notifyAck.message)).toMatch(/header/i);

  const missingOrderId = randomUUID();
  const { response: reconcileMissing } = await reconcilePaymentOrderViaApi(
    request,
    clientKind,
    missingOrderId
  );
  expect(reconcileMissing.status()).toBe(404);

  const { response: refundsList } = await listPaymentRefundsViaApi(request, clientKind);
  expect(refundsList.ok()).toBeTruthy();
  expect(Array.isArray((await refundsList.json()).items)).toBeTruthy();

  const { response: refundMissing } = await getPaymentRefundViaApi(request, clientKind, randomUUID());
  expect(refundMissing.status()).toBe(404);

  const { response: createRefundMissing } = await createPaymentRefundViaApi(
    request,
    clientKind,
    missingOrderId,
    { amountMinor: 1, reason: 'e2e-76' }
  );
  expect(createRefundMissing.status()).toBe(404);
});

test('UI：支付订单操作区与支付退款列表页（清单 76）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  await clickMainNavLink(page, /支付订单/);
  await expect(page.getByRole('heading', { name: '支付订单', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('payment-order-create')).toBeVisible();

  await clickMainNavLink(page, /支付退款/);
  await expect(page.getByRole('heading', { name: '支付退款', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.locator('.payment-refunds-view')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
