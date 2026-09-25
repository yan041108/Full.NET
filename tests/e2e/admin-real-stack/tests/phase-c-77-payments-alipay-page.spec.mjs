import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  buildAlipayPageMerchantConfigBody,
  createPaymentMerchantConfigViaApi,
  createPaymentOrderViaApi,
  paymentAlipayPageChannelKey
} from './support/payments-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Payments 支付宝页仅 Vue 交付线');
}

test('API：alipay_page 商户元数据与订单渠道白名单（清单 77）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const suffix = randomUUID().slice(0, 8);
  const { response: alipayMerchant } = await createPaymentMerchantConfigViaApi(
    request,
    clientKind,
    buildAlipayPageMerchantConfigBody(suffix)
  );
  expect(alipayMerchant.status()).toBe(201);
  const merchant = await alipayMerchant.json();
  expect(merchant.channelKey).toBe(paymentAlipayPageChannelKey);

  const { response: alipayMissingReturn } = await createPaymentMerchantConfigViaApi(
    request,
    clientKind,
    buildAlipayPageMerchantConfigBody(`${suffix}-bad`, { returnUrl: '' })
  );
  expect(alipayMissingReturn.status()).toBe(422);

  const { response: orderInvalidChannel } = await createPaymentOrderViaApi(request, clientKind, {
    tenantId: randomUUID(),
    merchantConfigId: merchant.id,
    channelKey: 'stripe_checkout',
    amountMinor: 100,
    currency: 'CNY',
    subject: 'e2e-77',
    description: null
  });
  expect([404, 422]).toContain(orderInvalidChannel.status());

  const { response: orderAlipayChannel } = await createPaymentOrderViaApi(request, clientKind, {
    tenantId: randomUUID(),
    merchantConfigId: null,
    channelKey: paymentAlipayPageChannelKey,
    amountMinor: 100,
    currency: 'CNY',
    subject: 'e2e-77-channel',
    description: null
  });
  expect([404, 422]).toContain(orderAlipayChannel.status());
});

test('UI：商户与订单页展示支付宝网页支付渠道（清单 77）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /支付商户配置/);
  await expect(page.getByRole('heading', { name: '支付商户配置', exact: true })).toBeVisible({
    timeout: 20_000
  });
  await page.getByTestId('payment-merchant-config-create').click();
  await page.getByLabel('支付渠道', { exact: false }).click();
  await page.getByRole('option', { name: '支付宝网页支付' }).click();
  await expect(page.getByLabel('同步跳转地址', { exact: false })).toBeVisible();
  await expect(page.getByLabel('API v3 密钥', { exact: false })).toHaveCount(0);

  await enterDevelopmentTenant(page);
  await clickMainNavLink(page, /支付订单/);
  await page.getByTestId('payment-order-create').click();
  await page.getByLabel('支付渠道', { exact: false }).click();
  await expect(page.getByRole('option', { name: '支付宝网页支付' })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
