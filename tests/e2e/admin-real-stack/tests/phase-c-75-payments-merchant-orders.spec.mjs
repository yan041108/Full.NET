import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createPaymentMerchantConfigViaApi,
  createPaymentOrderViaApi,
  expectPaymentMerchantConfigListItemMasked,
  getPaymentOrderViaApi,
  listPaymentMerchantConfigsViaApi,
  listPaymentOrdersViaApi,
  paymentWeChatNativeChannelKey
} from './support/payments-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Payments 页面仅 Vue 交付线');
}

test('API：商户配置脱敏、渠道白名单与订单查询（清单 75）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: merchantsResponse } = await listPaymentMerchantConfigsViaApi(
    request,
    clientKind
  );
  expect(merchantsResponse.ok()).toBeTruthy();
  const merchantsPage = await merchantsResponse.json();
  for (const item of merchantsPage.items ?? []) {
    expectPaymentMerchantConfigListItemMasked(item);
  }

  const suffix = randomUUID().slice(0, 8);
  const { response: invalidMerchant } = await createPaymentMerchantConfigViaApi(
    request,
    clientKind,
    {
      tenantId: null,
      name: `e2e-invalid-${suffix}`,
      channelKey: 'stripe_checkout',
      appId: 'wx0000000000000000',
      merchantId: '1900000000',
      certificateSerialNo: 'SERIAL',
      notifyUrl: 'https://example.com/api/v1/payments/wechat-native/notify/00000000-0000-0000-0000-000000000001',
      returnUrl: '',
      apiV3Key: '12345678901234567890123456789012',
      privateKeyPem: '-----BEGIN PRIVATE KEY-----\nMIIB\n-----END PRIVATE KEY-----',
      isDefault: false,
      isEnabled: false
    }
  );
  expect(invalidMerchant.status()).toBe(422);

  const { response: ordersResponse } = await listPaymentOrdersViaApi(request, clientKind);
  expect(ordersResponse.ok()).toBeTruthy();
  expect(Array.isArray((await ordersResponse.json()).items)).toBeTruthy();

  const { response: orderMissing } = await getPaymentOrderViaApi(request, clientKind, randomUUID());
  expect(orderMissing.status()).toBe(404);

  const { response: createOrderInvalid } = await createPaymentOrderViaApi(request, clientKind, {
    tenantId: randomUUID(),
    merchantConfigId: null,
    channelKey: paymentWeChatNativeChannelKey,
    amountMinor: 100,
    currency: 'CNY',
    subject: 'e2e-test',
    description: null
  });
  expect([404, 422]).toContain(createOrderInvalid.status());
});

test('UI：支付商户配置与支付订单页（清单 75）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /支付商户配置/);
  await expect(page.getByRole('heading', { name: '支付商户配置', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('payment-merchant-config-create')).toBeVisible();

  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /支付订单/);
  await expect(page.getByRole('heading', { name: '支付订单', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByTestId('payment-order-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
