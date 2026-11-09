import { expect, test } from '@playwright/test';
import { loginAsHostUser, statusPath } from './support/real-stack-auth.mjs';

const fixture = process.env.FULLNET_E2E_BILLING_ROLES;
test.skip(!fixture, '由订阅账单双库夹具准备专用订单和受限账号。');

test('受限角色通过真实页面读取原账单、订单与退款', async ({ browser }) => {
  test.setTimeout(145_000);
  const input = JSON.parse(fixture);
  const password = process.env.FULLNET_E2E_PASSWORD;
  const origin = process.env.FULLNET_E2E_ADMIN_ORIGIN;
  const { tenantName, subscriptionId, orderId, refundId, phase } = input;

  async function as(username, body) {
    const context = await browser.newContext({ baseURL: origin });
    try {
      const page = await context.newPage();
      page.setDefaultTimeout(15_000);
      await loginAsHostUser(page, username, password);
      await body(page);
    } finally {
      await context.close();
    }
  }

  async function openSubscriptions(page, target = tenantName) {
    await page.goto(statusPath('vue-admin', 'tenants'));
    await page.getByRole('row').filter({ hasText: target }).getByTestId('tenants-action-subscriptions').click();
    await expect(page.getByTestId('subscription-target')).toHaveText(target);
  }

  async function closeDialog(page, testId) {
    const dialog = page.getByRole('dialog').filter({ has: page.getByTestId(testId) });
    await dialog.getByRole('button', { name: '关闭', exact: true }).click();
    await expect(dialog).not.toBeVisible();
  }

  function inputField(page, testId) {
    return page.locator(`input[data-testid="${testId}"], [data-testid="${testId}"] input`);
  }

  async function readOriginalOrder(page) {
    await page.goto(statusPath('vue-admin', 'payments/orders'));
    await expect(page).toHaveURL(/#\/payments\/orders$/);
    await inputField(page, 'payment-order-id-input').fill(orderId);
    await page.getByTestId('payment-order-id-read').click();
    await expect(page.getByTestId('payment-order-detail-id')).toHaveText(orderId);
    await expect(page.getByTestId('payment-order-detail-snapshot')).toBeVisible();
  }

  async function readInvoice(page) {
    await openSubscriptions(page);
    await inputField(page, 'subscription-detail-lookup-input').fill(subscriptionId);
    await page.getByTestId('subscription-detail-lookup').click();
    await expect(page.getByTestId('subscription-detail-id')).toHaveText(subscriptionId);
    await page.getByTestId('subscription-invoice-read').click();
    await expect(page.getByTestId('subscription-invoice-snapshot')).toContainText(subscriptionId);
    return page.getByTestId('subscription-invoice-snapshot');
  }

  if (phase) {
    await as(input.reader, async page => {
      await page.goto(statusPath('vue-admin', 'payments/orders'));
      const original = page.getByTestId(`payment-order-detail-${orderId}`).locator('xpath=ancestor::tr');
      await original.getByTestId('subscription-fulfillment').click();
      await expect(page.getByTestId('fulfillment-read-at')).toBeVisible();
      await expect(page.locator('.fulfillment-snapshot')).toContainText(subscriptionId);
      if (phase === 'ReadyPaid') await expect(page.getByTestId('fulfillment-waiting')).toBeVisible();
      else await expect(page.locator('.fulfillment-snapshot')).toContainText('test.manual_review');
      await expect(page.getByTestId('subscription-fulfillment-retry')).toHaveCount(0);
    });
    return;
  }

  if (refundId) {
    await as(input.reader, async page => {
      const invoice = await readInvoice(page);
      await expect(invoice).toContainText(orderId);
    });
    await as(input.reconciler, async page => {
      await page.goto(statusPath('vue-admin', 'payments/refunds'));
      await inputField(page, 'refunds-exact-id').fill(refundId);
      await page.getByTestId('refunds-read-id').click();
      await expect(page.getByTestId('refund-detail-id')).toHaveText(refundId);
      await expect(page.getByTestId('refund-detail-snapshot')).toContainText(orderId);
      const readback = page.waitForResponse(response => response.request().method() === 'GET'
        && new URL(response.url()).pathname === `/api/v1/payments/refunds/${refundId}`);
      await page.getByTestId('refund-detail-refresh').click();
      expect((await readback).ok()).toBeTruthy();
      await expect(page.getByTestId('refund-detail-snapshot')).toContainText(orderId);
      await page.getByTestId('refund-read-original-order').click();
      await expect(page.getByTestId('payment-order-detail-id')).toHaveText(orderId);
    });
    return;
  }

  await as(input.reader, async page => {
    const invoice = await readInvoice(page);
    await expect(invoice).toContainText('Frozen billing plan');
    await expect(page.getByTestId('invoice-exact-amount')).toHaveText('12500');
    await expect(page.getByTestId('subscription-checkout')).toHaveCount(0);
    await expect(page.getByTestId('tenant-subscription-cancel')).toHaveCount(0);
    await closeDialog(page, 'subscription-detail-id');
    await closeDialog(page, 'subscription-target');
    await openSubscriptions(page, input.spareTenant);
    await expect(page.getByTestId('subscription-detail-id')).not.toBeVisible();
    await expect(page.getByTestId('subscription-invoice-snapshot')).not.toBeVisible();
    await readOriginalOrder(page);
    await expect(page.getByTestId(`payment-order-reconcile-${orderId}`)).toHaveCount(0);
    await expect(page.getByTestId(`payment-order-refund-${orderId}`)).toHaveCount(0);

    await closeDialog(page, 'payment-order-detail-id');
    await page.getByRole('button', { name: '受限管理员', exact: true }).click();
    await page.getByRole('button', { name: '退出登录', exact: true }).click();
    await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
    await page.getByLabel('账号', { exact: true }).fill(input.reconciler);
    await page.getByLabel('密码', { exact: true }).fill(password);
    await page.getByRole('button', { name: '进入控制台' }).click();
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
    await expect(page.getByTestId('payment-order-detail-id')).not.toBeVisible();
    await page.goto(statusPath('vue-admin', 'tenants'));
    await expect(page).toHaveURL(/#\/403$/);
    await page.goto(statusPath('vue-admin', 'payments/merchant-configs'));
    await expect(page).toHaveURL(/#\/403$/);
    await page.goto(statusPath('vue-admin', 'payments/orders'));
    await expect(inputField(page, 'payment-order-id-input')).toHaveValue('');
    await readOriginalOrder(page);
    await expect(page.getByTestId('subscription-fulfillment')).toHaveCount(0);
    await expect(page.getByTestId(`payment-order-refund-${orderId}`)).toHaveCount(0);
    await closeDialog(page, 'payment-order-detail-id');
    await page.getByTestId(`payment-order-reconcile-${orderId}`).click();
    await expect(page.getByTestId('payment-reconcile-id')).toHaveText(orderId);
    await expect(page.getByTestId('payment-reconcile-confirm')).toBeEnabled();
  });

  await as(input.operator, async page => {
    await openSubscriptions(page);
    await page.getByTestId('subscription-checkout').click();
    await page.getByTestId('subscription-checkout-confirm').click();
    await expect(page.getByRole('dialog').getByText(orderId, { exact: true })).toBeVisible();
    await page.getByTestId('subscription-fulfillment').click();
    await expect(page.getByTestId('fulfillment-read-at')).toBeVisible();
    await expect(page.getByTestId('subscription-fulfillment-retry')).toHaveCount(0);
  });

});
