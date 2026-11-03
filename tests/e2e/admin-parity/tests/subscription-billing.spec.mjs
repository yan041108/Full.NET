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

async function setup(page, allowed = permissions) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/auth/refresh', r => json(r, { accessToken: 'billing-test', tokenType: 'Bearer', expiresAtUtc: new Date(Date.now() + 3600000).toISOString() }));
  await page.route('**/api/v1/me', r => json(r, { id: id(9), username: 'billing', displayName: 'Billing operator', actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, tenantId: null, permissions: allowed, sessionId: id(10), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
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
    await expect(dialog).toContainText('操作失败');
    await page.getByTestId('paid-subscription-confirm').click();
    await expect.poll(() => requests.length).toBe(2);
    expect(requests[1]).toEqual(requests[0]);
    expect(Object.keys(requests[0]).sort()).toEqual(['effectiveFromUtc', 'idempotencyKey', 'offerVersion', 'packageId']);
    await dialog.getByTestId('subscription-checkout').click();
    await page.getByTestId('subscription-checkout-confirm').click();
    const payment = page.getByRole('dialog').filter({ has: page.getByTestId('subscription-checkout-confirm') });
    await expect(payment).toContainText('资金状态');
    await expect(payment).toContainText('已收款，等待发放');
    const box = await payment.boundingBox();
    expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(viewport.width);
    await expect(payment.getByTestId('subscription-checkout-confirm')).toBeEnabled();
    await payment.getByTestId('subscription-checkout-confirm').focus();
    await expect(payment.getByTestId('subscription-checkout-confirm')).toBeFocused();
    await page.keyboard.press('Tab');
    await expect.poll(() => payment.evaluate(element => element.contains(document.activeElement))).toBe(true);
    await payment.screenshot({ path: testInfo.outputPath(`checkout-${viewport.width}.png`) });
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
    errorCode: null, attempt: 1, updatedAtUtc: '2026-10-27T00:00:00Z' });
  await page.route(`**/api/v1/payments/orders/${orderId}/subscription-fulfillment`, r => json(r, status('Review')));
  await page.route(`**/api/v1/payments/orders/${orderId}/subscription-fulfillment/retry`, r => { retry++; return json(r, status('ReadyPaid')); });
  await page.route('**/checkout', r => { checkout++; return r.abort(); });
  await page.goto('/#/payments/orders');
  await expect(page.getByTestId('subscription-fulfillment')).toBeAttached();
  await page.getByTestId('subscription-fulfillment').click();
  const dialog = page.getByRole('dialog').last();
  await expect(dialog).toContainText('需要人工对账');
  await dialog.getByRole('button', { name: '重试履约', exact: true }).click();
  await expect(dialog).toContainText('已收款，等待发放');
  expect(retry).toBe(1); expect(checkout).toBe(0);
});
