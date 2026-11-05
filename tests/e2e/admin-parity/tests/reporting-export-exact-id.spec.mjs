import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, locale, otherOwner = false) {
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); const reads = []; let lists = 0, writes = 0, downloads = 0, missing = false;
  await page.route('**/api/v1/reporting/**', route => { if (route.request().method() !== 'GET') writes++; return route.fulfill({ status: 404 }); });
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'reader', displayName: 'Reader', tenantId: id(8), actorScope: `tenant:${id(8).replaceAll('-', '')}`,
    scope: `tenant:${id(8).replaceAll('-', '')}`, isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1,
    permissions: ['identity.navigation.read', 'reporting.export_tasks.read', ...(otherOwner ? ['reporting.export_tasks.create', 'reporting.export_tasks.cancel', 'reporting.export_tasks.download', 'reporting.published_definitions.read'] : [])] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'exports', parentId: null, routeName: 'reporting-export-tasks', path: '/reporting/export-tasks',
    componentKey: 'reporting-export-tasks', title: 'Exports', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.export_tasks.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/reporting/export-tasks?*', route => { lists++; return json(route, { items: [], page: 1, pageSize: 20, total: 0 }); });
  await page.route('**/api/v1/reporting/export-tasks/*', route => {
    expect(route.request().method()).toBe('GET'); const requested = new URL(route.request().url()).pathname.split('/').at(-1); reads.push(requested);
    if (missing) return route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ type: 'about:blank', status: 404, code: 'reporting.export_task.not_found', title: 'Task not found' }) });
    return json(route, { id: requested, definitionId: id(2), definitionKey: 'original', definitionName: 'Original report', versionNumber: 3, formatKey: 'excel', statusKey: otherOwner ? 'failed' : 'succeeded', rowCount: 1,
      outputFileName: 'original.xlsx', outputFileId: id(3), errorCode: null, errorMessage: null, requestedByUserId: otherOwner ? id(6) : id(9), createdAtUtc: '2026-11-05T00:00:00Z', completedAtUtc: '2026-11-05T00:01:00Z', version: 2, parameters: [{ parameterKey: 'year', value: '2026' }] });
  });
  await page.route('**/api/v1/reporting/export-tasks/*/download', route => { downloads++; return route.fulfill({ status: 403 }); });
  await page.goto('/#/reporting/export-tasks'); await expect(page.getByTestId('reporting-export-exact-id')).toBeVisible();
  return { reads, lists: () => lists, writes: () => writes, downloads: () => downloads, miss: () => { missing = true; } };
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('空列表按原任务 UUID 独立读取及未找到身份保留 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('reporting-export-exact-id').fill('bad'); await page.getByTestId('reporting-export-read-id').click(); expect(trace.reads).toHaveLength(0); expect(trace.lists()).toBe(1);
    await page.getByTestId('reporting-export-exact-id').fill(` ${id(4).toUpperCase()} `); await page.getByTestId('reporting-export-exact-id').press('Enter');
    await expect(page.getByTestId('reporting-export-detail-id')).toHaveText(id(4)); await expect(page.getByTestId('reporting-export-requester')).toHaveText(id(9));
    expect(trace.reads).toEqual([id(4)]); await expect(page.getByTestId('reporting-export-detail-download')).toHaveCount(0);
    await page.getByRole('dialog').screenshot({ path: info.outputPath('export-id-lookup-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.el-drawer').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByRole('dialog').getByRole('button', { name: /Close|关闭/i }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
    trace.miss(); await page.getByTestId('reporting-export-exact-id').fill(id(5)); await page.getByTestId('reporting-export-read-id').click();
    await expect(page.getByTestId('reporting-export-detail-problem')).toContainText('Task not found'); await expect(page.getByTestId('reporting-export-detail-id')).toHaveText(id(5));
    await expect(page.getByTestId('reporting-export-requester')).toHaveCount(0); expect(trace.lists()).toBe(1); expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
    await page.getByRole('dialog').screenshot({ path: info.outputPath('export-id-not-found-' + width + '.png'), animations: 'disabled' });
  });
  test('显示同企业可见任务发起人且不借读取操作他人任务 ' + width, async ({ page }) => {
    const trace = await boot(page, locale, true); await page.setViewportSize({ width, height: 1000 }); await page.getByTestId('reporting-export-exact-id').fill(id(4)); await page.getByTestId('reporting-export-read-id').click();
    await expect(page.getByTestId('reporting-export-requester')).toHaveText(id(6)); await expect(page.getByTestId('reporting-export-regenerate')).toHaveCount(0);
    await expect(page.getByTestId('reporting-export-detail-cancel')).toHaveCount(0); await expect(page.getByTestId('reporting-export-detail-download')).toHaveCount(0);
    expect(trace.writes()).toBe(0); expect(trace.downloads()).toBe(0);
  });
}
