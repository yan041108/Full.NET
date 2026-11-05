import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
const order = { id: id(1), tenantId: id(2), merchantConfigId: id(3), channelKey: 'wechat_native', outTradeNo: 'original-order',
  tradeStateKey: 'refunding', amountMinor: '100', currency: 'CNY', subject: 'Original', description: null, codeUrl: null,
  providerTransactionId: 'original-paid', failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: '2026-11-05T00:00:00Z', version: 3 };
const refund = { id: id(4), tenantId: order.tenantId, orderId: order.id, merchantConfigId: order.merchantConfigId, outTradeNo: order.outTradeNo,
  outRefundNo: 'original-refund', refundStateKey: 'provider_unknown', amountMinor: '29', currency: 'CNY', reason: 'Original reason',
  providerRefundId: null, failMessage: 'Provider result unconfirmed', createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, completedAtUtc: null, version: 2 };

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('原订单定位退款分页与精确只读详情 ' + width, async ({ page }, info) => {
    const writes = [], queries = [], details = []; let mode = 'success';
    page.on('request', request => { if (request.url().includes('/api/v1/') && request.method() !== 'GET' && !request.url().includes('/auth/refresh')) writes.push(request.url()); });
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
    await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
    await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
    await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'refund-reader', displayName: 'Refund reader', actorScope: 'host', scope: 'host',
      tenantId: null, isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
      permissions: ['identity.navigation.read', 'payments.orders.read', 'payments.refunds.read'] }));
    await page.route('**/api/v1/navigation', route => json(route, [['payments-orders', '/payments/orders', 'payments.orders.read'], ['payments-refunds', '/payments/refunds', 'payments.refunds.read']]
      .map(([key, path, permission], index) => ({ id: key, parentId: null, routeName: key, componentKey: key, path, title: key, caption: '',
        icon: 'setting', order: index, requiredPermission: permission, children: [] }))));
    await page.route('**/api/v1/tenancy/available', route => json(route, []));
    await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
    await page.route('**/api/v1/payments/orders?*', route => json(route, { items: [order], page: 1, pageSize: 20, total: 1 }));
    await page.route('**/api/v1/payments/refunds?*', route => {
      const query = Object.fromEntries(new URL(route.request().url()).searchParams); queries.push(query);
      expect(route.request().method()).toBe('GET');
      const current = Number(query.page); return json(route, { items: [{ ...refund, id: current === 2 ? id(5) : refund.id,
        outRefundNo: current === 2 ? 'second-page-refund' : refund.outRefundNo, refundStateKey: query.refundStateKey ?? refund.refundStateKey }], page: current, pageSize: 20, total: 42 });
    });
    await page.route(`**/api/v1/payments/refunds/${id(90)}`, route => {
      expect(route.request().method()).toBe('GET'); details.push(id(90));
      if (mode !== 'success') return json(route, { type: 'about:blank', status: mode === 'denied' ? 403 : 503, code: 'test.' + mode,
        title: mode === 'denied' ? 'Read access denied' : 'Read failed' }, mode === 'denied' ? 403 : 503);
      return json(route, { ...refund, id: id(90), outRefundNo: 'exact-refund-outside-page' });
    });
    await page.goto('/#/payments/orders');
    await expect(page.getByTestId('payment-order-create')).toHaveCount(0); await expect(page.getByTestId('payment-order-refund-' + order.id)).toHaveCount(0);
    await page.getByTestId('payment-order-refund-records-' + order.id).click();
    await expect(page).toHaveURL(new RegExp('orderId=' + order.id));
    await expect(page.getByTestId('refunds-filter-order')).toHaveValue(order.id);
    await expect.poll(() => queries.length).toBe(1); expect(queries[0]).toMatchObject({ orderId: order.id, page: '1' });
    await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('refunds-pagination').getByRole('button').last().click();
    await expect(page.locator('.payment-refunds-view .el-table__body-wrapper')).toContainText('second-page-refund');
    expect(queries.at(-1)).toMatchObject({ orderId: order.id, page: '2' });
    const status = page.getByTestId('refunds-filter-state').getByRole('combobox'); await status.focus(); await status.press('ArrowDown');
    await page.getByRole('option', { name: locale === 'en-US' ? 'Provider result unconfirmed' : '渠道结果未确认', exact: true }).click();
    await page.getByTestId('refunds-search').click(); await expect.poll(() => queries.at(-1)?.page).toBe('1');
    await expect(page.locator('.payment-refunds-view')).toHaveAttribute('aria-busy', 'false');
    expect(queries.at(-1)).toMatchObject({ orderId: order.id, refundStateKey: 'provider_unknown' }); expect(details).toHaveLength(0);
    await page.getByTestId('refunds-exact-id').fill(id(90)); await page.getByTestId('refunds-read-id').click();
    await expect(page.getByTestId('refund-detail-id')).toHaveText(id(90));
    await expect(page.getByTestId('refund-detail-snapshot')).toContainText('exact-refund-outside-page');
    await expect(page.getByTestId('refund-detail-amount')).toHaveText('0.29 CNY');
    const modal = page.getByRole('dialog').last();
    await expect(modal).toContainText(order.id); await expect(modal).toContainText('Original reason');
    await modal.screenshot({ path: info.outputPath('refund-record-detail-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    mode = 'failed'; await page.getByTestId('refund-detail-refresh').click(); await expect(page.getByTestId('refund-detail-problem')).toContainText('Read failed');
    await expect(page.getByTestId('refund-detail-snapshot')).toHaveCount(0); await expect(page.getByTestId('refund-detail-id')).toHaveText(id(90));
    mode = 'denied'; await page.getByTestId('refund-detail-refresh').click(); await expect(page.getByTestId('refund-detail-problem')).toContainText('Read access denied');
    await expect(page.getByTestId('refund-detail-snapshot')).toHaveCount(0); expect(details).toHaveLength(3); expect(queries).toHaveLength(3);
    await modal.getByRole('button', { name: locale === 'en-US' ? 'Close' : '关闭', exact: true }).click();
    await expect(modal).toBeHidden();
    await page.locator('.payment-refunds-view').screenshot({ path: info.outputPath('refund-record-list-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.payment-refunds-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('refunds-reset').click(); await expect(page.getByTestId('refunds-filter-order')).toHaveValue('');
    await expect.poll(() => queries.length).toBe(4); expect(queries.at(-1)).not.toHaveProperty('orderId'); expect(writes).toEqual([]);
  });
}
