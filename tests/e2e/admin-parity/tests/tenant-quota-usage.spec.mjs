import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const tenantId = id(1);
const json = (route, body, status = 200) => route.fulfill({ status, contentType: status >= 400 ? 'application/problem+json' : 'application/json', body: JSON.stringify(body) });
async function setup(page, locale, hasQuotaRead = true, hasDiagnostic = false) {
  await page.addInitScript(selected => localStorage.setItem('fullnet.admin.locale', selected), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'quota-reader', displayName: 'Quota reader', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['identity.navigation.read', 'tenancy.tenants.read', 'tenancy.tenant_packages.read', ...(hasQuotaRead ? ['tenancy.tenant_quota.read'] : []), ...(hasDiagnostic ? ['tenancy.tenant_quota.reconcile_usage_baseline'] : [])],
    sessionId: id(10), preferredLocale: locale, profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'tenants', parentId: null, routeName: 'tenant-management', path: '/tenants', componentKey: 'tenants', title: 'Tenants',
    caption: '', icon: 'setting', order: 1, requiredPermission: 'tenancy.tenants.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/tenancy/settings/entitlement-enforcement', route => json(route, { phase: 'Enforced', version: 1 }));
  await page.route('**/api/v1/tenancy/tenant-packages?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route('**/api/v1/tenancy/tenants?*', route => json(route, { items: [{ id: tenantId, identifier: 'acme', name: 'Quota tenant', domain: 'acme.localhost',
    isActive: false, version: 1, defaultLocale: locale }], page: 1, pageSize: 20, total: 1 }));
  const state = { reads: 0, writes: 0, mode: 'ok' };
  await page.route('**/api/v1/tenancy/tenants/*/quota/**', route => { if (route.request().method() !== 'GET') state.writes++; return json(route, { status: 405, code: 'test.unexpected_write' }, 405); });
  await page.route(`**/api/v1/tenancy/tenants/${tenantId}/quota/usage`, route => {
    state.reads++; expect(route.request().method()).toBe('GET'); expect(route.request().postData()).toBeNull();
    if (state.mode === 'failure') return json(route, { status: 503, code: 'test.unavailable', title: 'unavailable' }, 503);
    if (state.mode === 'denied') return json(route, { status: 403, code: 'authorization.permission_denied', title: 'denied' }, 403);
    return json(route, { tenantId, tenantIsActive: false, readAtUtc: '2026-11-05T00:00:00Z', metrics: state.mode === 'empty' ? [] : [
      { id: id(2), metricCode: 'identity.seats', periodKey: 'default', limitValue: '100', usedValue: '2', reservedValue: '1' },
      { id: id(3), metricCode: 'files.storage_bytes', periodKey: 'default', limitValue: '9223372036854775807', usedValue: '9223372036854775707', reservedValue: '25' }] });
  });
  await page.goto('/#/tenants'); await page.getByRole('link', { name: locale === 'en-US' ? 'Tenant management' : '租户管理', exact: true }).click();
  await expect(page.getByText('Quota tenant', { exact: true })).toBeVisible();
  return state;
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test(`只读配额精确展示及读取失败接续 ${width}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const state = await setup(page, locale); await page.setViewportSize({ width, height: 1000 });
    expect(state.reads).toBe(0); await page.getByTestId('tenants-action-quota').click();
    const modal = page.getByRole('dialog', { name: locale === 'en-US' ? 'Quota usage' : '配额用量', exact: true });
    const storage = page.getByTestId('quota-metric-files.storage_bytes');
    await expect(storage.getByTestId('quota-limit')).toHaveText('9,223,372,036,854,775,807'); await expect(storage.getByTestId('quota-remaining')).toHaveText('75');
    await expect(page.getByTestId('quota-metric-identity.seats').getByTestId('quota-remaining')).toHaveText('97');
    await expect(modal).toContainText(locale === 'en-US' ? 'Tenant is inactive' : '企业为非活动状态');
    await expect(page.getByTestId('quota-read-at')).toHaveAttribute('datetime', '2026-11-05T00:00:00Z');
    await modal.screenshot({ path: info.outputPath('quota-precise-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    state.mode = 'failure'; await page.getByTestId('quota-usage-refresh').click();
    await expect(page.getByTestId('quota-usage-problem')).toContainText(locale === 'en-US' ? 'last successful read' : '上次成功读取');
    await expect(storage.getByTestId('quota-remaining')).toHaveText('75');
    state.mode = 'denied'; await page.getByTestId('quota-usage-refresh').click(); await expect(page.getByTestId('quota-limit')).toHaveCount(0);
    expect(state.reads).toBe(3); expect(state.writes).toBe(0);
  });
  test(`未配置指标不补零 ${width}`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const state = await setup(page, locale); await page.setViewportSize({ width, height: 1000 }); state.mode = 'empty';
    await page.getByTestId('tenants-action-quota').click(); await expect(page.getByTestId('quota-metric-files.storage_bytes')).toContainText(locale === 'en-US' ? 'not configured' : '未配置');
    await expect(page.getByTestId('quota-limit')).toHaveCount(0); expect(state.reads).toBe(1); expect(state.writes).toBe(0);
  });
}
test('缺少配额读取权限不展示入口或发起查询', async ({ page }) => {
  const state = await setup(page, 'zh-CN', false); await expect(page.getByTestId('tenants-action-quota')).toHaveCount(0); expect(state.reads).toBe(0);
});

for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('单企业权威诊断只读切换与未知状态 ' + width, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 }); const state = await setup(page, locale, true, true);
    const reads = []; let mode = 'normal';
    await page.route(`**/api/v1/tenancy/tenants/${tenantId}/quota/usage-diagnostic?*`, route => {
      expect(route.request().method()).toBe('GET'); expect(route.request().postData()).toBeNull();
      const metricCode = new URL(route.request().url()).searchParams.get('metricCode'); reads.push(metricCode);
      if (mode === 'denied') return json(route, { status: 403, code: 'authorization.permission_denied', title: 'denied' }, 403);
      const storage = metricCode === 'files.storage_bytes';
      return json(route, { tenantId, metricCode, tenantIsActive: false, metricId: storage ? id(3) : id(2),
        bookUsedValue: storage ? '9223372036854775707' : '2', reservedValue: storage ? '0' : '1',
        authoritativeUsedValue: mode === 'unavailable' ? null : storage ? '9223372036854775807' : '3',
        observedDifference: mode === 'unavailable' || !storage ? null : '100', state: mode === 'unavailable' ? 'Unavailable' : storage ? 'Different' : 'InProgress', readAtUtc: '2026-11-05T00:00:01Z' });
    });
    await page.setViewportSize({ width, height: 1000 }); await page.getByTestId('tenants-action-quota').click();
    await expect(page.getByTestId('quota-diagnostic-read')).toBeVisible(); expect(reads).toHaveLength(0);
    await page.getByTestId('quota-diagnostic-read').click(); await expect(page.getByTestId('diagnostic-difference')).toHaveText(locale === 'en-US' ? 'Not compared' : '暂不比较');
    const storageLabel = locale === 'en-US' ? 'File storage' : '文件存储';
    await page.getByTestId('quota-diagnostic-metric').getByText(storageLabel, { exact: true }).click();
    await expect(page.getByTestId('quota-diagnostic-metric').getByRole('radio', { name: storageLabel, exact: true })).toBeChecked();
    await expect(page.getByTestId('quota-diagnostic-result')).toHaveCount(0); expect(reads).toHaveLength(1);
    await page.getByTestId('quota-diagnostic-read').click(); await expect(page.getByTestId('diagnostic-authoritative')).toHaveText('9223372036854775807');
    await expect(page.getByTestId('diagnostic-difference')).toHaveText('100');
    expect((await new AxeBuilder({ page }).include('.art-form-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('quota-diagnostic-result').scrollIntoViewIfNeeded();
    await page.getByRole('dialog').last().screenshot({ path: info.outputPath('quota-diagnostic-' + width + '.png'), animations: 'disabled' });
    mode = 'unavailable'; await page.getByTestId('quota-diagnostic-read').click(); await expect(page.getByTestId('diagnostic-authoritative')).toHaveText(locale === 'en-US' ? 'Unknown' : '未知');
    await expect(page.getByTestId('diagnostic-difference')).toHaveText(locale === 'en-US' ? 'Not compared' : '暂不比较');
    mode = 'denied'; await page.getByTestId('quota-diagnostic-read').click(); await expect(page.getByTestId('quota-diagnostic-result')).toHaveCount(0);
    await expect(page.getByTestId('quota-diagnostic-failure')).toBeVisible(); expect(reads).toEqual(['identity.seats', 'files.storage_bytes', 'files.storage_bytes', 'files.storage_bytes']);
    expect(state.reads).toBe(1); expect(state.writes).toBe(0);
  });
}
