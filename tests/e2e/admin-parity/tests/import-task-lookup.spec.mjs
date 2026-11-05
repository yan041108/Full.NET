import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const record = n => ({ id: id(n), tenantId: id(8), schemaKey: 'organization.tenant_positions', schemaDisplayName: `Import ${n}`, worksheetKey: 'positions', sourceFileId: id(5), sourceFileName: 'positions.xlsx', statusKey: 'preview_succeeded',
  totalRows: 1, validRowCount: 1, invalidRowCount: 0, errorCode: null, requestedByUserId: id(9), createdAtUtc: '2026-11-06T00:00:00Z', previewCompletedAtUtc: null, processedRowCount: 0, succeededRowCount: 0,
  executionFailedRowCount: 0, nextLineNumber: 0, executionStartedAtUtc: null, executionCompletedAtUtc: null, hasErrorReceipt: false, version: 4, previewRows: [],
  sourceRetention: { expiresAtUtc: '2099-01-01T00:00:00Z', stateKey: 'available', releasedAtUtc: null } });
async function boot(page, locale, statusMode = false) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); const reads = [], lists = []; let mode = 'ok', writes = 0, downloads = 0;
  await page.route('**/api/v1/import-export/**', route => { if (route.request().method() !== 'GET') writes++; if (route.request().url().includes('error-receipt')) downloads++; return route.fulfill({ status: 404 }); });
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'reader', displayName: 'Reader', tenantId: id(8), actorScope: `tenant:${id(8).replaceAll('-', '')}`, scope: `tenant:${id(8).replaceAll('-', '')}`,
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, permissions: ['identity.navigation.read', 'import_export.import_tasks.read'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'imports', parentId: null, routeName: 'import-export-tasks', path: '/import-export/tasks', componentKey: 'import-export-tasks', title: 'Imports', caption: '', icon: 'document', order: 1, requiredPermission: 'import_export.import_tasks.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, [])); await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/import-export/tasks?*', async route => {
    const query = Object.fromEntries(new URL(route.request().url()).searchParams); lists.push(query); await new Promise(resolve => setTimeout(resolve, 80));
    if (mode === 'list-failed' && query.page === '2') return route.fulfill({ status: 503 });
    const rows = statusMode ? [...Array.from({ length: 60 }, (_, n) => record(11 + n)), ...Array.from({ length: 60 }, (_, n) => ({ ...record(1011 + n), schemaKey: 'retired.fixture', statusKey: 'execution_failed' })), ...Array.from({ length: 60 }, (_, n) => ({ ...record(2011 + n), schemaKey: 'other.fixture', statusKey: 'execution_failed' }))] : Array.from({ length: 40 }, (_, n) => record(11 + n));
    const filtered = rows.filter(row => (!query.schemaKey || row.schemaKey === query.schemaKey) && (!query.statusKey || row.statusKey === query.statusKey));
    const items = mode === 'list-mismatch' ? [{ ...record(11), schemaDisplayName: 'WRONG-STATE' }] : filtered.slice((Number(query.page) - 1) * Number(query.pageSize), Number(query.page) * Number(query.pageSize));
    return json(route, { items, page: Number(query.page), pageSize: Number(query.pageSize), total: filtered.length });
  });
  await page.route('**/api/v1/import-export/tasks/*', route => {
    const target = new URL(route.request().url()).pathname.split('/').at(-1); reads.push(target);
    if (target === id(404)) return route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ status: 404, code: 'import_export.task.not_found', title: 'NOT-FOUND' }) });
    if (mode === 'detail-failed') return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, title: 'READ-FAILED' }) });
    return json(route, { ...record(target === id(11) ? 11 : 99), ...(mode === 'mismatch' ? { sourceFileId: id(999), schemaDisplayName: 'WRONG-SOURCE' } : {}), ...(mode === 'tenant' ? { tenantId: id(7) } : {}) });
  });
  await page.goto('/#/import-export/tasks'); await expect(page.getByTestId('import-task-exact-id')).toBeVisible(); await expect(page.locator('.el-table__body-wrapper')).toContainText('Import 11');
  return { reads, lists, mode: value => { mode = value; }, writes: () => writes, downloads: () => downloads };
}
async function lookup(page, value) { await page.getByTestId('import-task-exact-id').fill(value); await page.getByTestId('import-task-read-id').click(); }
async function close(page) { await page.getByRole('dialog').locator('.el-drawer__close-btn').click(); await expect(page.getByRole('dialog')).not.toBeVisible(); }
async function settle(page) {
  await expect(page.getByRole('dialog').locator('.el-loading-mask')).toHaveCount(0);
  await expect.poll(async () => { const box = await page.getByRole('dialog').boundingBox(); return !!box && Math.abs(box.x + box.width - page.viewportSize().width) < 2; }).toBe(true);
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('导入 Schema/状态提交、草稿独立及来源状态拒绝 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale, true); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.import-export-tasks-view');
    await page.getByTestId('import-task-schema-filter').fill(' retired.fixture '); await page.getByTestId('import-task-status-filter').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '执行失败' : 'Execution failed', exact: true }).click();
    await page.getByTestId('import-task-filter-submit').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 1011');
    await page.getByTestId('import-task-schema-filter').fill('other.fixture'); await page.getByTestId('import-task-status-filter').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '已取消' : 'Canceled', exact: true }).click();
    await view.locator('.el-pagination .btn-next').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 1031'); await expect(view.locator('.el-pager .is-active')).toHaveText('2');
    await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click(); await expect(view.locator('.el-loading-mask')).toHaveCount(0);
    expect(trace.lists.at(-1)).toEqual({ page: '2', pageSize: '20', schemaKey: 'retired.fixture', statusKey: 'execution_failed' }); await expect(page.getByTestId('import-task-applied-query')).toContainText('retired.fixture');
    await page.screenshot({ path: info.outputPath(`import-status-page-two-${width}.png`), animations: 'disabled' }); expect((await new AxeBuilder({ page }).include('.import-export-tasks-view').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    trace.mode('list-mismatch'); await page.getByTestId('import-task-filter-submit').click(); await expect(view.locator('.el-alert--error')).toBeVisible(); await expect(view.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(0); await expect(view).not.toContainText('WRONG-STATE');
    expect(trace.lists.at(-1)).toEqual({ page: '1', pageSize: '20', schemaKey: 'other.fixture', statusKey: 'canceled' });
    trace.mode('ok'); await page.getByTestId('import-task-schema-filter').fill(''); await page.getByTestId('import-task-status-filter').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '全部状态' : 'All statuses', exact: true }).click(); await page.getByTestId('import-task-filter-submit').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 11');
    expect(trace.lists.at(-1)).toEqual({ page: '1', pageSize: '20' }); expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
  });
  test('导入状态源第二页失败后保持条件并独立读取其他状态详情 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale, true); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.import-export-tasks-view');
    await page.getByTestId('import-task-schema-filter').fill('retired.fixture'); await page.getByTestId('import-task-status-filter').click(); await page.getByRole('option', { name: locale === 'zh-CN' ? '执行失败' : 'Execution failed', exact: true }).click(); await page.getByTestId('import-task-filter-submit').click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 1011');
    trace.mode('list-failed'); await view.locator('.el-pagination .btn-next').click(); await expect(page.getByTestId('import-task-requested-page')).toContainText('2'); await expect(view.locator('.el-loading-mask')).toHaveCount(0); await expect(view.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(0);
    await page.screenshot({ path: info.outputPath(`import-status-read-failed-${width}.png`), animations: 'disabled' }); expect(trace.lists).toHaveLength(3);
    trace.mode('ok'); await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 1031'); await expect(view.locator('.el-loading-mask')).toHaveCount(0);
    expect(trace.lists).toHaveLength(4); expect(trace.lists.at(-1)).toEqual({ page: '2', pageSize: '20', schemaKey: 'retired.fixture', statusKey: 'execution_failed' });
    await lookup(page, id(99)); await expect(page.getByTestId('import-task-detail-id')).toHaveText(id(99)); await expect(page.getByRole('dialog')).toContainText(locale === 'zh-CN' ? '预校验成功' : 'Preview succeeded'); expect(trace.lists).toHaveLength(4); expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
  });
  test('导入只读页外定位、原来源身份和失败重读 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 }); await lookup(page, ` ${id(99).toUpperCase()} `);
    await expect(page.getByTestId('import-task-detail-id')).toHaveText(id(99)); await expect(page.getByTestId('import-task-source-file')).toHaveText(id(5));
    await expect(page.getByTestId('import-task-tenant')).toHaveText(id(8)); await expect(page.getByTestId('import-task-requester')).toHaveText(id(9));
    await expect(page.getByTestId('import-task-schema-key')).toHaveText('organization.tenant_positions'); await expect(page.getByTestId('import-task-worksheet-key')).toHaveText('positions');
    await settle(page); await page.screenshot({ path: info.outputPath(`import-original-identity-${width}.png`), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.el-drawer').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    trace.mode('detail-failed'); await page.getByTestId('import-export-task-detail-refresh').click(); await expect(page.getByTestId('import-task-stale-snapshot')).toBeVisible(); await expect(page.getByTestId('import-task-source-file')).toHaveText(id(5));
    trace.mode('ok'); await close(page); await lookup(page, id(404)); await expect(page.getByTestId('import-task-detail-problem')).toContainText('NOT-FOUND');
    await expect(page.getByTestId('import-task-detail-id')).toHaveText(id(404)); await expect(page.getByTestId('import-task-source-file')).toHaveCount(0); await settle(page);
    await page.screenshot({ path: info.outputPath(`import-request-not-found-${width}.png`), animations: 'disabled' });
    await page.getByTestId('import-export-task-detail-refresh').click(); await expect.poll(() => trace.reads.length).toBe(4); expect(trace.reads.at(-1)).toBe(id(404));
    expect(trace.lists).toHaveLength(1); expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
  });
  test('导入非法目标、来源漂移和错误企业拒绝 ' + width, async ({ page }) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 }); await lookup(page, 'bad'); await expect(page.getByTestId('import-task-lookup-problem')).toBeVisible();
    await lookup(page, '00000000-0000-0000-0000-000000000000'); expect(trace.reads).toHaveLength(0); expect(trace.lists).toHaveLength(1);
    trace.mode('mismatch'); await lookup(page, id(11)); await expect(page.getByTestId('import-task-detail-problem')).toBeVisible(); await expect(page.getByRole('dialog')).not.toContainText('WRONG-SOURCE'); await expect(page.getByTestId('import-task-source-file')).toHaveCount(0);
    await close(page); trace.mode('tenant'); await lookup(page, id(99)); await expect(page.getByTestId('import-task-detail-problem')).toBeVisible(); await expect(page.getByTestId('import-task-tenant')).toHaveCount(0); expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
  });
  test('导入第二页失败后显式重读原页且不重读独立详情 ' + width, async ({ page }) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 }); const view = page.locator('.import-export-tasks-view');
    trace.mode('list-failed'); await view.locator('.el-pagination .btn-next').click(); await expect(page.getByTestId('import-task-requested-page')).toContainText('2'); await expect(view.locator('.el-loading-mask')).toHaveCount(0);
    await expect(view.locator('.el-table__body-wrapper .el-table__row')).toHaveCount(0); expect(trace.lists).toHaveLength(2);
    trace.mode('ok'); await view.getByRole('button', { name: locale === 'zh-CN' ? '刷新' : 'Refresh', exact: true }).click(); await expect(view.locator('.el-table__body-wrapper')).toContainText('Import 31');
    await expect(view.locator('.el-pager .is-active')).toHaveText('2'); expect(trace.lists).toHaveLength(3); expect(trace.lists.at(-1)).toEqual({ page: '2', pageSize: '20' });
    await lookup(page, id(99)); await expect(page.getByTestId('import-task-detail-id')).toHaveText(id(99)); expect(trace.lists).toHaveLength(3); expect(trace.reads).toEqual([id(99)]); expect(trace.writes()).toBe(0);
  });
}
