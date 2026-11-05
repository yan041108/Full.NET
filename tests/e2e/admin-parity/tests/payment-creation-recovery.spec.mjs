import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const problem = route => route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ type: 'about:blank', status: 404, code: 'payments.order.creation_request_not_found', title: 'Original creation request not found' }) });
const record = body => ({ id: id(4), tenantId: body.tenantId, merchantConfigId: body.merchantConfigId ?? id(3), channelKey: body.channelKey ?? 'wechat_native',
  outTradeNo: 'original-request-order', tradeStateKey: 'provider_unknown', amountMinor: body.amountMinor, currency: body.currency, subject: body.subject,
  description: body.description ?? null, codeUrl: null, providerTransactionId: null, failMessage: null, createdAtUtc: '2026-11-05T00:00:00Z', updatedAtUtc: null, paidAtUtc: null, version: 7 });
async function setup(page, locale, create = true) {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'payment', displayName: 'Payment', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false,
    permissions: ['identity.navigation.read', 'payments.orders.read', ...(create ? ['payments.orders.create'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'payments-orders', parentId: null, routeName: 'payments-orders', componentKey: 'payments-orders', path: '/payments/orders',
    title: 'Payments', caption: '', icon: 'setting', order: 1, requiredPermission: 'payments.orders.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/payments/orders?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('创建未知保留原请求并显式恢复 ' + width, async ({ page }, info) => {
    await setup(page, locale); const creates = []; let reads = 0, found = false;
    await page.route('**/api/v1/payments/orders', route => {
      expect(route.request().method()).toBe('POST'); creates.push(route.request().postDataJSON());
      return creates.length === 1 ? route.fulfill({ status: 503 }) : json(route, record(creates.at(-1)));
    });
    await page.route('**/api/v1/payments/orders/creation-requests/*?*', route => {
      reads++; expect(route.request().method()).toBe('GET'); const url = new URL(route.request().url());
      expect(url.searchParams.get('tenantId')).toBe(id(2)); expect(url.pathname.split('/').at(-1)).toBe(creates[0].idempotencyKey);
      return found ? json(route, { idempotencyKey: creates[0].idempotencyKey, order: record(creates[0]), readAtUtc: '2026-11-05T01:00:00Z' }) : problem(route);
    });
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('payment-order-create').click();
    await page.getByTestId('payment-order-create-tenant').fill(id(2)); await page.getByTestId('payment-order-create-subject').fill('Original payment');
    await page.getByTestId('payment-order-create-amount').fill('92233720368547758.07'); await page.getByTestId('payment-order-create-confirm').click();
    await expect(page.getByTestId('payment-creation-unconfirmed')).toBeVisible(); expect(creates).toHaveLength(1);
    const close = () => page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '关闭' : 'Close', exact: true }).click();
    await close(); await page.getByTestId('payment-order-create').click();
    await expect(page.getByTestId('payment-order-create-amount')).toHaveAttribute('readonly', '');
    await expect(page.getByTestId('payment-creation-original-key')).toContainText(creates[0].idempotencyKey); expect(creates).toHaveLength(1);
    await page.getByTestId('payment-creation-lookup').click(); await expect(page.getByTestId('creation-lookup-problem')).toContainText('Original creation request not found');
    expect(reads).toBe(1); expect(creates).toHaveLength(1); await close();
    await expect(page.getByTestId('payment-order-create-another')).toHaveCount(0);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('creation-locked-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('payment-order-create-confirm').click(); await expect.poll(() => creates.length).toBe(2); expect(creates[1]).toEqual(creates[0]);
    await expect(page.getByTestId('payment-order-create-amount')).toHaveCount(0);
    await expect(page.getByTestId('payment-order-creation-receipt')).toContainText(id(4));
    await page.getByTestId('payment-order-create').click(); await expect(page.getByTestId('payment-order-create-confirm')).toBeDisabled();
    await page.getByTestId('payment-order-create-another').click(); await expect(page.getByTestId('payment-order-create-amount')).toHaveValue(''); await close();
    // 独立 GET 可以找回原记录；它不重发创建或查询第三方。
    found = true; await page.getByTestId('payment-orders-creation-lookup').click();
    await page.getByTestId('creation-lookup-tenant').fill(id(2)); await page.getByTestId('creation-lookup-key').fill(creates[0].idempotencyKey);
    await page.getByTestId('creation-lookup-read').click(); await expect(page.getByTestId('creation-lookup-snapshot')).toContainText('92233720368547758.07');
    await expect(page.getByTestId('creation-lookup-snapshot')).toContainText('2026-11-05T01:00:00Z');
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('creation-readonly-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.creation-lookup').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    expect(creates).toHaveLength(2); expect(reads).toBe(2);
  });
  test('只读角色独立核对原创建请求 ' + width, async ({ page }) => {
    await setup(page, locale, false); let writes = 0;
    await page.route('**/api/v1/payments/orders', route => { writes++; return route.fulfill({ status: 403 }); });
    await page.route('**/api/v1/payments/orders/creation-requests/*?*', route => json(route, { idempotencyKey: id(1), order: record({ tenantId: id(2), amountMinor: '29', currency: 'CNY', subject: 'Readonly original' }), readAtUtc: '2026-11-05T01:00:00Z' }));
    await page.goto('/#/payments/orders'); await page.setViewportSize({ width, height: 1000 }); await expect(page.getByTestId('payment-order-create')).toHaveCount(0);
    await page.getByTestId('payment-orders-creation-lookup').click(); await page.getByTestId('creation-lookup-tenant').fill(id(2)); await page.getByTestId('creation-lookup-key').fill(id(1));
    await page.getByTestId('creation-lookup-read').click(); await expect(page.getByTestId('creation-lookup-snapshot')).toContainText('0.29 CNY'); expect(writes).toBe(0);
  });
}
