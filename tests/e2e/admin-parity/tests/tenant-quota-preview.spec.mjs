import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const tenantId = id(1);
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
async function setup(page, locale, hasPreview = true) {
  await page.addInitScript(selected => localStorage.setItem('fullnet.admin.locale', selected), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'quota-reader', displayName: 'Quota reader', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_packages.read', ...(hasPreview ? ['tenancy.tenant_quota.reconcile_usage_baseline'] : [])],
    sessionId: id(10), preferredLocale: locale, profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'tenants', parentId: null, routeName: 'tenant-management', path: '/tenants', componentKey: 'tenants', title: 'Tenants',
    caption: '', icon: 'setting', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', route => json(route, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/tenant-packages?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route('**/api/v1/tenancy/tenants?*', route => json(route, { items: [{ id: tenantId, identifier: 'acme', name: 'Quota tenant', domain: 'acme.localhost',
    isActive: false, version: 1, defaultLocale: locale }], page: 1, pageSize: 20, total: 1 }));
  const state = { requests: [], mode: 'ok' };
  await page.route('**/api/v1/tenancy/quota/usage-baseline', route => {
    const body = route.request().postDataJSON(); state.requests.push(body);
    expect(route.request().method()).toBe('POST'); expect(body.dryRun).toBe(true); expect(body.pageSize).toBe(25);
    if (state.mode === 'failure') return json(route, { status: 503, code: 'test.unavailable', title: 'unavailable' }, 503);
    if (state.mode === 'denied') return json(route, { status: 403, code: 'authorization.permission_denied', title: 'denied' }, 403);
    return json(route, { dryRun: state.mode !== 'invalid', candidateCount: 1, appliedCount: 0, tenantIds: [id(11)],
      nextAfterTenantId: body.afterTenantId ? null : id(12), isComplete: Boolean(body.afterTenantId) });
  });
  await page.goto('/#/tenants'); await page.getByRole('link', { name: locale === 'en-US' ? 'Tenant management' : '租户管理', exact: true }).click();
  await expect(page.getByText('Quota tenant', { exact: true })).toBeVisible();
  return state;
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`全局配额只预检及显式分页 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const state = await setup(page, locale); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('quota-preview-open').click(); expect(state.requests).toEqual([]);
    const modal = page.getByRole('dialog', { name: locale === 'en-US' ? 'Global quota preview' : '全局配额预检', exact: true });
    await page.getByTestId('quota-preview-load').click(); await expect(page.getByTestId('quota-preview-result')).toContainText(id(11));
    expect(state.requests).toEqual([{ dryRun: true, metricCode: 'identity.seats', afterTenantId: null, pageSize: 25 }]);
    await modal.screenshot({ path: info.outputPath('quota-preview-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    state.mode = 'failure'; await page.getByTestId('quota-preview-load').click();
    await expect(page.getByTestId('quota-preview-problem')).toContainText(locale === 'en-US' ? 'last successful batch' : '上次成功');
    await expect(page.getByTestId('quota-preview-result')).toContainText(id(11));
    state.mode = 'ok'; await page.getByTestId('quota-preview-load').click(); await expect(page.getByTestId('quota-preview-load')).toBeDisabled();
    expect(state.requests.slice(1).map(x => x.afterTenantId)).toEqual([id(12), id(12)]);
    await page.getByTestId('quota-preview-reset').click(); await expect(page.getByTestId('quota-preview-result')).toHaveCount(0);
    await page.getByTestId('quota-preview-metric').getByText(locale === 'en-US' ? 'File storage' : '文件存储', { exact: true }).click();
    await expect(page.getByRole('radio', { name: locale === 'en-US' ? 'File storage' : '文件存储', exact: true })).toBeChecked();
    expect(state.requests).toHaveLength(3); await page.getByTestId('quota-preview-load').click(); await expect(page.getByTestId('quota-preview-result')).toBeVisible();
    expect(state.requests[3]).toEqual({ dryRun: true, metricCode: 'files.storage_bytes', afterTenantId: null, pageSize: 25 });
    state.mode = 'denied'; await page.getByTestId('quota-preview-load').click(); await expect(page.getByTestId('quota-preview-result')).toHaveCount(0);
  });
}
test('无效预检批次不显示结果且可显式重试', async ({ page }) => {
  const state = await setup(page, 'zh-CN'); state.mode = 'invalid'; await page.getByTestId('quota-preview-open').click(); await page.getByTestId('quota-preview-load').click();
  await expect(page.getByTestId('quota-preview-problem')).toContainText('未推进游标'); await expect(page.getByTestId('quota-preview-result')).toHaveCount(0);
  state.mode = 'ok'; await page.getByTestId('quota-preview-load').click(); await expect(page.getByTestId('quota-preview-result')).toBeVisible();
  expect(state.requests.map(x => x.afterTenantId)).toEqual([null, null]);
});
test('缺少独立预检权限无全局入口', async ({ page }) => {
  const state = await setup(page, 'zh-CN', false); await expect(page.getByTestId('quota-preview-open')).toHaveCount(0); expect(state.requests).toEqual([]);
});
