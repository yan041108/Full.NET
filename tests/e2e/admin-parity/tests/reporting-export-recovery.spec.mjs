import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const createdAtUtc = '2026-10-07T00:00:00Z';
const publication = { id, definitionKey: 'fixture', name: '报表夹具', versionNumber: 3, parameterSchema: [] };
const task = { id, definitionId: id, definitionKey: 'fixture', definitionName: '报表夹具', versionNumber: 3,
  formatKey: 'excel', statusKey: 'queued', rowCount: 0, outputFileName: null, errorCode: null, errorMessage: null,
  requestedByUserId: id, createdAtUtc, completedAtUtc: null };
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, rows = [task]) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: '查看者', tenantId: id,
    actorScope: `tenant:${id.replaceAll('-', '')}`, scope: `tenant:${id.replaceAll('-', '')}`, isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['reporting.export_tasks.read', 'reporting.export_tasks.create', 'reporting.export_tasks.cancel', 'reporting.published_definitions.read'],
    sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'exports', parentId: null, routeName: 'reporting-export-tasks', path: '/reporting/export-tasks',
    componentKey: 'reporting-export-tasks', title: '报表导出', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.export_tasks.read', children: [] }]));
  await page.route('**/api/v1/reporting/export-tasks?*', route => json(route, { items: rows, page: 1, pageSize: 20, total: rows.length }));
  await page.route('**/api/v1/reporting/published-definitions?*', route => json(route, { items: [publication], page: 1, pageSize: 20, total: 1 }));
  await page.goto('/#/reporting/export-tasks');
  await expect(page.getByTestId('reporting-export-create')).toBeEnabled();
}
async function dialogFits(page) {
  await expect.poll(async () => {
    const box = await page.getByRole('dialog').boundingBox();
    return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width;
  }).toBe(true);
}

test('最小目录按需加载，未知创建核对后只解锁人工提交', async ({ page }) => {
  let directoryReads = 0, writes = 0;
  await boot(page, []);
  await page.route('**/api/v1/reporting/published-definitions?*', route => {
    directoryReads++; return json(route, { items: [publication], page: 1, pageSize: 20, total: 1 });
  });
  expect(directoryReads).toBe(0);
  await page.route('**/api/v1/reporting/export-tasks', route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ definitionId: id, versionNumber: 3, formatKey: 'excel', parameters: [] });
    return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, title: '结果尚未确认', code: 'gateway.unavailable' }) });
  });
  await page.getByTestId('reporting-export-create').click();
  await expect(page.getByTestId('reporting-export-submit')).toBeEnabled();
  await dialogFits(page); expect(directoryReads).toBe(1);
  await page.getByTestId('reporting-export-submit').click();
  await expect(page.getByTestId('reporting-export-submit')).toBeDisabled();
  await expect(page.getByTestId('reporting-export-receipt')).toContainText('结果');
  expect(writes).toBe(1);
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).click();
  await page.getByTestId('reporting-export-create').click();
  await expect(page.getByTestId('reporting-export-recovery')).toBeVisible();
  await page.getByTestId('reporting-export-recovery').click();
  await page.getByTestId('reporting-export-recovery-confirm').click();
  await expect(page.getByTestId('reporting-export-submit')).toBeEnabled();
  expect(writes).toBe(1);
  // 关闭恢复确认后检查业务弹窗本身，保留真实控件与键盘操作。
  await page.getByRole('dialog').getByRole('button', { name: '取消', exact: true }).focus();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('取消先核对详情版本，确认取消后旧列表不能恢复排队状态', async ({ page }) => {
  await boot(page); let reads = 0, writes = 0;
  await page.route('**/api/v1/reporting/export-tasks/' + id, route => {
    reads++; return json(route, { ...task, version: 7, parameters: [], outputFileId: null });
  });
  await page.route('**/api/v1/reporting/export-tasks/' + id + '/cancel', route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ version: 7 });
    return json(route, { ...task, statusKey: 'canceled', version: 8, parameters: [], outputFileId: null });
  });
  await page.getByTestId('reporting-export-cancel').click();
  await dialogFits(page); expect(reads).toBe(0); expect(writes).toBe(0);
  await page.getByTestId('reporting-cancel-confirm').click();
  await expect(page.getByTestId('reporting-export-receipt')).toContainText(id);
  await expect(page.getByTestId('reporting-export-cancel')).toHaveCount(0);
  expect(reads).toBe(1); expect(writes).toBe(1);
  await page.getByTestId('reporting-export-detail').click();
  await expect(page.getByRole('dialog')).toContainText('任务已取消');
  await dialogFits(page);
  await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
});
