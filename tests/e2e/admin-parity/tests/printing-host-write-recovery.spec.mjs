import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', createdId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const createdAtUtc = '2026-11-03T00:00:00Z';
const draft = { id, templateKey: 'existing', name: '现有草稿', formSchemaKey: 'printing.tenant_profile_card', layoutHtml: '<h1>Draft</h1>', latestPublishedVersionNumber: 1,
  isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 7 };
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
const summary = ({ layoutHtml: _, ...value }) => value;
async function boot(page, rows = () => [draft]) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['read', 'create', 'update', 'publish'].map(action => 'printing.templates.' + action),
    sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'printing', parentId: null, routeName: 'printing-preview', path: '/printing/preview', componentKey: 'printing-preview',
    title: '打印版式', caption: '', icon: 'document', order: 1, requiredPermission: 'printing.templates.read', children: [] }]));
  await page.route('**/api/v1/printing/templates?*', route => json(route, { items: rows().map(summary), page: 1, pageSize: 20, total: rows().length }));
  await page.route('**/api/v1/printing/templates/*/versions?*', route => json(route, { items: [], page: 1, pageSize: 20, total: 0 }));
  await page.route('**/api/v1/printing/templates/' + id, route => json(route, rows().find(item => item.id === id)));
  await page.goto('/#/printing/preview'); await expect(page.getByTestId('printing-preview-create')).toBeEnabled();
}
async function fits(dialog, page) {
  await expect.poll(async () => { const box = await dialog.boundingBox(); return !!box && box.x >= 0 && box.x + box.width <= page.viewportSize().width; }).toBe(true);
  await expect.poll(() => dialog.evaluate(element => {
    const panel = element.querySelector('.el-dialog') ?? element;
    // 动画作用于面罩和定位层，不能只检查内容面板。
    for (let node = panel; node; node = node.parentElement) {
      if (getComputedStyle(node).opacity !== '1' || node.getAnimations().some(animation => animation.playState === 'running')) return false;
    }
    return true;
  })).toBe(true);
}
test('未知草稿跨关闭和刷新保护，人工核对只解锁下一次保存', async ({ page }, info) => {
  let creates = 0, publishes = 0, saved;
  await boot(page, () => saved ? [saved, draft] : [draft]);
  await page.route('**/api/v1/printing/templates', route => {
    creates++; const body = route.request().postDataJSON(); expect(body.templateKey).toBe('new-draft');
    if (creates === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'fixture.lost', title: '结果未确认' }) });
    saved = { ...draft, ...body, id: createdId, latestPublishedVersionNumber: 0, version: 1 }; return json(route, saved);
  });
  await page.route('**/api/v1/printing/templates/*/publish', route => { publishes++; return route.fulfill({ status: 500 }); });
  await page.getByTestId('printing-preview-create').click(); await page.getByTestId('printing-template-key').fill('new-draft');
  await page.getByTestId('printing-template-name').fill('待核对草稿'); await page.getByTestId('printing-template-layout').fill('<h1>New</h1>');
  await page.getByTestId('printing-preview-submit').click(); await expect(page.getByTestId('printing-preview-submit')).toBeDisabled();
  await expect(page.getByTestId('printing-write-receipt')).toContainText('结果尚未确认');
  await page.getByTestId('printing-preview-create-back').click(); await page.getByTestId('printing-template-refresh').click();
  await page.getByTestId('printing-preview-create').click(); await expect(page.getByTestId('printing-template-name')).toHaveValue('待核对草稿');
  await expect(page.getByTestId('printing-preview-submit')).toBeDisabled();
  await expect(page.getByTestId('printing-preview-submit')).toHaveAccessibleDescription('创建结果尚未确认，请先刷新核对再决定是否重新提交。'); expect(creates).toBe(1);
  await fits(page.getByRole('dialog'), page); await page.screenshot({ path: info.outputPath('create-unknown.png'), fullPage: true, animations: 'disabled' });
  await page.getByTestId('printing-create-review').click();
  await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.getByTestId('printing-create-review-confirm').click(); await expect(page.getByTestId('printing-preview-submit')).toBeEnabled(); expect(creates).toBe(1);
  await page.getByTestId('printing-preview-submit').click(); await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.getByTestId('printing-write-receipt')).toContainText('草稿已保存'); expect(creates).toBe(2); expect(publishes).toBe(0);
});
test('关闭在途发布保留未知，刷新核对不自动重复发布', async ({ page }, info) => {
  let writes = 0, committed = false, release; const pending = new Promise(resolve => { release = resolve; });
  await boot(page, () => [{ ...draft, version: committed ? 8 : 7, latestPublishedVersionNumber: committed ? 2 : 1 }]);
  await page.route('**/api/v1/printing/templates/' + id + '/publish', async route => {
    writes++; expect(route.request().postDataJSON()).toEqual({ version: 7, changeNote: 'Reviewed' }); committed = true; await pending;
    await json(route, { id: createdId, templateId: id, versionNumber: 2, layoutHtml: draft.layoutHtml, changeNote: 'Reviewed', publishedByUserId: id, publishedAtUtc: createdAtUtc }).catch(() => {});
  });
  await page.getByTestId('printing-template-publish').click(); await page.getByTestId('printing-template-change-note').fill('Reviewed');
  await fits(page.getByRole('dialog'), page); await expect.poll(async () => (await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.screenshot({ path: info.outputPath('publish-open.png'), fullPage: true });
  await page.getByTestId('printing-template-confirm-publish').click(); await expect.poll(() => writes).toBe(1);
  await expect(page.getByRole('dialog')).toContainText('关闭只停止本窗口等待'); await page.getByTestId('printing-publish-close').click();
  await expect(page.getByRole('dialog')).toHaveCount(0); release();
  await expect(page.getByTestId('printing-write-receipt')).toContainText('结果尚未确认'); await page.getByTestId('printing-template-refresh').click();
  await expect(page.getByTestId('printing-template-versions')).toContainText('8'); await expect(page.getByTestId('printing-template-versions')).toContainText('2'); expect(writes).toBe(1);
});
