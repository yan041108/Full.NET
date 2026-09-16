import { expect, test } from '@playwright/test';
import {
  ADMIN_OIDC_CENTER_CLIENT_ID,
  ensureAdminOidcCenterClient,
  findActiveOidcCenterSession,
  loginAdminViaOidcCenter,
  logoutAdminShell,
  revokeOnlineSessionById,
  waitForNotificationsRealtimeConnection
} from './support/admin-oidc-center-fixtures.mjs';
import {
  CENTER_COOKIE_NAME,
  expectRefreshTokenRejects,
  resolveApiBase
} from './support/identity-oidc-fixtures.mjs';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  expectVisibleCurrentContext,
  prepareHostUserCredentialsForOidc
} from './support/real-stack-auth.mjs';

const adminOrigin = 'http://localhost:25175';
let credentials = {
  username: process.env.FULLNET_E2E_USERNAME ?? 'admin',
  password: process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure'
};

test.describe.configure({ mode: 'serial' });

test.beforeEach(async ({ page }) => {
  await page.context().clearCookies();
  await page.addInitScript(() => {
    localStorage.clear();
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
    sessionStorage.clear();
  });
});

test.beforeAll(async ({ request }) => {
  credentials = await prepareHostUserCredentialsForOidc(
    request,
    'vue',
    credentials.username,
    credentials.password
  );
  await ensureAdminOidcCenterClient(request, adminOrigin);
});

test.describe('Vue admin oidc-center auth', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('OIDC 中心登录后展示动态导航', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await expect(page.getByRole('link', { name: /^工作台$/ })).toBeVisible({
      timeout: 15_000
    });
    const refreshCredential = await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'));
    expect(refreshCredential).toContain(ADMIN_OIDC_CENTER_CLIENT_ID);
  });

  test('页面刷新后仍保持认证会话', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await page.reload();
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
      timeout: 30_000
    });
  });

  test('OIDC 中心 Host 上下文工作台探针可连接真实 /api/v1/me', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await expectVisibleCurrentContext(page, 'Full.NET Host');
    await expect(page.getByTestId('load-current-user')).toBeVisible({ timeout: 15_000 });
    await page.getByTestId('load-current-user').click();
    await expect(page.getByText('已连接：系统管理员', { exact: true })).toBeVisible({
      timeout: 15_000
    });
  });

  test('OIDC 中心 Host 上下文可打开工作流待办页', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /我的待办/, '工作流');
    await expect(page.getByRole('heading', { name: '我的工作流待办', exact: true }))
      .toBeVisible({ timeout: 15_000 });
  });

  test('OIDC 中心登录后可切换 Development 租户并返回 Host', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh')))
      .toContain(ADMIN_OIDC_CENTER_CLIENT_ID);

    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh')))
      .toContain(ADMIN_OIDC_CENTER_CLIENT_ID);
  });

  test('OIDC 中心切租户后可加载租户范围内受保护页面', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    await clickMainNavLink(page, /机构管理/);

    await expect(page.getByRole('heading', { name: '机构管理', exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: '机构编码' }).first()).toBeVisible();
  });

  test('OIDC 中心切租户后工作台探针可连接真实 /api/v1/me', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expect(page.getByTestId('load-current-user')).toBeVisible({ timeout: 15_000 });
    await page.getByTestId('load-current-user').click();
    await expect(page.getByText('已连接：系统管理员', { exact: true })).toBeVisible({
      timeout: 15_000
    });
  });

  test('应用退出后清理本地凭据、中心 Cookie 并拒绝 refresh token', async ({ page, request, context }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const refreshCredentialRaw = await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'));
    expect(refreshCredentialRaw).toBeTruthy();
    const refreshCredential = JSON.parse(refreshCredentialRaw);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'))).toBeNull();
    const centerCookie = (await context.cookies(resolveApiBase()))
      .find(cookie => cookie.name === CENTER_COOKIE_NAME);
    expect(centerCookie).toBeUndefined();

    await expectRefreshTokenRejects(request, {
      apiBase: resolveApiBase(),
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID,
      refreshToken: refreshCredential.refreshToken
    });
  });

  test('管理员强制下线后客户端收到实时通知并回到登录页', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await waitForNotificationsRealtimeConnection(page);
    const session = await findActiveOidcCenterSession(request, {
      adminOrigin,
      username: credentials.username,
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID
    });
    await revokeOnlineSessionById(request, {
      adminOrigin,
      sessionId: session.id
    });
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'))).toBeNull();
  });
});