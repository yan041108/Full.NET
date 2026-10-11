import { expect, test } from '@playwright/test';

const id = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60';
const token = { accessToken: 'fixture-access', tokenType: 'Bearer', expiresAtUtc: '2099-01-01T00:00:00Z' };
const user = { id, username: 'fixture', displayName: 'Fixture', tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false, passwordChangeRequired: false, permissions: ['platform.dashboard.read'], sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 };
const navigation = [{ id: 'overview', parentId: null, routeName: 'overview', path: '/', componentKey: 'overview', title: 'Overview', caption: '', icon: 'dashboard', order: 10, requiredPermission: 'platform.dashboard.read', children: [] }];
const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN'));
  await page.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await page.route('**/api/v1/auth/refresh', route => json(route, { status: 401, code: 'identity.refresh_missing', title: 'No session' }, 401));
  await page.route('**/api/v1/identity/oauth/providers', route => json(route, []));
});

test('OAuth 成功标记不能代替实际认证恢复', async ({ page }) => {
  await page.goto('/#/oauth/callback?oauth=success');
  await expect(page.getByText('会话恢复失败，请重新登录', { exact: true })).toBeVisible();
  await expect(page.locator('input[name="password"]')).toBeVisible();
  await expect(page.getByText('外部身份登录成功', { exact: true })).toHaveCount(0);
});

test('OAuth 真实 App 状态转换只恢复一次并离开回调页', async ({ page }) => {
  let refreshes = 0;
  await page.route('**/api/v1/auth/refresh', route => { refreshes++; return json(route, token); });
  await page.route('**/api/v1/me', route => json(route, user));
  await page.route('**/api/v1/navigation', route => json(route, navigation));
  await page.goto('/#/oauth/callback?oauth=success');
  await expect(page.getByText('外部身份登录成功', { exact: true })).toBeVisible();
  await expect(page).toHaveURL(/#\/$/);
  await expect(page.locator('.art-admin-shell')).toBeVisible();
  expect(refreshes).toBe(1);
});

test('登录失败清理密码，重新输入后只提交一次并成功进入壳层', async ({ page }) => {
  let attempts = 0;
  await page.route('**/api/v1/auth/login', route => { attempts++; return attempts === 1
    ? json(route, { status: 401, code: 'identity.invalid_credentials', title: 'Invalid credentials' }, 401) : json(route, token); });
  await page.route('**/api/v1/me', route => json(route, user));
  await page.route('**/api/v1/navigation', route => json(route, navigation));
  await page.goto('/#/login');
  await page.locator('input[name="username"]').fill('fixture');
  const password = page.locator('input[name="password"]');
  await password.fill('Wrong!Password123'); await password.press('Enter');
  await expect(password).toHaveValue(''); await expect(page.getByRole('alert')).toContainText('identity.invalid_credentials');
  await password.fill('Correct!Password123'); await password.press('Enter');
  await expect(page).toHaveURL(/#\/$/); await expect(page.locator('.art-admin-shell')).toBeVisible(); expect(attempts).toBe(2);
});

test('离开登录页后迟到错误不影响恢复密码页', async ({ page }) => {
  let release;
  const waiting = new Promise(resolve => { release = resolve; });
  let submitted = false;
  await page.route('**/api/v1/auth/login', async route => { submitted = true; await waiting; await json(route, { status: 401, code: 'identity.invalid_credentials', title: 'Invalid' }, 401).catch(() => {}); });
  await page.goto('/#/login');
  await page.locator('input[name="username"]').fill('fixture'); await page.locator('input[name="password"]').fill('Pending!Password123');
  await page.getByRole('button', { name: '进入控制台', exact: true }).click();
  await expect.poll(() => submitted).toBe(true);
  await page.getByRole('link', { name: '忘记密码', exact: true }).click(); release();
  await expect(page.getByRole('heading', { name: '恢复密码', exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('跨标签刷新保持互斥且不回传完成广播，退出同步到另一标签', async ({ page, context }) => {
  let refreshes = 0;
  await context.route('**/api/v1/**', route => route.fulfill({ status: 404 }));
  await context.route('**/api/v1/auth/refresh', route => { refreshes++; return json(route, token); });
  await context.route('**/api/v1/me', route => json(route, user));
  await context.route('**/api/v1/navigation', route => json(route, navigation));
  // 既有 page 级兜底优先于 context，移除后由两标签共用同一受控 HTTP。
  await page.unrouteAll();
  const peer = await context.newPage();
  try {
    await page.goto('/#/oauth/callback?oauth=success'); await expect(page.locator('.art-admin-shell')).toBeVisible();
    await peer.goto('/#/oauth/callback?oauth=success'); await expect(peer.locator('.art-admin-shell')).toBeVisible();
    await expect(page.locator('.art-admin-shell')).toBeVisible();
    const before = refreshes;
    await page.evaluate(() => {
      const channel = new BroadcastChannel('fullnet.session.refresh');
      window.__sessionProbe = { channel, completions: 0 };
      channel.onmessage = event => { if (event.data?.type === 'refresh-complete') window.__sessionProbe.completions++; };
      channel.postMessage({ type: 'refresh-complete', success: true, sourceId: 'browser-probe' });
    });
    await expect.poll(() => refreshes).toBe(before + 2);
    await expect(page.locator('.art-admin-shell')).toBeVisible(); await expect(peer.locator('.art-admin-shell')).toBeVisible();
    expect(await page.evaluate(() => window.__sessionProbe.completions)).toBe(0);
    await page.evaluate(() => window.__sessionProbe.channel.postMessage({ type: 'session-cleared', sourceId: 'browser-probe' }));
    await expect(page.locator('input[name="password"]')).toBeVisible(); await expect(peer.locator('input[name="password"]')).toBeVisible();
    expect(refreshes).toBe(before + 2);
  } finally { await page.evaluate(() => window.__sessionProbe?.channel.close()); await peer.close(); }
});
