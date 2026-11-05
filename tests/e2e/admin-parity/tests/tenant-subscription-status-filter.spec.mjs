import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const tenantId = id(1), packageId = id(2), chosenId = id(3), subscriptionId = id(4);
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
async function setup(page, { readOnly = false, active = true, rows = [], locale = 'zh-CN' } = {}) {
  await page.addInitScript(selected => localStorage.setItem('fullnet.admin.locale', selected), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'operator', displayName: 'Operator', actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, tenantId: null, permissions: ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_packages.read', 'tenancy.tenant_subscriptions.read',
      ...(readOnly ? [] : ['tenancy.tenant_subscriptions.manage'])], sessionId: id(10), preferredLocale: locale, profileVersion: 1, passwordChangeRequired: false }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'tenants', parentId: null, routeName: 'tenant-management', path: '/tenants', componentKey: 'tenants',
    title: '租户管理', caption: '', icon: 'setting', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', route => json(route, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/tenant-packages?*', route => json(route, { items: [
    { id: packageId, code: 'first', name: '默认首个套餐', description: null, isActive: true, version: 1, assignedTenantCount: 0 },
    { id: chosenId, code: 'chosen', name: '明确选择的套餐', description: null, isActive: true, version: 1, assignedTenantCount: 0 }], page: 1, pageSize: 20, total: 2 }));
  await page.route('**/api/v1/tenancy/tenants?*', route => json(route, { items: [{ id: tenantId, identifier: 'acme', name: 'Acme 企业', domain: 'acme.localhost',
    isActive: active, version: 1, defaultLocale: 'zh-CN' }], page: 1, pageSize: 20, total: 1 }));
  const state = { reads: [], writes: 0, mode: 'ok' };
  const history = Array.from({ length: 24 }, (_, i) => ({ id: id(30 + i), tenantId, packageId, status: i < 22 ? 'Expired' : 'Cancelled',
    currentPeriodStartUtc: '2026-09-01T00:00:00Z', currentPeriodEndUtc: '2026-10-01T00:00:00Z', trialEndsAtUtc: null, cancelledAtUtc: null, version: 1 }));
  await page.route('**/api/v1/tenancy/tenants/*/subscriptions**', route => {
    if (route.request().method() !== 'GET') { state.writes++; return json(route, {}, 405); }
    const url = new URL(route.request().url()); const filter = url.searchParams.get('status'); const index = Number(url.searchParams.get('page') || 1);
    state.reads.push({ status: filter, page: index });
    if (state.mode === 'failure') return json(route, { status: 503, code: 'test.unavailable', title: 'unavailable' }, 503);
    const found = filter ? history.filter(item => item.status === filter) : history;
    return json(route, { items: found.slice((index - 1) * 20, index * 20), page: index, pageSize: 20, total: found.length });
  });
  await page.goto('/#/tenants');
  await page.getByRole('link', { name: locale === 'en-US' ? 'Tenant management' : '租户管理', exact: true }).click();
  await page.getByTestId('tenants-action-subscriptions').click();
  await expect(page.getByTestId('subscription-target')).toHaveText('Acme 企业');
  await expect(page.locator('.el-loading-mask')).toHaveCount(0);
  return state;
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`只读订阅状态筛选与分页 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    const state = await setup(page, { readOnly: true, active: false, locale }); await page.setViewportSize({ width, height: 1000 });
    const group = page.getByTestId('subscription-status-filter');
    const modal = page.locator('.art-form-dialog').filter({ has: group });
    await group.getByText(locale === 'en-US' ? 'Expired' : '已到期', { exact: true }).click();
    await expect(modal.locator('.el-pagination__total')).toHaveText(/\b22\b/);
    expect(state.reads.at(-1)).toEqual({ status: 'Expired', page: 1 });
    await modal.locator('.el-pagination .btn-next').click();
    await expect(page.locator('.subscription-history .el-table__body-wrapper tbody tr')).toHaveCount(2);
    expect(state.reads.at(-1)).toEqual({ status: 'Expired', page: 2 });
    await modal.screenshot({ path: info.outputPath('subscription-filter-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await group.getByText(locale === 'en-US' ? 'Cancelled' : '已取消', { exact: true }).click();
    await expect(modal.locator('.el-pagination__total')).toHaveText(/\b2\b/);
    expect(state.reads.at(-1)).toEqual({ status: 'Cancelled', page: 1 });
    await group.getByText(locale === 'en-US' ? 'All statuses' : '全部状态', { exact: true }).click();
    await expect(modal.locator('.el-pagination__total')).toHaveText(/\b24\b/);
    expect(state.reads.at(-1)).toEqual({ status: null, page: 1 });
    await expect(page.getByTestId('tenant-subscription-cancel')).toHaveCount(0); expect(state.writes).toBe(0);
  });
}

