import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const record = (n, status) => ({ id: id(n), definitionId: id(2), definitionKey: 'status_fixture', definitionName: `${status} history ${n}`, versionNumber: 3, formatKey: 'excel',
  statusKey: status, rowCount: status === 'succeeded' ? 1 : 0, outputFileName: status === 'succeeded' ? 'result.xlsx' : null, outputFileId: status === 'succeeded' ? id(3) : null,
  errorCode: null, errorMessage: null, requestedByUserId: id(9), createdAtUtc: '2026-11-05T00:00:00Z', completedAtUtc: '2026-11-05T00:01:00Z', version: 1, parameters: [] });
const summary = value => { const { version, outputFileId, parameters, ...row } = value; return row; };
async function boot(page, locale) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); const reads = []; let writes = 0, mode = 'ok';
  const rows = [...Array.from({ length: 60 }, (_, index) => record(1000 + index, 'succeeded')), ...Array.from({ length: 60 }, (_, index) => record(2000 + index, 'failed'))];
  await page.route('**/api/v1/reporting/**', route => { if (route.request().method() !== 'GET') writes++; return route.fulfill({ status: 404 }); });
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'reader', displayName: 'Reader', tenantId: id(8), actorScope: `tenant:${id(8).replaceAll('-', '')}`,
    scope: `tenant:${id(8).replaceAll('-', '')}`, isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1,
    permissions: ['identity.navigation.read', 'reporting.export_tasks.read'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'exports', parentId: null, routeName: 'reporting-export-tasks', path: '/reporting/export-tasks',
    componentKey: 'reporting-export-tasks', title: 'Exports', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.export_tasks.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/reporting/export-tasks?*', async route => {
    const query = Object.fromEntries(new URL(route.request().url()).searchParams); reads.push(query); expect(route.request().method()).toBe('GET');
    await new Promise(resolve => setTimeout(resolve, 80));
    if (mode === '503' && query.page === '2') return route.fulfill({ status: 503 });
    const filtered = query.statusKey ? rows.filter(row => row.statusKey === query.statusKey) : rows;
    const items = mode === 'mismatch' ? [{ ...summary(rows[0]), definitionName: 'MISMATCH-ROW' }] : filtered.slice((Number(query.page) - 1) * Number(query.pageSize), Number(query.page) * Number(query.pageSize)).map(summary);
    return json(route, { items, page: Number(query.page), pageSize: Number(query.pageSize), total: filtered.length });
  });
  await page.route('**/api/v1/reporting/export-tasks/*', route => json(route, { ...record(9999, 'succeeded'), id: new URL(route.request().url()).pathname.split('/').at(-1) }));
  await page.goto('/#/reporting/export-tasks'); await expect(page.getByTestId('reporting-export-filter-status')).toBeVisible();
  return { reads, writes: () => writes, mode: value => { mode = value; } };
}
async function choose(page, locale, state) {
  await page.getByTestId('reporting-export-filter-status').click(); const labels = { failed: locale === 'zh-CN' ? '已失败' : 'Failed', canceled: locale === 'zh-CN' ? '已取消' : 'Cancelled' };
  await page.getByRole('option', { name: labels[state], exact: true }).click();
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('导出状态来源筛选和已提交条件稳定分页 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.reporting-export-tasks-view');
    await choose(page, locale, 'failed'); await page.getByTestId('reporting-export-filter-submit').click();
    await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history'); expect(trace.reads.at(-1)).toEqual({ page: '1', pageSize: '20', statusKey: 'failed' });
    await choose(page, locale, 'canceled'); await view.locator('.el-pagination .btn-next').click();
    await expect(view.locator('.el-pager .is-active')).toHaveText('2'); await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history 2020');
    expect(trace.reads.at(-1)).toEqual({ page: '2', pageSize: '20', statusKey: 'failed' });
    await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history 2020'); expect(trace.reads.at(-1)).toEqual({ page: '2', pageSize: '20', statusKey: 'failed' });
    await expect(page.getByTestId('reporting-export-applied-status')).toContainText(locale === 'zh-CN' ? '已失败' : 'Failed');
    await page.screenshot({ path: info.outputPath('export-status-page-two-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.reporting-export-tasks-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    trace.mode('mismatch'); await page.getByTestId('reporting-export-filter-submit').click(); await expect(view.locator('.el-alert')).toBeVisible();
    await expect(view.locator('.el-table__body-wrapper')).not.toContainText('MISMATCH-ROW'); await expect(view.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(0);
    expect(trace.reads.at(-1)).toEqual({ page: '1', pageSize: '20', statusKey: 'canceled' }); trace.mode('ok'); await page.getByTestId('reporting-export-filter-reset').click();
    await expect(view.locator('.el-table__body-wrapper')).toContainText('succeeded history'); expect(trace.reads.at(-1)).toEqual({ page: '1', pageSize: '20' });
    await choose(page, locale, 'failed'); await page.getByTestId('reporting-export-filter-submit').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history');
    await page.getByTestId('reporting-export-exact-id').fill(id(9999)); await page.getByTestId('reporting-export-read-id').click(); await expect(page.getByTestId('reporting-export-detail-id')).toHaveText(id(9999));
    await expect(page.getByRole('dialog')).toContainText(locale === 'zh-CN' ? '已成功' : 'Succeeded'); expect(trace.writes()).toBe(0);
  });
  test('状态页读取失败后只显式重试原页而不自动回退 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.reporting-export-tasks-view');
    await choose(page, locale, 'failed'); await page.getByTestId('reporting-export-filter-submit').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history');
    trace.mode('503'); await view.locator('.el-pagination .btn-next').click(); await expect(view.locator('.el-alert')).toBeVisible(); await expect(view.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(0);
    await expect(view.locator('.el-loading-mask')).toHaveCount(0); await expect(view.locator('.el-alert')).toContainText(locale === 'zh-CN' ? '待读取页码： 2' : 'Requested page: 2');
    await page.screenshot({ path: info.outputPath('export-status-read-failed-' + width + '.png'), animations: 'disabled' });
    expect(trace.reads).toHaveLength(3); expect(trace.reads.at(-1)).toEqual({ page: '2', pageSize: '20', statusKey: 'failed' });
    trace.mode('ok'); await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click();
    await expect(view.locator('.el-table__body-wrapper')).toContainText('failed history 2020'); expect(trace.reads).toHaveLength(4);
    expect(trace.reads.at(-1)).toEqual({ page: '2', pageSize: '20', statusKey: 'failed' }); expect(trace.writes()).toBe(0);
  });
}
