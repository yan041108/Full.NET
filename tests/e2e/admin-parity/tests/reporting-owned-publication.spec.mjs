import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true, animations: 'disabled' }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', related = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const createdAtUtc = '2026-11-03T00:00:00Z';
const draft = { id, groupId: related, dataSourceId: related, definitionKey: 'report-review', name: '发布验收报表', description: null, queryPortKey: 'metadata.schemas',
  parameterSchema: [], layoutConfigJson: '{}', latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 7 };
const summary = (version = 4, published = 1) => { const { layoutConfigJson: _, parameterSchema: __, ...row } = draft; return { ...row, version, latestPublishedVersionNumber: published }; };
const version = note => ({ id: related, definitionId: id, versionNumber: 2, dataSourceId: related, queryPortKey: draft.queryPortKey, parameterSchema: [], layoutConfigJson: '{}',
  changeNote: note, publishedByUserId: id, publishedAtUtc: createdAtUtc });
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page, row = () => summary()) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['reporting.definitions.read', 'reporting.definitions.publish'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'reporting', parentId: null, routeName: 'reporting-definitions', path: '/reporting/definitions', componentKey: 'reporting-definitions',
    title: '报表发布', caption: '', icon: 'document', order: 1, requiredPermission: 'reporting.definitions.read', children: [] }]));
  await page.route('**/api/v1/reporting/definitions?*', route => json(route, { items: [row()], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/reporting/definitions/' + id, route => json(route, draft));
  await page.goto('/#/reporting/definitions'); await expect(page.getByTestId('reporting-config-publish')).toBeEnabled();
}
async function open(page, note) {
  await page.getByTestId('reporting-config-publish').click(); await expect(page.getByTestId('reporting-publication-target-version')).toContainText('7');
  await page.getByTestId('reporting-config-publish-note').fill(note);
  await expect.poll(async () => { const box = await page.getByRole('dialog').boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
  await expect.poll(() => page.getByRole('dialog').evaluate(element => {
    for (let node = element.querySelector('.el-dialog') ?? element; node; node = node.parentElement)
      if (getComputedStyle(node).opacity !== '1' || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    return true;
  })).toBe(true);
}
const row = page => page.getByRole('row').filter({ hasText: '发布验收报表' });
test('确认精确发布版本后，陈旧目录和刷新不能回退或重放发布', async ({ page }, info) => {
  await boot(page); let writes = 0;
  await page.route('**/api/v1/reporting/definitions/' + id + '/publish', route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ version: 7, changeNote: 'Approved' }); return json(route, version('Approved'));
  });
  await open(page, 'Approved'); await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath('publication-open.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('reporting-config-publish-confirm').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('reporting-publication-receipt').locator('span')).toHaveText('2'); await expect(row(page).getByRole('cell').nth(3)).toHaveText('2');
  await expect(page.getByTestId('reporting-config-unconfirmed')).toHaveCount(0); await page.getByTestId('reporting-config-refresh').click();
  await expect(row(page).getByRole('cell').nth(3)).toHaveText('2'); expect(writes).toBe(1);
});
test('关闭在途发布取消接入，读取实际新版本不自动重发', async ({ page }) => {
  let committed = false, writes = 0, cancelled = false, release; const pending = new Promise(resolve => { release = resolve; });
  await boot(page, () => committed ? summary(8, 2) : summary());
  page.on('requestfailed', request => { if (request.url().endsWith('/' + id + '/publish')) cancelled = true; });
  await page.route('**/api/v1/reporting/definitions/' + id + '/publish', async route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ version: 7, changeNote: 'Close review' }); committed = true; await pending;
    await json(route, version('Close review')).catch(() => {});
  });
  await open(page, 'Close review'); await page.getByTestId('reporting-config-publish-confirm').click(); await expect.poll(() => writes).toBe(1);
  await expect(page.getByRole('dialog')).toContainText('关闭只停止本窗口等待'); await page.getByTestId('reporting-publication-close').click();
  await expect(page.getByRole('dialog')).toHaveCount(0); await expect.poll(() => cancelled).toBe(true); release();
  await expect(page.getByTestId('reporting-publication-receipt')).toContainText('发布结果尚未确认'); await page.getByTestId('reporting-config-refresh').click();
  await expect(row(page).getByRole('cell').nth(3)).toHaveText('2'); expect(writes).toBe(1);
});
async function openHistory(page) {
  await row(page).getByTestId('art-table-action-more').click(); await page.getByTestId('reporting-config-history').click();
}
const historyRow = (number, key = '019bc2b1-2a40-7cc3-8992-' + (1000 + number).toString(16).padStart(12, '0')) => ({ id: key, definitionId: id, dataSourceId: related, versionNumber: number,
  queryPortKey: draft.queryPortKey, changeNote: '历史' + number, publishedByUserId: id, publishedAtUtc: createdAtUtc });
