import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'refunding', amountMinor: '29', currency: 'CNY', subject: 'Original payment', description: null, codeUrl: null,
  providerTransactionId: 'paid', failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 1 };
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('订单服务端筛选及已提交条件分页 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    let writes = 0, mismatch = false; const reads = [];
    await page.route('**/api/v1/payments/**', route => { if (route.request().method() !== 'GET') writes++; return route.fulfill({ status: 404 }); });
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'readonly', displayName: 'Read only', actorScope: 'host', scope: 'host',
      tenantId: null, isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
      permissions: ['identity.navigation.read', 'payments.orders.read'] }));
    await page.route('**/api/v1/navigation', route => json(route, [{ id: 'payments-orders', parentId: null, routeName: 'payments-orders', componentKey: 'payments-orders',
      path: '/payments/orders', title: 'Payments', caption: '', icon: 'setting', order: 1, requiredPermission: 'payments.orders.read', children: [] }]));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/payments/orders?*', route => {
      expect(route.request().method()).toBe('GET'); const query = Object.fromEntries(new URL(route.request().url()).searchParams); reads.push(query);
      return json(route, { items: [{ ...order, tenantId: query.tenantId ?? order.tenantId,
        tradeStateKey: mismatch ? 'closed' : query.tradeStateKey ?? order.tradeStateKey }], page: Number(query.page), pageSize: 20, total: 60 });
    });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    await expect.poll(() => reads.length).toBe(1);
    await page.getByTestId('payment-orders-filter-tenant').fill(order.tenantId);
    await page.getByTestId('payment-orders-filter-trade').fill('original');
    await page.getByTestId('payment-orders-filter-state').click();
    await page.getByRole('option', { name: locale === 'zh-CN' ? '退款中' : 'Refunding', exact: true }).click();
    await page.getByTestId('payment-orders-search').click(); await expect.poll(() => reads.length).toBe(2);
    expect(reads.at(-1)).toEqual({ page: '1', pageSize: '20', tenantId: order.tenantId, tradeStateKey: 'refunding', outTradeNoContains: 'original' });
    await page.getByTestId('payment-orders-filter-tenant').fill(id(4));
    await page.locator('.payment-orders-view .el-pagination .btn-next').click(); await expect.poll(() => reads.length).toBe(3);
    expect(reads.at(-1)).toEqual({ page: '2', pageSize: '20', tenantId: order.tenantId, tradeStateKey: 'refunding', outTradeNoContains: 'original' });
    await expect(page.locator('.payment-orders-view')).toHaveAttribute('aria-busy', 'false');
    await page.screenshot({ path: info.outputPath('payment-order-filters-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-orders-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-orders-filter-tenant').fill('bad'); await page.getByTestId('payment-orders-search').click();
    await expect(page.locator('.payment-orders-view')).toContainText(locale === 'zh-CN' ? '不会回退为全部订单' : 'No fallback to all orders'); expect(reads).toHaveLength(3);
    await expect(page.locator('.el-table__body-wrapper')).not.toContainText(order.outTradeNo);
    await page.getByTestId('payment-orders-reset').click(); await expect.poll(() => reads.length).toBe(4);
    expect(reads.at(-1)).toEqual({ page: '1', pageSize: '20' });
    await page.getByTestId('payment-orders-filter-state').click();
    await page.getByRole('option', { name: locale === 'zh-CN' ? '失败' : 'Failed', exact: true }).click();
    await page.getByTestId('payment-orders-search').click(); await expect.poll(() => reads.length).toBe(5);
    mismatch = true; await page.locator('.payment-orders-view').getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await expect.poll(() => reads.length).toBe(6);
    await expect(page.locator('.payment-orders-view')).toContainText(locale === 'zh-CN' ? '订单分页与已提交的筛选条件不一致' : 'does not match the submitted filters');
    await expect(page.locator('.el-table__body-wrapper')).not.toContainText(order.outTradeNo);
    mismatch = false; await page.getByTestId('payment-orders-reset').click(); await expect.poll(() => reads.length).toBe(7);
    expect(reads.at(-1)).toEqual({ page: '1', pageSize: '20' }); expect(writes).toBe(0);
  });
}
