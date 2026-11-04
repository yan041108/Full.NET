import { expect, test } from '@playwright/test';

// 由独立生产预览入口执行；开发服务器的原生 ESM 重导出不代表生产拆包结果。

test('匿名首屏显示登录且不请求完整业务 SDK', async ({ page }) => {
  const scripts = [];
  page.on('request', request => { if (request.resourceType() === 'script') scripts.push(new URL(request.url()).pathname); });
  await page.route('**/api/v1/**', route => route.fulfill({ status: 401, contentType: 'application/problem+json',
    body: JSON.stringify({ status: 401, code: 'identity.invalid_credentials', title: '请登录' }) }));
  await page.goto('/#/login');
  await expect(page.locator('input[name="username"]')).toBeVisible();
  await expect(page.locator('input[name="password"]')).toBeVisible();
  await page.waitForLoadState('networkidle');
  expect(scripts.some(path => path.endsWith('.js'))).toBe(true);
  expect(scripts.filter(path => /\/operations\.generated-[^/]+\.js$/u.test(path))).toEqual([]);
});

async function authenticatedStartup(page) {
  const state = { unreadReads: 0, scripts: [] };
  page.on('request', request => { if (request.resourceType() === 'script') state.scripts.push(new URL(request.url()).pathname); });
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname;
    const json = body => route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/api/v1/auth/refresh') return json({ accessToken: 'fixture', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' });
    if (path === '/api/v1/me') return json({ id: '019bc2b1-2a40-7cc3-8992-a80de51bf294', username: 'startup', displayName: '启动账号',
      actorScope: 'host', scope: 'host', tenantId: null, isSuperAdministrator: false, passwordChangeRequired: false,
      sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf295', preferredLocale: 'zh-CN', profileVersion: '1', permissions: ['notifications.inbox.read'] });
    if (path === '/api/v1/navigation') return json([]);
    if (path.endsWith('/unread-count')) { state.unreadReads++; return json({ unreadCount: 2 }); }
    if (path.endsWith('/latest-unread')) return route.fulfill({ status: 204 });
    return json([]);
  });
  return state;
}

test('生产启动入口正常恢复会话并读取通知未读数', async ({ page }) => {
  const state = await authenticatedStartup(page);
  await page.goto('/#/403');
  await expect.poll(() => state.unreadReads).toBeGreaterThan(0);
  await expect(page.locator('input[name="username"]')).toHaveCount(0);
});

test('设置面板首次打开才请求代码，键盘关闭后可重新打开', async ({ page }) => {
  const state = await authenticatedStartup(page);
  await page.goto('/#/403');
  const button = page.locator('.art-header__setting-btn');
  await expect(button).toBeVisible();
  const loaded = () => state.scripts.filter(path => /\/ArtSettingsPanel-[^/]+\.js$/u.test(path));
  expect(loaded()).toEqual([]);
  await button.click(); await expect(page.locator('.art-settings-drawer')).toBeVisible();
  expect(loaded()).toHaveLength(1);
  await page.keyboard.press('Escape'); await expect(page.locator('.art-settings-drawer')).toBeHidden();
  await button.click(); await expect(page.locator('.art-settings-drawer')).toBeVisible();
  expect(loaded()).toHaveLength(1);
});
