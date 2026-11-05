import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-reconciliation-order',
  tradeStateKey: 'provider_unknown', amountMinor: '9223372036854775807', currency: 'CNY', subject: 'Original', description: null,
  codeUrl: null, providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 7 };
async function setup(page, locale, reconcile = true) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'payment', displayName: 'Payment', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'payments.orders.read', ...(reconcile ? ['payments.orders.reconcile'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'payments-orders', parentId: null, routeName: 'payments-orders', componentKey: 'payments-orders', path: '/payments/orders',
    title: 'Payments', caption: '', icon: 'setting', order: 1, requiredPermission: 'payments.orders.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/payments/orders?*', route => json(route, { items: [order], page: 1, pageSize: 20, total: 1 }));
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('人工对账先确认原订单且未知重开不自动重发 ' + width, async ({ page }, info) => {
    await setup(page, locale); let reads = 0, writes = 0, readFails = false;
    await page.route(`**/api/v1/payments/orders/${order.id}`, route => { reads++; expect(route.request().method()).toBe('GET');
      return readFails ? route.fulfill({ status: 503 }) : json(route, order); });
    await page.route(`**/api/v1/payments/orders/${order.id}/reconcile`, route => { writes++; expect(route.request().method()).toBe('POST');
      expect(route.request().postData()).toBeNull(); return writes === 1 ? route.fulfill({ status: 503 }) : json(route, { ...order, version: 8, tradeStateKey: 'awaiting_payment' }); });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    const open = () => page.getByTestId('payment-order-reconcile-' + order.id).click();
    const close = async () => { await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click();
      await expect(page.getByRole('dialog')).toHaveCount(0); };
    await open(); await expect(page.getByTestId('payment-reconcile-snapshot')).toContainText('92233720368547758.07'); expect(writes).toBe(0); expect(reads).toBe(1);
    await expect(page.getByTestId('payment-reconcile-snapshot')).toContainText(order.merchantConfigId);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('reconcile-confirm-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-reconcile').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await close(); expect(writes).toBe(0); await open(); await expect(page.getByTestId('payment-reconcile-confirm')).toBeEnabled(); expect(reads).toBe(2);
    await page.getByTestId('payment-reconcile-confirm').click(); await expect(page.getByTestId('payment-reconcile-unconfirmed')).toBeVisible(); expect(writes).toBe(1);
    await close(); await open(); await expect(page.getByTestId('payment-reconcile-confirm')).toBeDisabled(); expect(reads).toBe(2); expect(writes).toBe(1);
    await page.getByTestId('payment-reconcile-read').click(); await expect(page.getByTestId('payment-reconcile-confirm')).toBeEnabled(); expect(reads).toBe(3);
    await page.getByTestId('payment-reconcile-confirm').click(); await expect(page.getByTestId('payment-reconcile-accepted')).toBeVisible(); expect(writes).toBe(2);
    await expect(page.getByTestId('payment-reconcile-snapshot')).toContainText(locale === 'zh-CN' ? '待支付' : 'Awaiting payment');
    await expect(page.getByTestId('payment-reconcile-confirm')).toBeDisabled();
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('reconcile-receipt-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-reconcile').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await close(); await open(); expect(reads).toBe(3); await expect(page.getByTestId('payment-reconcile-confirm')).toBeDisabled();
    readFails = true; await page.getByTestId('payment-reconcile-read').click(); await expect(page.getByTestId('payment-reconcile-problem')).toBeVisible();
    await expect(page.getByTestId('payment-reconcile-snapshot')).toHaveCount(0); await expect(page.getByTestId('payment-reconcile-confirm')).toBeDisabled(); expect(writes).toBe(2);
  });
  test('只读角色没有对账确认入口 ' + width, async ({ page }) => {
    await setup(page, locale, false); let writes = 0;
    await page.route('**/api/v1/payments/orders/*/reconcile', route => { writes++; return route.fulfill({ status: 403 }); });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    await expect(page.getByTestId('payment-order-detail-' + order.id)).toBeVisible();
    await expect(page.getByTestId('payment-order-reconcile-' + order.id)).toHaveCount(0); expect(writes).toBe(0);
  });
}
