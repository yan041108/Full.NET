import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
const id = n => `019bc2b1-2a40-7cc3-8992-${String(n).padStart(12, '0')}`;
const json = (route, body) => route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
const original = { id: id(1), definitionId: id(2), definitionKey: 'original', definitionName: 'Original report', versionNumber: 3, formatKey: 'excel', statusKey: 'succeeded', rowCount: 1,
  outputFileName: 'original.xlsx', outputFileId: id(3), errorCode: null, errorMessage: null, requestedByUserId: id(9), createdAtUtc: '2026-11-05T00:00:00Z', completedAtUtc: '2026-11-05T00:01:00Z', version: 2,
  parameters: [{ parameterKey: 'year', value: '2026' }, { parameterKey: 'department', value: null }, { parameterKey: 'empty', value: '' }] };
const listRow = value => { const { version, outputFileId, parameters, ...row } = value; return row; };
async function boot(page, locale, lost, sourceStatus = 'succeeded', allowDownload = true) {
  const source = { ...original, statusKey: sourceStatus, outputFileId: sourceStatus === 'succeeded' ? original.outputFileId : null };
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.addInitScript(value => localStorage.setItem('fullnet.admin.locale', value), locale);
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 })); const writes = []; let catalogReads = 0, latest;
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id: id(9), username: 'operator', displayName: 'Operator', tenantId: id(8), actorScope: `tenant:${id(8).replaceAll('-', '')}`,
    scope: `tenant:${id(8).replaceAll('-', '')}`, isSuperAdministrator: false, passwordChangeRequired: false, sessionId: id(10), preferredLocale: locale, profileVersion: 1,
    permissions: ['identity.navigation.read', 'reporting.export_tasks.read', 'reporting.export_tasks.create', ...(allowDownload ? ['reporting.export_tasks.download'] : []), 'reporting.published_definitions.read'] }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'exports', parentId: null, routeName: 'reporting-export-tasks', path: '/reporting/export-tasks',
    componentKey: 'reporting-export-tasks', title: 'Exports', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.export_tasks.read', children: [] }]));
  await page.route('**/api/v1/tenancy/available', route => json(route, []));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => json(route, { unreadCount: 0 }));
  await page.route('**/api/v1/reporting/published-definitions?*', route => { catalogReads++; return json(route, { items: [{ id: original.definitionId, definitionKey: 'original', name: 'Latest report', versionNumber: 9, parameterSchema: [] }], page: 1, pageSize: 20, total: 1 }); });
  await page.route('**/api/v1/reporting/export-tasks?*', route => json(route, { items: latest ? [listRow(latest), listRow(source)] : [listRow(source)], page: 1, pageSize: 20, total: latest ? 2 : 1 }));
  await page.route('**/api/v1/reporting/export-tasks/*', route => {
    expect(route.request().method()).toBe('GET'); const requested = new URL(route.request().url()).pathname.split('/').at(-1);
    return json(route, latest && requested === latest.id ? latest : source);
  });
  await page.route(`**/api/v1/reporting/export-tasks/${original.id}/download`, route => route.fulfill({ status: 404, contentType: 'application/problem+json',
    body: JSON.stringify({ type: 'about:blank', status: 404, code: 'files.file.not_found', title: 'Original result unavailable' }) }));
  await page.route('**/api/v1/reporting/export-tasks', route => {
    expect(route.request().method()).toBe('POST'); const body = route.request().postDataJSON(); writes.push(body);
    if (lost) return route.fulfill({ status: 503 });
    latest = { ...original, id: id(4), statusKey: 'queued', rowCount: 0, outputFileName: null, outputFileId: null, completedAtUtc: null, version: 1, parameters: body.parameters };
    return json(route, latest);
  });
  await page.goto('/#/reporting/export-tasks'); await expect(page.getByTestId('reporting-export-detail')).toBeVisible();
  return { writes, catalogReads: () => catalogReads };
}
for (const [width, locale] of [[1440, 'en-US'], [390, 'zh-CN']]) {
  test('明确缺失结果按原版本与原参数创建独立导出 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale, false); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('reporting-export-detail').click(); await page.getByTestId('reporting-export-detail-download').click();
    await page.getByTestId('reporting-export-regenerate').click(); const snapshot = page.getByTestId('reporting-export-regeneration-snapshot');
    await expect(snapshot).toContainText(original.id); await expect(snapshot).toContainText(locale === 'zh-CN' ? '未提供（null）' : 'Not provided (null)');
    await expect(snapshot).toContainText(locale === 'zh-CN' ? '空字符串' : 'Empty string'); await expect(snapshot.locator('input')).toHaveCount(0);
    await expect(page.getByTestId('reporting-export-definition')).toHaveCount(0); expect(trace.writes).toHaveLength(0); expect(trace.catalogReads()).toBe(0);
    await expect(page.getByRole('dialog')).toHaveCount(1);
    await page.getByRole('dialog').screenshot({ path: info.outputPath('export-regeneration-confirm-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.el-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByRole('dialog').getByRole('button', { name: locale === 'zh-CN' ? '取消' : 'Cancel', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0); expect(trace.writes).toHaveLength(0);
    await page.getByTestId('reporting-export-detail').click(); await page.getByTestId('reporting-export-regenerate').click(); await expect(snapshot).toBeVisible();
    await page.getByTestId('reporting-export-submit').click(); await expect(page.getByTestId('reporting-export-receipt')).toContainText(id(4));
    expect(trace.writes).toEqual([{ definitionId: original.definitionId, versionNumber: 3, formatKey: 'excel', parameters: original.parameters }]); expect(trace.catalogReads()).toBe(0);
    await expect(page.getByTestId('reporting-export-regeneration-snapshot')).toHaveCount(0);
    await expect(page.getByTestId('reporting-export-detail-download')).toHaveCount(0);
  });
  test('独立重生成响应未知后关闭重开仍需核对，不自动再发 ' + width, async ({ page }) => {
    const trace = await boot(page, locale, true); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('reporting-export-detail').click(); await page.getByTestId('reporting-export-detail-download').click(); await page.getByTestId('reporting-export-regenerate').click();
    await page.getByTestId('reporting-export-submit').click(); await expect(page.getByTestId('reporting-export-submit')).toBeDisabled(); expect(trace.writes).toHaveLength(1);
    await page.getByRole('dialog').last().getByRole('button', { name: locale === 'zh-CN' ? '取消' : 'Cancel', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.getByTestId('reporting-export-detail').click(); await page.getByTestId('reporting-export-regenerate').click();
    await expect(page.getByTestId('reporting-export-recovery')).toBeVisible(); await expect(page.getByTestId('reporting-export-submit')).toBeDisabled();
    expect(trace.writes).toHaveLength(1); expect(trace.catalogReads()).toBe(0);
  });
  test('已取消原导出按当前授权另建且不需要下载或取消权限 ' + width, async ({ page }, info) => {
    const trace = await boot(page, locale, false, 'canceled', false); await page.setViewportSize({ width, height: 1000 });
    await page.getByTestId('reporting-export-detail').click(); await expect(page.getByTestId('reporting-export-detail-download')).toHaveCount(0);
    await page.getByTestId('reporting-export-regenerate').click(); await expect(page.getByTestId('reporting-export-regeneration-snapshot')).toContainText(original.id);
    await expect(page.getByTestId('reporting-export-regeneration-snapshot')).toContainText(locale === 'zh-CN' ? '原任务已失败或取消' : 'original task failed or was cancelled');
    await expect(page.getByRole('dialog')).toHaveCount(1); expect(trace.writes).toHaveLength(0);
    await page.getByRole('dialog').screenshot({ path: info.outputPath('export-original-terminal-' + width + '.png'), animations: 'disabled' });
    expect((await new AxeBuilder({ page }).include('.el-dialog').withTags(['wcag2a', 'wcag2aa']).analyze()).violations).toEqual([]);
    await page.getByTestId('reporting-export-submit').click(); await expect(page.getByTestId('reporting-export-receipt')).toContainText(id(4));
    expect(trace.writes).toEqual([{ definitionId: original.definitionId, versionNumber: 3, formatKey: 'excel', parameters: original.parameters }]); expect(trace.catalogReads()).toBe(0);
  });
}
