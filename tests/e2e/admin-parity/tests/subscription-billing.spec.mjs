import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const tenantId = id(1), packageId = id(2), merchantId = id(3), subscriptionId = id(4), orderId = id(5);
const permissions = ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_packages.read', 'tenancy.tenant_packages.update',
  'tenancy.tenant_entitlements.read', 'tenancy.tenant_entitlements.manage_bindings', 'tenancy.tenant_subscriptions.read',
  'tenancy.tenant_subscriptions.manage', 'payments.orders.read', 'payments.orders.create', 'payments.orders.reconcile'];
const pkg = { id: packageId, code: 'billing-plan', name: '付费套餐', description: null, isActive: true, version: 1, assignedTenantCount: 1 };
const offer = { packageId, paymentTenantId: tenantId, merchantConfigId: merchantId, amountMinor: 12500, currency: 'CNY',
  seatLimit: 20, storageLimit: 100000, durationDays: 30, entitlementIds: [id(6)], isActive: true, version: 1 };
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });

async function setup(page, allowed = permissions, locale = 'zh-CN') {
  await page.addInitScript(selected => localStorage.setItem('fullnet.admin.locale', selected), locale);
  await page.route('**/api/v1/auth/refresh', r => json(r, { accessToken: 'billing-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', r => json(r, { id: id(9), username: 'billing', displayName: 'Billing operator', actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, tenantId: null, permissions: allowed, sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', r => json(r, [
    ['tenant-packages', '/tenant-packages', 'tenancy.tenant_packages.read'], ['tenant-management', '/tenants', 'tenancy.tenants.read'],
    ['payments-orders', '/payments/orders', 'payments.orders.read']
  ].map(([key, path, requiredPermission], index) => ({ id: key, parentId: null, routeName: key, path, componentKey: key === 'tenant-management' ? 'tenants' : key,
    title: key, caption: key, icon: 'setting', order: index, requiredPermission, children: [] }))));
  await page.route('**/api/v1/tenancy/available', r => json(r, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', r => json(r, { unreadCount: 0 }));
  await page.route('**/api/v1/identity/session-policy', r => json(r, { loginPolicy: 0 }));
  await page.route('**/api/v1/me/tenant-invitations', r => json(r, []));
  await page.route('**/api/v1/tenancy/tenant-packages?*', r => json(r, { items: [pkg], total: 1, page: 1, pageSize: Number(new URL(r.request().url()).searchParams.get('pageSize')) }));
  await page.route('**/api/v1/tenancy/tenants?*', r => json(r, { items: [{ id: tenantId, identifier: 'billing', name: 'Billing tenant', domain: 'billing.localhost', isActive: true, version: 1, defaultLocale: 'zh-CN' }], total: 1, page: 1, pageSize: 20 }));
  await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions?*`, r => json(r, { items: [], total: 0, page: 1, pageSize: 20 }));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', r => json(r, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/entitlements/catalog', r => json(r, [{ id: id(6), code: 'files.feature', name: '文件功能', description: null, entitlementType: 'Feature', isActive: true, version: 1 }]));
  await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/offer`, r => json(r, offer));
}

for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
  test(`报价确认、网络重试与资金履约分离 ${viewport.width}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await setup(page);
    const requests = [];
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/paid`, r => {
      const body = r.request().postDataJSON(); requests.push(body);
      if (requests.length === 1) return json(r, { status: 503, code: 'test.response_lost', title: 'response lost' }, 503);
      return json(r, { ...offer, tenantId, subscriptionId, offerVersion: 1, paymentOperationId: id(7), subject: '付费套餐',
        effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2026-12-01T00:00:00Z' });
    });
    await page.route(`**/api/v1/payments/subscriptions/${tenantId}/${subscriptionId}/checkout`, r => {
      expect(r.request().postData()).toBeNull();
      return json(r, { subscriptionId, fulfillmentStateKey: 'ReadyPaid', errorCode: null, order: {
        id: orderId, tenantId, merchantConfigId: merchantId, channelKey: 'wechat_native', amountMinor: 12500, currency: 'CNY',
        subject: '付费套餐', description: null, outTradeNo: 'original-order', tradeStateKey: 'succeeded', codeUrl: 'weixin://original',
        providerTransactionId: 'original-paid', failMessage: null, paidAtUtc: '2026-10-27T00:00:00Z', createdAtUtc: '2026-10-27T00:00:00Z', updatedAtUtc: null, version: 1
      } });
    });
    await page.goto('/#/tenants');
    await page.getByRole('link', { name: '租户管理', exact: true }).click();
    await page.setViewportSize(viewport);
    await expect(page.getByText('Billing tenant', { exact: true })).toBeVisible();
    await page.getByTestId('tenants-action-subscriptions').click();
    await page.getByTestId('paid-subscription-create').click();
    let dialog = page.getByRole('dialog').filter({ has: page.getByTestId('paid-subscription-confirm') });
    // 未读报价之前不允许创建；确认按钮只有报价成功后出现。
    await expect(page.getByTestId('paid-subscription-confirm')).toHaveCount(0);
    dialog = page.getByRole('dialog').last();
    await dialog.getByRole('combobox').focus();
    await dialog.getByRole('combobox').press('ArrowDown');
    await page.getByRole('option', { name: '付费套餐', exact: true }).click();
    await dialog.getByRole('button', { name: '读取报价', exact: true }).click();
    await expect(dialog).toContainText('12500 CNY');
    await page.getByTestId('paid-subscription-confirm').click();
    await expect(dialog).toContainText('账单结果未确认');
    await dialog.getByRole('button', { name: '关闭', exact: true }).click();
    await page.getByTestId('paid-subscription-create').click();
    dialog = page.getByRole('dialog').last();
    await expect(dialog.getByTestId('subscription-invoice-recovery')).toBeVisible();
    expect(requests.length).toBe(1);
    await page.getByTestId('paid-subscription-confirm').click();
    await expect.poll(() => requests.length).toBe(2);
    expect(requests[1]).toEqual(requests[0]);
    expect(Object.keys(requests[0]).sort()).toEqual(['effectiveFromUtc', 'idempotencyKey', 'offerVersion', 'packageId']);
    await dialog.getByTestId('subscription-checkout').click();
    await page.getByTestId('subscription-checkout-confirm').click();
    const payment = page.getByRole('dialog').filter({ has: page.locator('input[aria-label="支付渠道链接"]') });
    await expect(payment).toContainText('资金状态');
    await expect(payment).toContainText('已收款，等待发放');
    const box = await payment.boundingBox();
    expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    await expect(page.getByTestId('subscription-checkout-confirm')).toHaveCount(0);
    await payment.getByRole('button', { name: '关闭', exact: true }).focus();
    await expect(payment.getByRole('button', { name: '关闭', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect.poll(() => payment.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await expect(page.locator('.el-loading-mask')).toHaveCount(0);
    await payment.screenshot({ path: testInfo.outputPath(`checkout-${viewport.width}.png`), animations: 'disabled' });
    const axe = await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(axe.violations).toEqual([]);
  });
}

test('报价保存携带版本与权益；缺少绑定权限时不显示编辑入口', async ({ page }) => {
  await setup(page);
  const saved = [];
  await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/offer`, r => {
    if (r.request().method() === 'PUT') { const body = r.request().postDataJSON(); saved.push(body); return json(r, { packageId, ...body, version: 2 }); }
    return json(r, offer);
  });
  await page.goto('/#/tenant-packages');
  await expect(page.getByTestId('package-offer-edit')).toBeAttached();
  await page.getByTestId('package-offer-edit').click();
  await page.getByTestId('package-offer-save').click();
  await expect.poll(() => saved.length).toBe(1);
  expect(saved[0]).toMatchObject({ amountMinor: 12500, durationDays: 30, version: 1, entitlementIds: [id(6)] });
  await setup(page, permissions.filter(code => code !== 'tenancy.tenant_entitlements.manage_bindings'));
  await page.reload();
  await expect(page.getByTestId('package-offer-edit')).toHaveCount(0);
});

test('人工重试只调用原订单履约接口，不下单或扣款', async ({ page }) => {
  await setup(page);
  await page.route('**/api/v1/payments/orders?*', r => json(r, { items: [{ id: orderId, tenantId, merchantConfigId: merchantId,
    channelKey: 'wechat_native', outTradeNo: 'original', tradeStateKey: 'succeeded', amountMinor: 12500, currency: 'CNY',
    subject: '付费套餐', createdAtUtc: '2026-10-27T00:00:00Z', paidAtUtc: '2026-10-27T00:00:00Z' }], page: 1, pageSize: 20, total: 1 }));
  let retry = 0, checkout = 0;
  const status = stateKey => ({ subscriptionId, beneficiaryTenantId: tenantId, paymentOrderId: orderId, stateKey,
    errorCode: null, attempt: 1, updatedAtUtc: '2026-10-27T00:00:00Z', effectiveFromUtc: '2026-10-01T00:00:00Z',
    nextAttemptAtUtc: stateKey === 'ReadyPaid' ? '2026-10-27T00:00:00Z' : null, readAtUtc: '2026-10-27T00:00:00Z' });
  await page.route(`**/api/v1/payments/orders/${orderId}/subscription-fulfillment`, r => json(r, status('Review')));
  await page.route(`**/api/v1/payments/orders/${orderId}/subscription-fulfillment/retry`, r => { retry++; return json(r, status('ReadyPaid')); });
  await page.route('**/checkout', r => { checkout++; return r.abort(); });
  await page.goto('/#/payments/orders');
  await expect(page.getByTestId('subscription-fulfillment')).toBeAttached();
  await page.getByTestId('subscription-fulfillment').click();
  const dialog = page.getByRole('dialog').last();
  await expect(dialog).toContainText('需要人工对账');
  await dialog.getByRole('button', { name: '重试履约', exact: true }).click();
  await expect(page.getByTestId('fulfillment-confirm-order')).toHaveText(orderId); expect(retry).toBe(0);
  await expect(page.getByTestId('fulfillment-retry-confirm')).toBeFocused();
  await page.getByTestId('fulfillment-retry-confirm').click();
  await expect(dialog).toContainText('已收款，等待发放');
  expect(retry).toBe(1); expect(checkout).toBe(0);
});


for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('人工重试结果未确认时先重读当前订单 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 }); await setup(page, permissions, locale);
    await page.route('**/api/v1/payments/orders?*', r => json(r, { items: [{ id: orderId, tenantId, merchantConfigId: merchantId,
      channelKey: 'wechat_native', outTradeNo: 'original', tradeStateKey: 'succeeded', amountMinor: 12500, currency: 'CNY',
      subject: 'Plan', createdAtUtc: '2026-10-27T00:00:00Z', paidAtUtc: '2026-10-27T00:00:00Z' }], page: 1, pageSize: 20, total: 1 }));
    let reads = 0, retries = 0, checkouts = 0;
    await page.route('**/api/v1/payments/orders/' + orderId + '/subscription-fulfillment', r => {
      reads++; return json(r, { subscriptionId, beneficiaryTenantId: tenantId, paymentOrderId: orderId,
        stateKey: reads === 1 ? 'Review' : 'ReadyPaid', errorCode: null, attempt: 1, updatedAtUtc: '2026-11-05T00:00:00Z',
        effectiveFromUtc: '2026-11-01T00:00:00Z', nextAttemptAtUtc: reads === 1 ? null : '2026-11-05T00:00:00Z', readAtUtc: '2026-11-05T00:00:00Z' });
    });
    await page.route('**/api/v1/payments/orders/' + orderId + '/subscription-fulfillment/retry', r => {
      retries++; return json(r, { status: 503, code: 'test.response_lost', title: 'unconfirmed' }, 503);
    });
    await page.route('**/checkout', r => { checkouts++; return r.abort(); });
    await page.goto('/#/payments/orders'); await page.getByTestId('subscription-fulfillment').click();
    await page.getByTestId('subscription-fulfillment-retry').click();
    await expect(page.getByTestId('fulfillment-confirm-order')).toHaveText(orderId); expect(retries).toBe(0);
    await expect(page.getByTestId('fulfillment-retry-confirm')).toBeFocused();
    await page.getByTestId('fulfillment-retry-cancel').click();
    await expect(page.getByTestId('subscription-fulfillment-retry')).toBeFocused(); expect(reads).toBe(1); expect(retries).toBe(0);
    await page.getByTestId('subscription-fulfillment-retry').click(); await expect(page.getByTestId('fulfillment-retry-confirm')).toBeFocused();
    await page.locator('.art-form-dialog').filter({ has: page.getByTestId('fulfillment-retry-confirmation') })
      .screenshot({ path: info.outputPath('fulfillment-confirm-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('fulfillment-retry-confirm').press('Enter');
    const modal = page.getByRole('dialog').last();
    await expect(modal).toContainText(locale === 'en-US' ? 'Fulfillment result unconfirmed' : '履约结果未确认');
    await expect(page.getByTestId('subscription-fulfillment-retry')).toHaveCount(0);
    expect(reads).toBe(1); expect(retries).toBe(1); expect(checkouts).toBe(0);
    await modal.screenshot({ path: info.outputPath('fulfillment-unconfirmed-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('subscription-fulfillment-refresh').click();
    await expect(modal).toContainText(locale === 'en-US' ? 'Paid, awaiting fulfillment' : '已收款，等待发放');
    expect(reads).toBe(2); expect(retries).toBe(1); expect(checkouts).toBe(0);
  });
}

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`只读履约等待生效与服务器读取时间 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    const readOnly = permissions.filter(code => !['payments.orders.create', 'payments.orders.reconcile', 'tenancy.tenant_subscriptions.manage'].includes(code));
    await setup(page, readOnly, locale);
    await page.route('**/api/v1/payments/orders?*', r => json(r, { items: [{ id: orderId, tenantId, merchantConfigId: merchantId,
      channelKey: 'wechat_native', outTradeNo: 'original', tradeStateKey: 'succeeded', amountMinor: 12500, currency: 'CNY',
      subject: 'Scheduled plan', createdAtUtc: '2026-11-05T00:00:00Z', paidAtUtc: '2026-11-05T00:00:00Z' }], page: 1, pageSize: 20, total: 1 }));
    let reads = 0, writes = 0;
    const effective = '2026-12-01T00:00:00Z';
    await page.route('**/api/v1/payments/orders/' + orderId + '/subscription-fulfillment', r => {
      reads++; return json(r, { subscriptionId, beneficiaryTenantId: tenantId, paymentOrderId: orderId, stateKey: 'ReadyPaid',
        errorCode: null, attempt: 0, updatedAtUtc: '2026-11-05T00:00:00Z', effectiveFromUtc: effective, nextAttemptAtUtc: effective,
        readAtUtc: reads === 1 ? '2026-11-05T00:00:00Z' : '2026-12-02T00:00:00Z' });
    });
    await page.route('**/api/v1/payments/orders/' + orderId + '/subscription-fulfillment/retry', r => { writes++; return json(r, {}, 405); });
    await page.goto('/#/payments/orders'); await expect(page.getByTestId('subscription-fulfillment')).toBeAttached();
    await page.setViewportSize({ width, height: 1000 }); await page.getByTestId('subscription-fulfillment').click();
    const modal = page.locator('.art-form-dialog').filter({ has: page.getByTestId('fulfillment-read-at') });
    await expect(page.getByTestId('fulfillment-waiting')).toBeVisible();
    await expect(page.getByTestId('fulfillment-effective')).toHaveAttribute('datetime', effective);
    await expect(page.getByTestId('fulfillment-next')).toHaveAttribute('datetime', effective);
    await expect(page.getByTestId('fulfillment-read-at')).toHaveAttribute('datetime', '2026-11-05T00:00:00Z');
    await expect(page.getByTestId('subscription-fulfillment-retry')).toHaveCount(0); expect(reads).toBe(1); expect(writes).toBe(0);
    await modal.screenshot({ path: info.outputPath('fulfillment-schedule-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('subscription-fulfillment-refresh').click(); await expect(page.getByTestId('fulfillment-waiting')).toHaveCount(0);
    await expect(page.getByTestId('fulfillment-read-at')).toHaveAttribute('datetime', '2026-12-02T00:00:00Z'); expect(reads).toBe(2); expect(writes).toBe(0);
  });
}

for (const width of [1440, 390]) {
  test('同套餐续期固定起点，结果不明原样恢复 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); await setup(page);
    const source = { id: subscriptionId, tenantId, packageId, status: 'Active', version: 2, trialEndsAtUtc: null,
      currentPeriodStartUtc: '2026-11-01T00:00:00Z', currentPeriodEndUtc: '2026-12-01T00:00:00Z', cancelledAtUtc: null };
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions?*`, r => json(r, { items: [source], total: 1, page: 1, pageSize: 20 }));
    let previews = 0, purchases = [], checkouts = 0;
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/renewal-preview`, r => {
      previews++; return json(r, { tenantId, sourceSubscriptionId: subscriptionId, sourceVersion: 2,
        effectiveFromUtc: source.currentPeriodEndUtc, readAtUtc: '2026-11-05T00:00:00Z', offer });
    });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/paid`, r => {
      const body = r.request().postDataJSON(); purchases.push(body);
      if (purchases.length === 1) return json(r, { status: 503, code: 'test.unconfirmed', title: 'Unconfirmed' }, 503);
      return json(r, { subscriptionId: id(20), tenantId, paymentTenantId: tenantId, paymentOperationId: id(21), merchantConfigId: merchantId,
        packageId, offerVersion: offer.version, amountMinor: offer.amountMinor, currency: offer.currency,
        effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: '2026-12-31T00:00:00Z', subject: '付费套餐',
        renewalOfSubscriptionId: subscriptionId, renewalSourceVersion: 2 });
    });
    await page.route('**/checkout', r => { checkouts++; return r.abort(); });
    await page.goto('/#/tenants'); await page.getByRole('link', { name: '租户管理', exact: true }).click();
    await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('tenants-action-subscriptions').click(); await page.getByTestId('subscription-renewal').click();
    let modal = page.getByRole('dialog').last(); await expect(modal).toContainText(subscriptionId);
    await expect(page.getByTestId('paid-subscription-start')).toHaveAttribute('readonly', '');
    await modal.getByRole('button', { name: '读取报价', exact: true }).click();
    await expect(page.getByTestId('paid-subscription-start')).toHaveValue(source.currentPeriodEndUtc);
    expect(purchases).toHaveLength(0); expect(previews).toBe(1);
    const axe = await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze();
    expect(axe.violations).toEqual([]);
    await modal.screenshot({ path: info.outputPath('renewal-preview-' + width + '.png'), animations: 'disabled' });
    await page.getByTestId('paid-subscription-confirm').click(); await expect(page.getByTestId('subscription-invoice-recovery')).toBeVisible();
    await modal.getByRole('button', { name: '关闭', exact: true }).click(); await page.getByTestId('subscription-renewal').click();
    await page.getByTestId('paid-subscription-confirm').click();
    modal = page.getByRole('dialog').last(); await expect(modal).toContainText(id(20));
    await expect(modal).toContainText(source.currentPeriodEndUtc);
    expect(purchases).toHaveLength(2); expect(purchases[1]).toEqual(purchases[0]);
    expect(purchases[0]).toMatchObject({ renewalOfSubscriptionId: subscriptionId, renewalSourceVersion: 2,
      effectiveFromUtc: source.currentPeriodEndUtc, packageId, offerVersion: offer.version });
    expect(previews).toBe(1); expect(checkouts).toBe(0);
    await modal.screenshot({ path: info.outputPath('renewal-created-' + width + '.png'), animations: 'disabled' });
  });
}

for (const width of [1440, 390]) {
  test('停用企业原冻结账单只读回读保留精确金额 ' + width, async ({ page }, info) => {
    const readOnly = ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_subscriptions.read'];
    await page.setViewportSize({ width: 1440, height: 1000 }); await setup(page, readOnly);
    const row = { id: subscriptionId, tenantId, packageId, status: 'Cancelled', version: 4, trialEndsAtUtc: null,
      currentPeriodStartUtc: '2026-09-01T00:00:00Z', currentPeriodEndUtc: '2026-10-01T00:00:00Z', cancelledAtUtc: '2026-09-15T00:00:00Z' };
    await page.route('**/api/v1/tenancy/tenants?*', r => json(r, { items: [{ id: tenantId, identifier: 'billing', name: 'Historical tenant', domain: 'billing.localhost', isActive: false, version: 4, defaultLocale: 'zh-CN' }], total: 1, page: 1, pageSize: 20 }));
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions?*`, r => json(r, { items: [row], total: 1, page: 1, pageSize: 20 }));
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}`, r => json(r, row));
    const invoice = { subscriptionId, tenantId, packageId, subject: '原冻结商品名称', offerVersion: 7, amountMinor: '9007199254740993', currency: 'CNY',
      seatLimit: '9223372036854775807', storageLimit: '9223372036854775806', effectiveFromUtc: row.currentPeriodStartUtc, effectiveToUtc: row.currentPeriodEndUtc,
      phase: 'Refunded', renewalOfSubscriptionId: id(30), renewalSourceVersion: 3, paymentOrderId: orderId, readAtUtc: '2026-11-05T00:00:00Z' };
    let reads = 0, writes = 0;
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/invoice`, r => {
      reads++; return reads === 1 ? json(r, invoice) : reads === 2 ? json(r, { status: 503, title: 'temporary', code: 'test.temporary' }, 503)
        : json(r, { status: 404, title: 'missing', code: 'tenancy.subscription_billing.not_found' }, 404);
    });
    await page.route('**/api/v1/**', r => {
      if (r.request().method() !== 'GET' && /\/api\/v1\/(?:tenancy|payments)\//.test(r.request().url())) { writes++; return r.abort(); }
      return r.fallback();
    });
    await page.goto('/#/tenants'); await page.getByRole('link', { name: '租户管理', exact: true }).click();
    await page.setViewportSize({ width, height: 1000 }); await page.getByTestId('tenants-action-subscriptions').click();
    await page.getByTestId('tenant-subscription-detail').click(); await expect(page.getByTestId('subscription-detail-status')).toContainText('已取消');
    expect(reads).toBe(0); await page.getByTestId('subscription-invoice-read').click();
    const snapshot = page.getByTestId('subscription-invoice-snapshot');
    await expect(page.getByTestId('invoice-exact-amount')).toHaveText(invoice.amountMinor);
    await expect(snapshot).toContainText(invoice.renewalOfSubscriptionId); await expect(snapshot).toContainText(invoice.seatLimit);
    await expect(snapshot).toContainText('已接收退款事实');
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await snapshot.getByText(invoice.readAtUtc, { exact: true }).scrollIntoViewIfNeeded();
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('frozen-invoice-' + width + '.png'), animations: 'disabled' });
    await page.getByTestId('subscription-invoice-read').click(); await expect(page.getByTestId('subscription-invoice-failure')).toContainText('保留');
    await expect(snapshot).toContainText(invoice.readAtUtc); await page.getByTestId('subscription-invoice-read').click();
    await expect(snapshot).toHaveCount(0); await expect(page.getByTestId('subscription-invoice-failure')).toContainText('不存在或不可读取');
    expect(reads).toBe(3); expect(writes).toBe(0);
  });
}

for (const width of [1440, 390]) {
  test('原账单来源键盘追溯不改变历史列表 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); await setup(page, ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_subscriptions.read']);
    const sourceId = id(30);
    const child = { id: subscriptionId, tenantId, packageId, status: 'Active', version: 2, trialEndsAtUtc: null,
      currentPeriodStartUtc: '2026-11-01T00:00:00Z', currentPeriodEndUtc: '2026-12-01T00:00:00Z', cancelledAtUtc: null };
    const source = { ...child, id: sourceId, status: 'Expired', version: 5, currentPeriodStartUtc: '2026-10-01T00:00:00Z', currentPeriodEndUtc: child.currentPeriodStartUtc };
    let lists = 0, invoiceReads = 0, writes = 0; const detailIds = [];
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions?*`, r => { lists++; return json(r, { items: [child], total: 1, page: 1, pageSize: 20 }); });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}`, r => { detailIds.push(subscriptionId); return json(r, child); });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${sourceId}`, r => { detailIds.push(sourceId); return json(r, source); });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/invoice`, r => {
      invoiceReads++; return json(r, { subscriptionId, tenantId, packageId, subject: '独立续期账单', offerVersion: 2, amountMinor: '25000', currency: 'CNY',
        seatLimit: '20', storageLimit: '100000', effectiveFromUtc: child.currentPeriodStartUtc, effectiveToUtc: child.currentPeriodEndUtc, phase: 'Applied',
        renewalOfSubscriptionId: sourceId, renewalSourceVersion: 3, paymentOrderId: orderId, readAtUtc: '2026-11-05T00:00:00Z' });
    });
    await page.route('**/api/v1/**', r => {
      if (r.request().method() !== 'GET' && /\/api\/v1\/(?:tenancy|payments)\//.test(r.request().url())) { writes++; return r.abort(); }
      return r.fallback();
    });
    await page.goto('/#/tenants'); await page.getByRole('link', { name: '租户管理', exact: true }).click(); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('tenants-action-subscriptions').click(); await page.getByTestId('tenant-subscription-detail').click();
    await page.getByTestId('subscription-invoice-read').click(); await expect(page.getByTestId('subscription-invoice-source')).toBeVisible();
    await page.getByTestId('subscription-invoice-source').focus(); await page.keyboard.press('Enter');
    await expect(page.getByTestId('subscription-detail-id')).toHaveText(sourceId); await expect(page.getByTestId('subscription-detail-status')).toHaveText('已到期');
    await expect(page.getByTestId('subscription-detail-refresh')).toBeFocused(); await expect(page.getByTestId('subscription-invoice-snapshot')).toHaveCount(0);
    expect(detailIds).toEqual([subscriptionId, sourceId]); expect(lists).toBe(1); expect(invoiceReads).toBe(1); expect(writes).toBe(0);
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('renewal-source-' + width + '.png'), animations: 'disabled' });
  });
}
