import { expect, test } from '@playwright/test';

const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const settings = { minimumRetainedVersionsPerItem: 1, maximumRetainedHistoryVersions: 0, pollSeconds: 300, batchSize: 50 };
const statistics = { summary: { totalItems: 3, totalVersions: 5, totalSizeKb: 1024, totalSizeInfo: '1 MB' }, byType: [{ extension: 'pdf', count: 3, totalSizeKb: 1024 }], byCategory: [], shareCount: 1, todayAccessCount: 0, todayDownloadCount: 0, todayCreatedCount: 1, recycleBinCount: 0 };
const log = { id, documentItemId: id, documentTitle: '旧访问日志', accessTypeKey: 'preview', sourceKey: 'authenticated', actorUserId: id, occurredAtUtc: '2026-10-10T00:00:00Z', clientIpFingerprint: null };
const pageResult = { items: [log], page: 1, pageSize: 20, total: 100 };
const json = (route, value) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) });
async function boot(page, permissions = ['document.host_statistics.read', 'document.host_access_logs.read', 'document.host_documents.read', 'document.host_documents.update']) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions, sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'fixture', parentId: null, routeName: 'document-statistics', path: '/document/statistics', componentKey: 'document-statistics', title: '文档统计', caption: '', icon: 'document', order: 1, requiredPermission: 'document.host_statistics.read', children: [] }]));
  await page.route('**/api/v1/document/host/statistics', route => json(route, statistics));
  await page.route('**/api/v1/document/host/access-logs?*', route => json(route, pageResult));
  await page.route('**/api/v1/document/host/version-retention', route => json(route, settings));
  await page.goto('/#/document/statistics'); await expect(page.getByTestId('document-statistics-panel')).toContainText('1 MB');
}
const refresh = page => page.getByTestId('document-statistics-refresh').click();
const tabs = page => page.getByTestId('document-statistics-tabs');
const retentionTab = page => tabs(page).getByRole('tab', { name: '版本保留', exact: true }).click();
const logsTab = page => tabs(page).getByRole('tab', { name: '访问日志', exact: true }).click();
const statsTab = page => tabs(page).getByRole('tab').first().click();
const saveButton = page => page.getByTestId('document-version-retention-save');

test('子页签按独立read隐藏，update不能代替read', async ({ page }) => {
  let reads = 0; await boot(page, ['document.host_statistics.read', 'document.host_documents.update']);
  await page.route('**/api/v1/document/host/version-retention', route => { reads++; return json(route, settings); });
  await expect(tabs(page).getByRole('tab')).toHaveCount(1); await expect(saveButton(page)).toHaveCount(0); expect(reads).toBe(0);
});

test('只读设置允许刷新，操作权限缺失无保存入口', async ({ page }) => {
  await boot(page, ['document.host_statistics.read', 'document.host_documents.read']); await retentionTab(page);
  await expect(page.getByTestId('document-version-retention-panel')).toContainText('50'); await expect(saveButton(page)).toHaveCount(0);
  let reads = 0; await page.route('**/api/v1/document/host/version-retention', route => { reads++; return json(route, { ...settings, batchSize: 77 }); });
  await refresh(page); await expect(page.getByTestId('document-version-retention-panel')).toContainText('77'); expect(reads).toBe(1);
});

test('不合法读取关闭保存，刷新恢复后空必填数值不能写入', async ({ page }) => {
  await boot(page); let reads = 0, writes = 0;
  await page.route('**/api/v1/document/host/version-retention', route => {
    if (route.request().method() === 'PUT') { writes++; return json(route, settings); }
    return json(route, ++reads === 1 ? { ...settings, pollSeconds: 1 } : settings);
  });
  await retentionTab(page); await expect(page.locator('.el-alert--error')).toBeVisible(); await expect(saveButton(page)).toHaveCount(0);
  await refresh(page); await expect(saveButton(page)).toBeEnabled(); const input = page.getByRole('spinbutton').first();
  await input.fill(''); await input.blur(); await expect(saveButton(page)).toBeDisabled(); expect(writes).toBe(0);
  await input.fill('2'); await input.blur(); await expect(saveButton(page)).toBeEnabled();
});

