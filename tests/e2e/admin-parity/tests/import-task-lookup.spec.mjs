import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const record = n => ({ id: id(n), tenantId: id(8), schemaKey: 'organization.tenant_positions', schemaDisplayName: `Import ${n}`, worksheetKey: 'positions', sourceFileId: id(5), sourceFileName: 'positions.xlsx', statusKey: 'preview_succeeded',
  totalRows: 1, validRowCount: 1, invalidRowCount: 0, errorCode: null, requestedByUserId: id(9), createdAtUtc: '2026-11-06T00:00:00Z', previewCompletedAtUtc: null, processedRowCount: 0, succeededRowCount: 0,
  executionFailedRowCount: 0, nextLineNumber: 0, executionStartedAtUtc: null, executionCompletedAtUtc: null, hasErrorReceipt: false, version: 4, previewRows: [],
  sourceRetention: { expiresAtUtc: '2099-01-01T00:00:00Z', stateKey: 'available', releasedAtUtc: null } });
async function boot(page, locale) {
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
    const rows = Array.from({ length: 40 }, (_, n) => record(11 + n)); return json(route, { items: rows.slice((Number(query.page) - 1) * Number(query.pageSize), Number(query.page) * Number(query.pageSize)), page: Number(query.page), pageSize: Number(query.pageSize), total: 40 });
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
