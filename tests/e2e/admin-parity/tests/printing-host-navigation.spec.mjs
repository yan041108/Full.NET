import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test.use({ viewport: { width: Number(process.env.FULLNET_ADMIN_PARITY_VIEWPORT_WIDTH ?? 1440), height: 1000 } });
test.afterEach(async ({ page }, info) => { await page.screenshot({ path: info.outputPath('page.png'), fullPage: true }); });
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const createdAtUtc = '2026-10-07T00:00:00Z';
const template = (key = id, name = '旧模板') => ({ id: key, templateKey: key === id ? 'old' : 'new', name, formSchemaKey: 'printing.tenant_profile_card',
  latestPublishedVersionNumber: 1, isEnabled: true, createdAtUtc, updatedAtUtc: null, version: 4 });
const version = (key, html) => ({ id: key, templateId: key, versionNumber: 1, changeNote: null, publishedByUserId: id, publishedAtUtc: createdAtUtc, layoutHtml: html });
const json = (route, body) => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
async function boot(page) {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' }));
  await page.route('**/api/v1/me', route => json(route, { id, username: 'fixture', displayName: 'Host', tenantId: null, actorScope: 'host', scope: 'host',
    isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['printing.templates.read', 'printing.templates.preview'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 }));
  await page.route('**/api/v1/navigation', route => json(route, [{ id: 'printing', parentId: null, routeName: 'printing-preview', path: '/printing/preview',
    componentKey: 'printing-preview', title: '打印版式', caption: '', icon: 'document', order: 1, requiredPermission: 'printing.templates.read', children: [] }]));
  await page.route('**/api/v1/printing/templates/*/versions?*', route => {
    const key = new URL(route.request().url()).pathname.split('/').at(-2), { layoutHtml: _, ...summary } = version(key, '');
    return json(route, { items: [summary], page: 1, pageSize: 20, total: 1 });
  });
}
async function previewVisible(page, text) {
  await expect(page.frameLocator('iframe.printing-preview-html').getByRole('heading', { name: text })).toBeVisible();
  const box = await page.locator('iframe.printing-preview-html').boundingBox(); expect(box.x).toBeGreaterThanOrEqual(0); expect(box.x + box.width).toBeLessThanOrEqual(page.viewportSize().width);
}
test('搜索赢过初始读取，Host版式按指定版本净化展示', async ({ page }) => {
  await boot(page); let initial = false, searched = false, release; const waiting = new Promise(resolve => { release = resolve; }); let boundReads = 0;
  await page.route('**/api/v1/printing/templates?*', async route => {
    const keyword = new URL(route.request().url()).searchParams.get('nameContains');
    if (!keyword) { initial = true; await waiting; await json(route, { items: [template()], page: 1, pageSize: 20, total: 1 }).catch(() => {}); }
    else { searched = true; await json(route, { items: [template(other, '新模板')], page: 1, pageSize: 20, total: 1 }); }
  });
  await page.route('**/api/v1/printing/templates/*/preview', route => { boundReads++; return route.fulfill({ status: 500 }); });
  await page.route('**/api/v1/printing/templates/' + other + '/versions/1', route => json(route, version(other, '<h1>新排版 {{tenantName}}</h1><script>window.__unsafeLayout=true</script><img src="x" onerror="window.__unsafeLayout=true">')));
  await page.goto('/#/printing/preview'); await expect.poll(() => initial).toBe(true);
  await page.getByTestId('printing-preview-template').getByRole('combobox').fill('新'); await expect.poll(() => searched).toBe(true);
  await page.getByRole('option', { name: '新模板', exact: true }).click(); release();
  await expect(page.getByRole('option', { name: '旧模板', exact: true })).toHaveCount(0);
  await page.getByTestId('printing-preview-run').click(); await previewVisible(page, '新排版 {{tenantName}}');
  await expect(page.frameLocator('iframe.printing-preview-html').locator('script,[onerror]')).toHaveCount(0); expect(boundReads).toBe(0);
});
test('版本目录失败可独立重试，切模板取消旧正文并展示新选择', async ({ page }) => {
  await boot(page); let historyReads = 0, pending = false, release; const waiting = new Promise(resolve => { release = resolve; });
  await page.route('**/api/v1/printing/templates?*', route => json(route, { items: [template(), template(other, '另一个模板')], page: 1, pageSize: 20, total: 2 }));
  await page.route('**/api/v1/printing/templates/' + id + '/versions?*', route => {
    historyReads++; const { layoutHtml: _, ...summary } = version(id, '');
    return historyReads === 1 ? route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, title: '历史暂不可用', code: 'fixture.history_unavailable' }) })
      : json(route, { items: [summary], page: 1, pageSize: 20, total: 1 });
  });
  await page.route('**/api/v1/printing/templates/' + id + '/versions/1', async route => { pending = true; await waiting; await json(route, version(id, '<h1>旧正文</h1>')).catch(() => {}); });
  await page.route('**/api/v1/printing/templates/' + other + '/versions/1', route => json(route, version(other, '<h1>另一个版式 {{tenantName}}</h1>')));
  await page.goto('/#/printing/preview'); await expect(page.getByTestId('printing-template-history-problem')).toContainText('历史暂不可用');
  await page.getByTestId('printing-template-history-retry').click(); await expect(page.getByTestId('printing-template-history-problem')).toHaveCount(0); expect(historyReads).toBe(2);
  // 版式禁止脚本，先扫描管理控件；随后以真实 DOM 验证沙箱正文，不放宽 sandbox。
  await expect.poll(async () => (await new AxeBuilder({ page }).include('.printing-preview-view').analyze()).violations.map(item => item.id)).toEqual([]);
  await page.getByTestId('printing-preview-run').click(); await expect.poll(() => pending).toBe(true);
  await page.getByTestId('printing-preview-template').click(); await page.getByRole('option', { name: '另一个模板', exact: true }).click(); release();
  await page.getByTestId('printing-preview-run').click(); await previewVisible(page, '另一个版式 {{tenantName}}');
  await expect(page.frameLocator('iframe.printing-preview-html').getByText('旧正文')).toHaveCount(0);
  await expect(page.locator('iframe.printing-preview-html')).toHaveAttribute('sandbox', 'allow-same-origin allow-modals');
  await expect(page.locator('iframe.printing-preview-html')).toHaveAttribute('title', /.+/);
  await expect(page.getByTestId('printing-preview-print')).toHaveAccessibleName('浏览器打印');
});
test('打印重新读取同一不可变版本，拒绝时不输出，恢复后只在新正文加载时输出一次', async ({ page }) => {
  await boot(page); let reads = 0, denied = false;
  await page.addInitScript(() => {
    window.__layoutPrints = 0;
    document.addEventListener('load', event => {
      const frame = event.target;
      if (frame instanceof HTMLIFrameElement && frame.classList.contains('printing-preview-html') && frame.contentWindow) {
        frame.contentWindow.print = () => { window.__layoutPrints++; window.__printedLayout = frame.contentDocument.body.textContent; };
      }
    }, true);
  });
  await page.route('**/api/v1/printing/templates?*', route => json(route, { items: [template()], page: 1, pageSize: 20, total: 1 }));
  await page.route('**/api/v1/printing/templates/' + id + '/versions/1', route => {
    reads++; return denied ? route.fulfill({ status: 403, contentType: 'application/problem+json', body: JSON.stringify({ status: 403, code: 'fixture.denied', title: '版式读取已撤权' }) })
      : json(route, version(id, '<h1>版式 {{tenantName}}</h1>'));
  });
  await page.goto('/#/printing/preview'); await page.getByTestId('printing-preview-run').click(); await previewVisible(page, '版式 {{tenantName}}');
  expect(await page.evaluate(() => window.__layoutPrints)).toBe(0); denied = true; await page.getByTestId('printing-preview-print').click();
  await expect(page.locator('iframe.printing-preview-html')).toHaveCount(0); await expect(page.getByText('版式读取已撤权', { exact: true })).toBeVisible();
  expect(reads).toBe(2); expect(await page.evaluate(() => window.__layoutPrints)).toBe(0);
  denied = false; await page.getByTestId('printing-preview-run').click(); await previewVisible(page, '版式 {{tenantName}}');
  await page.getByTestId('printing-preview-print').click(); await expect.poll(() => page.evaluate(() => window.__layoutPrints)).toBe(1);
  expect(reads).toBe(4); expect(await page.evaluate(() => window.__printedLayout)).toContain('版式 {{tenantName}}');
});
test('已观察目录修订不能回退，陈旧目录禁用预览，人工刷新可恢复', async ({ page }) => {
  await boot(page); let revision = 4;
  await page.route('**/api/v1/printing/templates?*', route => json(route, { items: [{ ...template(), version: revision }], page: 1, pageSize: 20, total: 1 }));
  await page.goto('/#/printing/preview'); await expect(page.getByTestId('printing-preview-run')).toBeEnabled();
  revision = 9; await page.getByTestId('printing-template-refresh').click(); await expect(page.getByTestId('printing-template-versions')).toContainText('9');
  revision = 4; await page.getByTestId('printing-template-refresh').click(); await expect(page.getByTestId('printing-preview-run')).toBeDisabled();
  await expect(page.getByTestId('printing-template-versions')).toContainText('9');
  revision = 9; await page.getByTestId('printing-template-refresh').click(); await expect(page.getByTestId('printing-preview-run')).toBeEnabled();
});