test('保存响应错配必须重读，后续保存保留新设置', async ({ page }) => {
  await boot(page); let writes = 0, committed = false;
  const latest = { ...settings, minimumRetainedVersionsPerItem: 2, batchSize: 77 };
  await page.route('**/api/v1/document/host/version-retention', route => {
    if (route.request().method() === 'GET') return json(route, committed ? latest : settings);
    const body = route.request().postDataJSON(); writes++;
    if (writes === 1) { expect(body).toEqual({ ...settings, minimumRetainedVersionsPerItem: 2 }); committed = true; return json(route, latest); }
    expect(body).toEqual({ ...latest, pollSeconds: 600 }); return json(route, body);
  });
  await retentionTab(page); await expect(saveButton(page)).toBeEnabled(); await page.getByRole('spinbutton').nth(0).fill('2'); await page.getByRole('spinbutton').nth(0).blur(); await saveButton(page).click();
  await expect(page.locator('.el-alert--error')).toBeVisible(); await expect(saveButton(page)).toHaveCount(0); expect(writes).toBe(1);
  await refresh(page); await expect(page.getByRole('spinbutton').nth(3)).toHaveValue('77'); await page.getByRole('spinbutton').nth(2).fill('600'); await page.getByRole('spinbutton').nth(2).blur(); await saveButton(page).click();
  await expect(page.locator('.el-message--success')).toBeVisible(); expect(writes).toBe(2);
});

test('离开页签取消保存且不显示迟到成功，返回重新读取', async ({ page }) => {
  await boot(page); let release, started = false, finished = false, cancelled = false, reads = 0;
  const pending = new Promise(resolve => { release = resolve; });
  page.on('requestfailed', request => { if (request.method() === 'PUT' && request.url().endsWith('/version-retention')) cancelled = true; });
  await page.route('**/api/v1/document/host/version-retention', async route => {
    if (route.request().method() === 'GET') { reads++; return json(route, reads === 1 ? settings : { ...settings, batchSize: 77 }); }
    started = true; await pending; await json(route, settings).catch(() => {}); finished = true;
  });
  await retentionTab(page); await expect(saveButton(page)).toBeEnabled(); await saveButton(page).click(); await expect.poll(() => started).toBe(true);
  await expect(page.getByRole('spinbutton').first()).toBeDisabled(); await expect(page.getByTestId('document-statistics-refresh')).toBeDisabled();
  await statsTab(page); await expect(page.getByTestId('document-statistics-panel')).toBeVisible(); release(); await expect.poll(() => finished).toBe(true); await expect.poll(() => cancelled).toBe(true); await expect(page.locator('.el-message--success,.el-alert--error')).toHaveCount(0);
  await retentionTab(page); await expect(page.getByRole('spinbutton').nth(3)).toHaveValue('77'); expect(reads).toBe(2);
});

test('日志替换取消旧请求，失败清空行和分页', async ({ page }) => {
  await boot(page); await logsTab(page); await expect(page.getByTestId('document-access-logs-table')).toContainText('旧访问日志');
  let release, started = false, finished = false, cancelled = false, calls = 0;
  const pending = new Promise(resolve => { release = resolve; });
  page.on('requestfailed', request => { if (request.url().includes('/access-logs?')) cancelled = true; });
  await page.route('**/api/v1/document/host/access-logs?*', async route => {
    if (++calls === 1) { started = true; await pending; await json(route, pageResult).catch(() => {}); finished = true; return; }
    return route.fulfill({ status: 500, contentType: 'application/problem+json', body: JSON.stringify({ title: '新筛选失败', status: 500, code: 'fixture.failed' }) });
  });
  await page.getByRole('button', { name: '筛选', exact: true }).click(); await expect.poll(() => started).toBe(true); await page.getByRole('button', { name: '筛选', exact: true }).click();
  await expect(page.locator('.el-alert--error')).toContainText('新筛选失败'); release(); await expect.poll(() => finished).toBe(true); await expect.poll(() => cancelled).toBe(true);
  await expect(page.getByTestId('document-access-logs-table')).toHaveCount(0); await expect(page.locator('.el-pagination')).toHaveCount(0); await expect(page.getByTestId('document-access-logs-empty')).toBeVisible();
});

test('卸载取消在途读取，返回读取新设置', async ({ page }) => {
  await boot(page); let release, started = false, finished = false, reads = 0, cancelled = false;
  const pending = new Promise(resolve => { release = resolve; });
  page.on('requestfailed', request => { if (request.url().endsWith('/version-retention')) cancelled = true; });
  await page.route('**/api/v1/document/host/version-retention', async route => {
    if (++reads === 1) { started = true; await pending; await json(route, { ...settings, batchSize: 888 }).catch(() => {}); finished = true; return; }
    return json(route, settings);
  });
  await retentionTab(page); await expect.poll(() => started).toBe(true); await page.goto('/#/document/share/PUBLIC'); await expect(page.getByTestId('document-version-retention-panel')).toHaveCount(0); release();
  await expect.poll(() => finished).toBe(true); await expect.poll(() => cancelled).toBe(true);
  await page.goto('/#/document/statistics'); await retentionTab(page); await expect(page.getByRole('spinbutton').nth(3)).toHaveValue('50'); expect(reads).toBe(2);
});
