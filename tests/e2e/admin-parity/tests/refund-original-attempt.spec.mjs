import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-refund-order',
  tradeStateKey: 'succeeded', amountMinor: 100, currency: 'CNY', subject: 'Original payment', description: null,
  codeUrl: null, providerTransactionId: 'paid', failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null,
  paidAtUtc: '2026-11-05T00:00:00Z', version: 1 };
const refund = { id: id(5), tenantId: id(2), orderId: order.id, merchantConfigId: id(3), outTradeNo: order.outTradeNo,
  outRefundNo: 'original-refund', refundStateKey: 'provider_unknown', amountMinor: 29, currency: 'CNY', reason: 'original reason',
  providerRefundId: null, failMessage: null, createdAtUtc: order.createdAtUtc, updatedAtUtc: null, completedAtUtc: null, version: 1 };

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('未知退款锁定原请求并按原订单只读核对 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'refund', displayName: 'Refund', actorScope: 'host', scope: 'host',
      tenantId: null, isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
      permissions: ['identity.navigation.read', 'payments.orders.read', 'payments.refunds.read', 'payments.refunds.create'] }));
    await page.route('**/api/v1/navigation', route => json(route, [['payments-orders', '/payments/orders', 'payments.orders.read'], ['payments-refunds', '/payments/refunds', 'payments.refunds.read']]
      .map(([key, path, permission], index) => ({ id: key, parentId: null, routeName: key, componentKey: key, path, title: key,
        caption: '', icon: 'setting', order: index, requiredPermission: permission, children: [] }))));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    let orderReads = 0, refundReads = 0; const writes = [];
    await page.route('**/api/v1/payments/orders?*', route => { orderReads++; return json(route, { items: [order], page: 1, pageSize: 20, total: 1 }); });
    await page.route(`**/api/v1/payments/orders/${order.id}/refunds`, route => {
      expect(route.request().method()).toBe('POST'); writes.push(route.request().postDataJSON());
      return route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({
        code: 'payments.refund.provider_unknown', type: 'about:blank', status: 409, title: 'Provider response unknown' }) });
    });
    await page.route('**/api/v1/payments/refunds?*', route => {
      expect(new URL(route.request().url()).searchParams.get('orderId')).toBe(order.id); refundReads++;
      return json(route, { items: [refund], page: 1, pageSize: 20, total: 1 });
    });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('payment-order-refund-' + order.id).click();
    await page.getByTestId('payment-order-refund-reason').fill(' original reason ');
    await page.getByTestId('payment-order-refund-amount').fill('0.29');
    await page.getByTestId('payment-order-refund-confirm').click();
    await expect(page.getByTestId('payment-refund-attempt-status')).toContainText(locale === 'zh-CN' ? '退款结果尚未核实' : 'outcome is unverified');
    expect(writes).toEqual([{ amountMinor: 29, reason: 'original reason' }]);
    await expect(page.getByTestId('payment-order-refund-confirm')).toBeDisabled();
    await expect(page.getByTestId('payment-order-refund-reason')).toHaveAttribute('readonly', '');
    await expect(page.getByTestId('payment-order-refund-amount')).toHaveAttribute('readonly', '');
    await expect(page.getByTestId('payment-refund-original-attempt')).toContainText(order.id);
    await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click();
    await page.locator('.payment-orders-view').getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await expect.poll(() => orderReads).toBe(2);
    await page.getByTestId('payment-order-refund-' + order.id).click();
    await expect(page.getByTestId('payment-order-refund-reason')).toHaveValue('original reason');
    await expect(page.getByTestId('payment-order-refund-amount')).toHaveValue('0.29');
    await expect(page.getByTestId('payment-order-refund-confirm')).toBeDisabled(); expect(writes).toHaveLength(1);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('refund-original-attempt-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-refund-check-records').click();
    await expect(page).toHaveURL(new RegExp('/#/payments/refunds\\?orderId=' + order.id));
    await expect.poll(() => refundReads).toBe(1);
    await expect(page.locator('.payment-refunds-view')).toContainText(refund.outRefundNo);
    expect(writes).toHaveLength(1);
  });
}
