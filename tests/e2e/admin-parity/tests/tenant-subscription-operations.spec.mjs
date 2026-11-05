import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const tenantId = id(1), packageId = id(2), chosenId = id(3), subscriptionId = id(4);
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
async function setup(page, { readOnly = false, active = true, rows = [] } = {}) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'operator', displayName: 'Operator', actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, tenantId: null, permissions: ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_packages.read', 'tenancy.tenant_subscriptions.read',
      ...(readOnly ? [] : ['tenancy.tenant_subscriptions.manage'])], sessionId: id(10), preferredLocale: 'zh-CN', profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'tenants', parentId: null, routeName: 'tenant-management', path: '/tenants', componentKey: 'tenants',
    title: '租户管理', caption: '', icon: 'setting', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', route => json(route, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/tenant-packages?*', route => json(route, { items: [
    { id: packageId, code: 'first', name: '默认首个套餐', description: null, isActive: true, version: 1, assignedTenantCount: 0 },
    { id: chosenId, code: 'chosen', name: '明确选择的套餐', description: null, isActive: true, version: 1, assignedTenantCount: 0 }], page: 1, pageSize: 20, total: 2 }));
  await page.route('**/api/v1/tenancy/tenants?*', route => json(route, { items: [{ id: tenantId, identifier: 'acme', name: 'Acme 企业', domain: 'acme.localhost',
    isActive: active, version: 1, defaultLocale: 'zh-CN' }], page: 1, pageSize: 20, total: 1 }));
  const state = { rows, reads: 0, creates: [], cancels: [] };
  await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions?*`, route => {
    state.reads++; return json(route, { items: state.rows, page: 1, pageSize: 20, total: state.rows.length });
  });
  await page.goto('/#/tenants');
  await page.getByRole('link', { name: '租户管理', exact: true }).click();
  await page.getByTestId('tenants-action-subscriptions').click();
  await expect(page.getByTestId('subscription-target')).toHaveText('Acme 企业');
  await expect(page.locator('.el-loading-mask')).toHaveCount(0);
  return state;
}
for (const width of [1440, 390]) {
  test(`到期原套餐按当前报价创建独立新周期 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    const previous = { id: subscriptionId, tenantId, packageId, status: 'Expired', currentPeriodStartUtc: '2026-10-01T00:00:00Z',
      currentPeriodEndUtc: '2026-11-01T00:00:00Z', trialEndsAtUtc: null, cancelledAtUtc: null, version: 3 };
    const state = await setup(page, { rows: [previous] }); await page.setViewportSize({ width, height: 1000 });
    let offers = 0; const newId = id(8);
    const offer = { packageId, paymentTenantId: tenantId, merchantConfigId: id(7), amountMinor: 24900, currency: 'CNY', seatLimit: 30,
      storageLimit: 200000, durationDays: 30, entitlementIds: [], isActive: true, version: 7 };
    await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/offer`, route => { offers++; return json(route, offer); });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/paid`, route => {
      const body = route.request().postDataJSON(); state.creates.push(body);
      expect(Object.keys(body).sort()).toEqual(['effectiveFromUtc', 'idempotencyKey', 'offerVersion', 'packageId']);
      expect(body.packageId).toBe(packageId); expect(body.offerVersion).toBe(7);
      expect(Date.parse(body.effectiveFromUtc)).toBeGreaterThan(Date.parse(previous.currentPeriodEndUtc));
      const end = new Date(Date.parse(body.effectiveFromUtc) + 30 * 86400000).toISOString();
      state.rows = [{ id: newId, tenantId, packageId, status: 'PastDue', currentPeriodStartUtc: body.effectiveFromUtc,
        currentPeriodEndUtc: end, trialEndsAtUtc: null, cancelledAtUtc: null, version: 1 }, previous];
      return json(route, { ...offer, tenantId, subscriptionId: newId, offerVersion: 7, paymentOperationId: id(6), subject: 'Current plan',
        effectiveFromUtc: body.effectiveFromUtc, effectiveToUtc: end });
    });
    await page.getByTestId('subscription-repurchase').click();
    const modal = page.getByRole('dialog').filter({ has: page.getByTestId('repurchase-source') });
    await expect(modal.getByTestId('repurchase-source')).toHaveText(subscriptionId);
    await expect(modal.getByRole('combobox')).toBeDisabled(); expect(offers).toBe(0); expect(state.creates.length).toBe(0);
    await modal.getByRole('button', { name: '读取报价', exact: true }).click(); await expect(modal).toContainText('24900 CNY');
    await modal.screenshot({ path: info.outputPath('expired-repurchase-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('paid-subscription-confirm').click(); await expect(modal).toContainText(newId);
    expect(state.creates.length).toBe(1); expect(offers).toBe(1); expect(state.rows.find(item => item.id === subscriptionId)).toEqual(previous);
    await modal.getByRole('button', { name: '关闭', exact: true }).click();
    await expect(modal).toBeHidden();
    await expect(page.getByTestId('subscription-repurchase')).toHaveCount(0);
    const history = page.getByRole('dialog', { name: '订阅管理', exact: true });
    await expect(history).toContainText('已到期'); await expect(history).toContainText('待付款');
  });
  test(`选择套餐确认试用及取消未知后重读 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const state = await setup(page); await page.setViewportSize({ width, height: 1000 });
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions`, route => {
      const body = route.request().postDataJSON(); state.creates.push(body);
      expect(body.packageId).toBe(chosenId); expect(body.status).toBe('Trial');
      state.rows = [{ id: subscriptionId, tenantId, ...body, cancelledAtUtc: null, version: 1 }];
      return json(route, state.rows[0]);
    });
    await expect(page.getByTestId('tenant-subscription-trial')).toBeDisabled();
    const select = page.getByTestId('trial-subscription-package').getByRole('combobox');
    await select.focus(); await select.press('ArrowDown'); await page.getByRole('option', { name: '明确选择的套餐', exact: true }).click();
    await page.getByTestId('tenant-subscription-trial').click();
    const confirmation = page.getByRole('dialog').filter({ has: page.getByTestId('tenant-subscription-confirm') });
    await expect(confirmation).toContainText('明确选择的套餐'); expect(state.creates.length).toBe(0);
    await confirmation.screenshot({ path: info.outputPath('trial-confirm-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('tenant-subscription-confirm').click(); await expect(page.getByTestId('tenant-subscription-cancel')).toBeEnabled();
    expect(state.creates.length).toBe(1); await expect(page.getByTestId('tenant-subscription-trial')).toBeDisabled();
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/subscriptions/${subscriptionId}/cancel`, route => {
      state.cancels.push(route.request().postDataJSON()); state.rows = [{ ...state.rows[0], status: 'Cancelled', version: 2 }];
      return json(route, { status: 503, code: 'test.response_lost', title: 'unconfirmed' }, 503);
    });
    await page.getByTestId('tenant-subscription-cancel').click(); expect(state.cancels.length).toBe(0);
    await expect(confirmation).toContainText('不会自动发起渠道退款'); await page.getByTestId('tenant-subscription-confirm').click();
    await expect(page.getByTestId('subscription-operation-receipt')).toContainText('操作结果未确认');
    expect(state.cancels).toEqual([{ version: 1 }]); await expect(page.getByTestId('tenant-subscription-cancel')).toBeDisabled();
    const reads = state.reads; await page.getByTestId('tenant-subscriptions-refresh').click();
    await expect(page.getByTestId('tenant-subscription-cancel')).toHaveCount(0); expect(state.reads).toBe(reads + 1); expect(state.cancels.length).toBe(1);
  });
  test(`只读用户可查看停用企业订阅历史 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await setup(page, { readOnly: true, active: false, rows: [{ id: subscriptionId, tenantId, packageId, status: 'Expired',
      currentPeriodStartUtc: '2026-10-01T00:00:00Z', currentPeriodEndUtc: '2026-11-01T00:00:00Z', trialEndsAtUtc: null, cancelledAtUtc: null, version: 3 }] });
    await page.setViewportSize({ width, height: 1000 });
    await expect(page.getByRole('dialog')).toContainText('已到期');
    await expect(page.getByTestId('tenant-subscription-cancel')).toHaveCount(0); await expect(page.getByTestId('tenant-subscription-trial')).toHaveCount(0);
    await expect(page.getByTestId('paid-subscription-create')).toHaveCount(0);
    await page.getByRole('dialog').screenshot({ path: info.outputPath('readonly-history-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
  });
}
