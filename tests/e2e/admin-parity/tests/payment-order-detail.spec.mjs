import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-readonly-order',
  tradeStateKey: 'refunding', amountMinor: '9007199254740990', currency: 'CNY', subject: 'Original payment', description: 'Original description',
  codeUrl: 'https://example.com/payment-link', providerTransactionId: 'original-provider-transaction', failMessage: null,
  createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: '2026-11-05T00:00:00Z', version: 7 };
const refund = { id: id(5), tenantId: order.tenantId, orderId: order.id, merchantConfigId: order.merchantConfigId, outTradeNo: order.outTradeNo,
  outRefundNo: 'original-refund', refundStateKey: 'processing', amountMinor: '29', currency: 'CNY', reason: 'original reason',
  providerRefundId: 'provider-refund', failMessage: null, createdAtUtc: order.createdAtUtc, updatedAtUtc: null, completedAtUtc: null, version: 1 };

async function mockPaymentIdentity(page, locale, reconcile = false) {
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'operator', displayName: 'Payment operator', actorScope: 'host', scope: 'host',
    tenantId: null, isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'payments.orders.read', 'payments.refunds.read', ...(reconcile ? ['payments.orders.reconcile'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [['payments-orders', '/payments/orders', 'payments.orders.read'], ['payments-refunds', '/payments/refunds', 'payments.refunds.read']]
    .map(([key, path, permission], index) => ({ id: key, parentId: null, routeName: key, componentKey: key, path, title: key,
      caption: '', icon: 'setting', order: index, requiredPermission: permission, children: [] }))));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
}

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('列表外订单精确读取及退款追溯原订单 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    let writes = 0, paymentLinks = 0, orderReads = 0; let mode = 'ok', showRow = false;
    await page.route('https://example.com/payment-link', route => { paymentLinks++; return route.fulfill({ status: 404 }); });
    await page.route('**/api/v1/payments/**', route => { if (route.request().method() !== 'GET') writes++; return route.fulfill({ status: 404 }); });
    await mockPaymentIdentity(page, locale);
    await page.route('**/api/v1/payments/orders?*', route => json(route, { items: showRow ? [order] : [], page: 1, pageSize: 20, total: showRow ? 1 : 0 }));
    await page.route(`**/api/v1/payments/orders/${order.id}`, route => {
      expect(route.request().method()).toBe('GET'); orderReads++;
      if (mode === '503') return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'test.unavailable', type: 'about:blank', title: 'Temporarily unavailable', status: 503 }) });
      return json(route, mode === 'mismatch' ? { ...order, currency: 'USD' } : order);
    });
    await page.route('**/api/v1/payments/refunds?*', route => json(route, { items: [refund], page: 1, pageSize: 20, total: 1 }));
    await page.route(`**/api/v1/payments/refunds/${refund.id}`, route => json(route, refund));
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    await expect(page.getByTestId('payment-order-create')).toHaveCount(0);
    await page.getByTestId('payment-order-id-input').fill('bad'); await page.getByTestId('payment-order-id-read').click(); expect(orderReads).toBe(0);
    await page.getByTestId('payment-order-id-input').fill(order.id); await page.getByTestId('payment-order-id-input').press('Enter');
    await expect(page.getByTestId('payment-order-detail-amount')).toHaveText('90071992547409.90 CNY');
    await expect(page.getByTestId('payment-order-detail-snapshot')).toContainText(order.providerTransactionId);
    expect(orderReads).toBe(1); expect(paymentLinks).toBe(0);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-order-exact-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.order-detail-content').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mode = '503'; await page.getByTestId('payment-order-detail-refresh').click();
    await expect(page.getByTestId('payment-order-detail-problem')).toContainText('Temporarily unavailable');
    await expect(page.getByTestId('payment-order-detail-snapshot')).toHaveCount(0);
    await expect(page.getByTestId('payment-order-detail-id')).toHaveText(order.id);
    await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click();
    mode = 'ok'; showRow = true;
    await page.locator('.payment-orders-view').getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await page.getByTestId('payment-order-detail-' + order.id).click();
    await expect(page.getByTestId('payment-order-detail-snapshot')).toContainText(order.outTradeNo); expect(orderReads).toBe(3);
    await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click();
    await page.goto('/#/payments/refunds');
    await page.getByTestId('refunds-exact-id').fill(refund.id); await page.getByTestId('refunds-read-id').click();
    await expect(page.getByTestId('refund-detail-snapshot')).toBeVisible();
    await page.getByTestId('refund-read-original-order').click();
    await expect(page.getByTestId('payment-order-detail-snapshot')).toContainText(order.outTradeNo);
    await expect(page.getByTestId('payment-order-detail-id')).toHaveText(order.id); expect(orderReads).toBe(4);
    await expect(page.getByTestId('payment-order-detail-reconcile-refund')).toHaveCount(0);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('payment-order-from-refund-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.order-detail-content').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mode = 'mismatch'; await page.getByTestId('payment-order-detail-refresh').click();
    await expect(page.getByTestId('payment-order-detail-problem')).toContainText(locale === 'zh-CN' ? '原退款归属不一致' : 'original refund ownership');
    await expect(page.getByTestId('payment-order-detail-snapshot')).toHaveCount(0);
    expect(orderReads).toBe(5); expect(writes).toBe(0); expect(paymentLinks).toBe(0);
  });
}

test('处理中退款从原订单确认对账后只重读原退款', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await mockPaymentIdentity(page, 'zh-CN', true);
  const paymentWrites = [];
  const refundReads = [];
  let orderReads = 0;
  let reconciled = false;
  page.on('request', request => {
    const path = new URL(request.url()).pathname;
    if (path.startsWith('/api/v1/payments/') && request.method() !== 'GET') paymentWrites.push(path);
  });
  await page.route('**/api/v1/payments/refunds?*', route => json(route, { items: [refund], page: 1, pageSize: 20, total: 1 }));
  await page.route(`**/api/v1/payments/refunds/${refund.id}`, route => {
    refundReads.push(new URL(route.request().url()).pathname);
    return json(route, reconciled ? { ...refund, refundStateKey: 'succeeded', version: 2 } : refund);
  });
  await page.route(`**/api/v1/payments/orders/${order.id}`, route => { orderReads++; return json(route, order); });
  await page.route(`**/api/v1/payments/orders/${order.id}/reconcile`, route => {
    expect(route.request().method()).toBe('POST');
    reconciled = true;
    return json(route, { ...order, tradeStateKey: 'refunded', version: 8 });
  });

  await page.goto('/#/payments/refunds');
  await page.getByTestId('refunds-exact-id').fill(refund.id);
  await page.getByTestId('refunds-read-id').click();
  await page.getByTestId('refund-read-original-order').click();
  await page.getByTestId('payment-order-detail-reconcile-refund').click();
  await expect(page.getByTestId('payment-reconcile-snapshot')).toBeVisible();
  expect(orderReads).toBe(2);
  expect(paymentWrites).toHaveLength(0);
  await page.getByTestId('payment-reconcile-confirm').click();
  await expect(page.getByTestId('refund-detail-snapshot')).toBeVisible();
  expect(refundReads).toEqual(Array(2).fill(`/api/v1/payments/refunds/${refund.id}`));
  expect(paymentWrites).toEqual([`/api/v1/payments/orders/${order.id}/reconcile`]);
});
