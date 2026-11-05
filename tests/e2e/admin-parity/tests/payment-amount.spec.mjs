import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'precise-order',
  tradeStateKey: 'succeeded', amountMinor: '9007199254740990', currency: 'CNY', subject: '原订单', description: null,
  codeUrl: null, providerTransactionId: 'paid', failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: '2026-11-05T00:00:00Z', version: 1 };
const maxRefundOrder = { ...order, id: id(9), amountMinor: '9223372036854775807', outTradeNo: 'max-refund-order' };
const fullOrder = { ...order, id: id(8), amountMinor: '100', outTradeNo: 'full-refund-order' };
const refund = { id: id(5), tenantId: id(2), orderId: order.id, merchantConfigId: id(3), outTradeNo: order.outTradeNo, outRefundNo: 'precise-refund',
  refundStateKey: 'succeeded', amountMinor: '9007199254740990', currency: 'CNY', reason: '核对', providerRefundId: 'refunded',
  failMessage: null, createdAtUtc: order.createdAtUtc, updatedAtUtc: null, completedAtUtc: order.createdAtUtc, version: 1 };

for (const width of [1440, 390]) {
  test('支付创建退款严格金额与列表刷新 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'payment', displayName: 'Payment', actorScope: 'host', scope: 'host',
      tenantId: null, isSuperAdministrator: false, sessionId: id(10), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false,
      permissions: ['identity.navigation.read', 'payments.orders.read', 'payments.orders.create', 'payments.refunds.read', 'payments.refunds.create'] }));
    await page.route('**/api/v1/navigation', route => json(route, [['payments-orders', '/payments/orders', 'payments.orders.read'], ['payments-refunds', '/payments/refunds', 'payments.refunds.read']]
      .map(([key, path, permission], index) => ({ id: key, parentId: null, routeName: key, componentKey: key, path, title: key, caption: '',
        icon: 'setting', order: index, requiredPermission: permission, children: [] }))));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    let orderReads = 0, refundReads = 0; const creates = [], refunds = [];
    await page.route('**/api/v1/payments/orders?*', route => {
      orderReads++; return json(route, { items: [order, { ...order, id: id(4), outTradeNo: 'unknown-amount', amountMinor: '9223372036854775807' }, { ...order, id: id(7), channelKey: 'alipay_page', outTradeNo: 'unsupported-refund' }, fullOrder, maxRefundOrder], page: 1, pageSize: 20, total: 5 });
    });
    await page.route('**/api/v1/payments/orders', route => {
      expect(route.request().method()).toBe('POST'); creates.push(route.request().postDataJSON()); return json(route, { ...order, amountMinor: creates.at(-1).amountMinor });
    });
    await page.route('**/api/v1/payments/orders/*/refunds', route => {
      expect(route.request().method()).toBe('POST'); refunds.push(route.request().postDataJSON()); const source = route.request().url().includes(fullOrder.id) ? fullOrder : route.request().url().includes(maxRefundOrder.id) ? maxRefundOrder : order;
      return json(route, { ...refund, orderId: source.id, outTradeNo: source.outTradeNo, reason: refunds.at(-1).reason, amountMinor: refunds.at(-1).amountMinor ?? source.amountMinor });
    });
    await page.route('**/api/v1/payments/refunds?*', route => {
      refundReads++; return json(route, { items: [refund, { ...refund, id: id(6), outRefundNo: 'unknown-refund', amountMinor: '9223372036854775807' }], page: 1, pageSize: 20, total: 2 });
    });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    const orders = page.locator('.payment-orders-view');
    await expect(orders.locator('.el-table__body-wrapper')).toContainText('90071992547409.90');
    await expect(orders.locator('.el-table__body-wrapper')).toContainText('92233720368547758.07');
    await expect(page.getByTestId('payment-refund-channel-hint')).toContainText('当前仅支持微信 Native');
    await expect(page.getByTestId('payment-order-refund-' + id(7))).toBeDisabled();
    await expect(page.getByTestId('payment-order-refund-records-' + id(7))).toBeEnabled();
    expect(refunds).toHaveLength(0);
    await page.screenshot({ path: info.outputPath('payment-refund-channel-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-orders-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await orders.getByRole('button', { name: '刷新', exact: true }).click(); await expect.poll(() => orderReads).toBe(2);
    await page.getByTestId('payment-order-create').click();
    await page.getByTestId('payment-order-create-tenant').fill(id(2)); await page.getByTestId('payment-order-create-subject').fill('明确订单');
    for (const invalid of ['1abc', '1.001', '92233720368547758.071']) {
      await page.getByTestId('payment-order-create-amount').fill(invalid); await page.getByTestId('payment-order-create-confirm').click();
      await expect(page.getByTestId('payment-order-create-amount')).toHaveValue(invalid); expect(creates).toHaveLength(0);
      await expect(page.getByText('金额需大于 0、最多两位小数，且不超过允许范围。', { exact: true }).last()).toBeVisible();
    }
    await page.getByTestId('payment-order-create-amount').fill('0.29');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-amount-create-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-order-create-confirm').click(); await expect.poll(() => creates.length).toBe(1); expect(creates[0].amountMinor).toBe('29');
    await expect(page.getByTestId('payment-order-create-amount')).toHaveCount(0); await expect.poll(() => orderReads).toBe(3);
    await page.getByTestId('payment-order-create').click();
    await page.getByTestId('payment-order-create-tenant').fill(id(2)); await page.getByTestId('payment-order-create-subject').fill('完整 Int64');
    await page.getByTestId('payment-order-create-amount').fill('92233720368547758.07');
    await expect(page.getByTestId('payment-order-create-amount')).toHaveValue('92233720368547758.07');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-int64-create-' + width + '.png'), animations: 'disabled' });
    await page.getByTestId('payment-order-create-confirm').click(); await expect.poll(() => creates.length).toBe(2);
    expect(creates[1].amountMinor).toBe('9223372036854775807'); expect(creates[1].idempotencyKey).not.toBe(creates[0].idempotencyKey);
    await expect.poll(() => orderReads).toBe(4);
    await page.getByTestId('payment-order-refund-' + order.id).click();
    await page.getByTestId('payment-order-refund-reason').fill('明确退款原因'); await page.getByTestId('payment-order-refund-amount').fill('2.675');
    await page.getByTestId('payment-order-refund-confirm').click(); expect(refunds).toHaveLength(0);
    await expect(page.getByText('金额需大于 0、最多两位小数，且不超过允许范围。', { exact: true }).last()).toBeVisible();
    await page.getByTestId('payment-order-refund-amount').fill('0.29');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-amount-refund-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-order-refund-confirm').click(); await expect.poll(() => refunds.length).toBe(1); expect(refunds[0].amountMinor).toBe('29');
    await expect(page.getByTestId('payment-order-refund-amount')).toHaveCount(0); await expect.poll(() => orderReads).toBe(5);
    await page.getByTestId('payment-order-refund-' + fullOrder.id).click(); await page.getByTestId('payment-order-refund-reason').fill('全额语义核对');
    await page.getByTestId('payment-order-refund-confirm').click(); await expect.poll(() => refunds.length).toBe(2); expect(refunds[1].amountMinor).toBeNull();
    await expect.poll(() => orderReads).toBe(6);
    await page.getByTestId('payment-order-refund-' + maxRefundOrder.id).click();
    await page.getByTestId('payment-order-refund-reason').fill('完整 Int64 退款');
    await page.getByTestId('payment-order-refund-amount').fill('92233720368547758.071');
    await page.getByTestId('payment-order-refund-confirm').click(); expect(refunds).toHaveLength(2);
    await expect(page.getByTestId('payment-order-refund-amount')).toHaveValue('92233720368547758.071');
    await page.getByTestId('payment-order-refund-amount').fill('92233720368547758.07');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-int64-refund-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-order-refund-confirm').click(); await expect.poll(() => refunds.length).toBe(3);
    expect(refunds[2].amountMinor).toBe('9223372036854775807'); await expect.poll(() => orderReads).toBe(7);
    await page.goto('/#/payments/refunds'); const records = page.locator('.payment-refunds-view');
    await expect(records.locator('.el-table__body-wrapper')).toContainText('90071992547409.90');
    await expect(records.locator('.el-table__body-wrapper')).toContainText('92233720368547758.07');
    await records.getByRole('button', { name: '刷新', exact: true }).click(); await expect.poll(() => refundReads).toBe(2);
  });
}