test('发布历史失败独立重试，按二十条分页并在窄屏可关闭', async ({ page }, info) => {
  await boot(page); const pages = []; let unavailable = true;
  await page.route('**/api/v1/reporting/definitions/' + id + '/versions?*', route => {
    const query = new URL(route.request().url()).searchParams; const pageNumber = Number(query.get('page')); pages.push(pageNumber); expect(query.get('pageSize')).toBe('20');
    if (unavailable) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'fixture.history', title: '历史暂不可用' }) });
    return json(route, { items: pageNumber === 1 ? Array.from({ length: 20 }, (_, index) => historyRow(21 - index)) : [historyRow(1)], total: 21, page: pageNumber, pageSize: 20 });
  });
  await openHistory(page); const dialog = page.getByRole('dialog'); await expect(dialog.getByTestId('reporting-history-problem')).toContainText('历史暂不可用');
  await expect(page.getByTestId('reporting-config-unconfirmed')).toHaveCount(0); unavailable = false;
  await page.getByTestId('reporting-history-retry').click(); await expect(dialog.getByRole('cell', { name: '历史21', exact: true })).toBeVisible();
  await expect.poll(async () => { const box = await dialog.boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
  await expect.poll(() => dialog.evaluate(element => { for (let node = element; node; node = node.parentElement) if (getComputedStyle(node).opacity !== '1' || node.getAnimations().some(animation => animation.playState === 'running')) return false; return true; })).toBe(true);
  await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  const scroll = dialog.locator('.el-table__body-wrapper .el-scrollbar__wrap'); await expect(scroll).toHaveAttribute('tabindex', '0');
  if (page.viewportSize().width < 600) { await scroll.focus(); await page.keyboard.press('ArrowRight'); await expect.poll(() => scroll.evaluate(element => element.scrollLeft)).toBeGreaterThan(0); }
  await page.screenshot({ path: info.outputPath('publication-history.png'), fullPage: true, animations: 'disabled' });
  await dialog.locator('.btn-next').click(); await expect(dialog.getByRole('cell', { name: '历史1', exact: true })).toBeVisible(); expect(pages).toEqual([1, 1, 2]);
  await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0);
});
test('已确认发布号的历史身份错配被拒绝，重试只核对而不发布', async ({ page }) => {
  await boot(page); let writes = 0, correct = false;
  await page.route('**/api/v1/reporting/definitions/' + id + '/publish', route => { writes++; return json(route, version('History review')); });
  await open(page, 'History review'); await page.getByTestId('reporting-config-publish-confirm').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.route('**/api/v1/reporting/definitions/' + id + '/versions?*', route => json(route, { items: [historyRow(2, correct ? related : undefined), historyRow(1)], total: 2, page: 1, pageSize: 20 }));
  await openHistory(page); await expect(page.getByTestId('reporting-history-problem')).toBeVisible();
  await expect(page.getByRole('dialog').getByRole('cell', { name: '历史2', exact: true })).toHaveCount(0);
  correct = true; await page.getByTestId('reporting-history-retry').click(); await expect(page.getByRole('dialog').getByRole('cell', { name: '历史2', exact: true })).toBeVisible();
  expect(writes).toBe(1); await page.goto('/#/'); await expect(page.getByRole('dialog')).toHaveCount(0);
});
