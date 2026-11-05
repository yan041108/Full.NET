import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const record = n => ({ id: id(n), documentItemId: id(3), documentTitle: `Preview ${n}`, versionId: id(4), sourceFileId: id(5), outputFileId: id(6),
  statusKey: 'succeeded', providerKey: 'external_http', errorCode: null, requestedByUserId: id(9), createdAtUtc: '2026-11-06T00:00:00Z', startedAtUtc: null, completedAtUtc: '2026-11-06T00:01:00Z', version: 3 });
async function boot(page, locale) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); const reads = [], lists = []; let writes = 0, content = 0, mode = 'ok';
  await page.route('**/api/v1/document/**', route => { if (route.request().method() !== 'GET') writes++; if (route.request().url().endsWith('/content')) content++; return route.fulfill({ status: 404 }); });
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'reader', displayName: 'Reader', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1, permissions: ['identity.navigation.read', 'document.host_preview_tasks.read'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'previews', parentId: null, routeName: 'document-preview-tasks', path: '/document/preview-tasks', componentKey: 'document-preview-tasks',
    title: 'Previews', caption: '', icon: 'document', order: 1, requiredPermission: 'document.host_preview_tasks.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/document/public/preview-capabilities', route => json(route, { enabled: true, providerKey: 'external_http', allowedMimeTypes: [], maxInputBytes: 1024, maxAttempts: 3 }));
  await page.route('**/api/v1/document/host/preview-tasks?*', route => {
    const query = Object.fromEntries(new URL(route.request().url()).searchParams); lists.push(query);
    return json(route, { items: [record(11)], page: Number(query.page), pageSize: Number(query.pageSize), total: 1 });
  });
  await page.route('**/api/v1/document/host/preview-tasks/*', route => {
    const target = new URL(route.request().url()).pathname.split('/').at(-1); reads.push(target);
    if (mode === '503') return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, title: 'READ-FAILED' }) });
    if (target === id(404)) return route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ status: 404, code: 'document.host_preview_task.not_found', title: 'NOT-FOUND' }) });
    return json(route, { ...record(target === id(11) ? 11 : 99), ...(mode === 'mismatch' ? { sourceFileId: id(999), documentTitle: 'WRONG-SOURCE' } : {}) });
  });
  await page.goto('/#/document/preview-tasks'); await expect(page.getByTestId('document-preview-exact-id')).toBeVisible();
  await expect(page.locator('.el-table__body-wrapper')).toContainText('Preview 11');
  return { reads, lists, writes: () => writes, content: () => content, mode: value => { mode = value; } };
}
async function lookup(page, value) { await page.getByTestId('document-preview-exact-id').fill(value); await page.getByTestId('document-preview-read-id').click(); }
async function close(page) { await page.locator('.el-drawer__close-btn').click(); await expect(page.getByRole('dialog')).not.toBeVisible(); }
async function settleDrawer(page) {
  await expect(page.getByRole('dialog').locator('.el-loading-mask')).toHaveCount(0);
  await expect.poll(async () => {
    const box = await page.getByRole('dialog').boundingBox();
    return !!box && Math.abs(box.x + box.width - page.viewportSize().width) < 2;
  }).toBe(true);
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('预览只读精确任务、原文件身份及失败重读 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 });
    await lookup(page, ` ${id(99).toUpperCase()} `); await expect(page.getByTestId('document-preview-detail-id')).toHaveText(id(99));
    await expect(page.getByTestId('document-preview-source-file')).toHaveText(id(5)); await expect(page.getByTestId('document-preview-version-id')).toHaveText(id(4));
    await expect(page.getByTestId('document-preview-output-file')).toHaveText(id(6)); await expect(page.getByTestId('document-preview-requester')).toHaveText(id(9));
    await expect(page.getByRole('dialog')).toContainText('Preview 99');
    await settleDrawer(page); await page.screenshot({ path: info.outputPath(`preview-original-identity-${width}.png`), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.el-drawer').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    trace.mode('503'); await page.getByTestId('document-preview-detail-refresh').click(); await expect(page.getByTestId('document-preview-stale-snapshot')).toBeVisible();
    await expect(page.getByTestId('document-preview-source-file')).toHaveText(id(5)); expect(trace.reads).toEqual([id(99), id(99)]);
    trace.mode('ok'); await page.getByTestId('document-preview-detail-refresh').click(); await expect(page.getByTestId('document-preview-stale-snapshot')).toHaveCount(0);
    await close(page); await lookup(page, id(404)); await expect(page.getByTestId('document-preview-detail-problem')).toContainText('NOT-FOUND');
    await expect(page.getByTestId('document-preview-detail-id')).toHaveText(id(404)); await expect(page.getByTestId('document-preview-source-file')).toHaveCount(0);
    await settleDrawer(page); await page.screenshot({ path: info.outputPath(`preview-request-not-found-${width}.png`), animations: 'disabled' });
    await page.getByTestId('document-preview-detail-refresh').click(); await expect.poll(() => trace.reads.length).toBe(5); expect(trace.reads.at(-1)).toBe(id(404));
    expect(trace.lists).toHaveLength(1); expect(trace.writes()).toBe(0); expect(trace.content()).toBe(0); await expect(page.getByTestId('document-preview-task-create')).toHaveCount(0);
  });
  test('预览非法精确输入及原列表来源漂移拒绝 ' + width, async ({ page }) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 });
    await lookup(page, 'bad'); await expect(page.getByTestId('document-preview-lookup-problem')).toBeVisible();
    await lookup(page, '00000000-0000-0000-0000-000000000000'); expect(trace.reads).toHaveLength(0); expect(trace.lists).toHaveLength(1);
    trace.mode('mismatch'); await lookup(page, id(11)); await expect(page.getByTestId('document-preview-detail-problem')).toBeVisible();
    await expect(page.getByRole('dialog')).not.toContainText('WRONG-SOURCE'); await expect(page.getByTestId('document-preview-source-file')).toHaveCount(0);
    await expect(page.getByTestId('document-preview-detail-id')).toHaveText(id(11)); expect(trace.writes()).toBe(0); expect(trace.content()).toBe(0);
  });
}
