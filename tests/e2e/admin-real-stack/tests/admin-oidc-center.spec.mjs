import { expect, test } from '@playwright/test';
import {
  ADMIN_OIDC_CENTER_CLIENT_ID,
  ADMIN_OIDC_CENTER_ORIGIN,
  buildOidcCenterApiHeaders,
  buildOidcCenterJsonHeaders,
  captureOidcAccessTokenFromOverviewProbe,
  createE2eHostPingJobDefinition,
  ensureAdminOidcCenterClient,
  findActiveOidcCenterSession,
  loginAdminViaOidcCenter,
  logoutAdminShell,
  revokeOnlineSessionById,
  waitForNotificationsRealtimeConnection
} from './support/admin-oidc-center-fixtures.mjs';
import {
  CENTER_COOKIE_NAME,
  expectMeEndpointRejectsToken,
  expectRefreshTokenRejects,
  resolveApiBase
} from './support/identity-oidc-fixtures.mjs';
import {
  clickMainNavLink,
  enterDevelopmentTenant,
  expectVisibleCurrentContext,
  loginHostAdminAccessToken,
  prepareHostUserCredentialsForOidc
} from './support/real-stack-auth.mjs';
import {
  getInstance,
  openTodoAndAct,
  publishApprovalAssets,
  startInstance
} from './support/workflow-approval-fixtures.mjs';

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
  await ensureAdminOidcCenterClient(request, ADMIN_OIDC_CENTER_ORIGIN);
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

  test('OIDC 中心 Host 上下文 access token 可访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心 Host 上下文可打开 Agent 工具页', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /Agent 工具/);
    await expect(page.getByRole('heading', { name: 'Agent 工具', exact: true }))
      .toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('tab', { name: '静态目录' })).toBeVisible();
  });

  test('OIDC 中心 Host 上下文 access token 可访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/ai/agent-tools`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心退出后 access token 无法访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    const response = await request.get(`${resolveApiBase()}/api/v1/ai/agent-tools`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(401);
  });

  test('OIDC 中心退出后 access token 无法访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const beforeLogout = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(beforeLogout.status()).toBe(200);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    const afterLogout = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(afterLogout.status()).toBe(401);
  });

  test('OIDC 中心退出后 access token 无法访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const beforeLogout = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(beforeLogout.status()).toBe(200);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    const afterLogout = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(afterLogout.status()).toBe(401);
  });

  test('OIDC 中心退出后 access token 无法访问后台任务执行历史 API', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const jobKey = `e2e.oidc.exec.${stamp}`.slice(0, 32);
    const definition = await createE2eHostPingJobDefinition(request, {
      jobKey,
      displayName: `E2E OIDC Logout Exec ${stamp}`,
      description: 'oidc-center post-logout executions list rejection'
    });

    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const triggerResponse = await request.post(
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      {
        headers: buildOidcCenterJsonHeaders(accessToken),
        data: {}
      }
    );
    expect(triggerResponse.status()).toBe(201);
    const execution = await triggerResponse.json();

    const beforeLogout = await request.get(
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      { headers: buildOidcCenterApiHeaders(accessToken) }
    );
    expect(beforeLogout.status()).toBe(200);
    expect((await beforeLogout.json()).items?.some(item => item.id === execution.id)).toBe(true);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    const afterLogout = await request.get(
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      { headers: buildOidcCenterApiHeaders(accessToken) }
    );
    expect(afterLogout.status()).toBe(401);
  });

  test('OIDC 中心 Host 上下文可完成工作流待办同意', async ({ page, request }) => {
    test.setTimeout(120_000);
    const accessToken = await loginHostAdminAccessToken(request, 'vue');
    const assets = await publishApprovalAssets(request, 'vue', accessToken);
    const instance = await startInstance(
      request,
      'vue',
      accessToken,
      assets.versionId,
      'oidc-center approved'
    );

    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /我的待办/, '工作流');
    await openTodoAndAct(page, instance.id, 'approved', 'approve');
    await expect.poll(async () =>
      (await getInstance(request, 'vue', accessToken, instance.id)).statusKey
    ).toBe('completed');
  });

  test('OIDC 中心 Host 上下文可完成工作流待办驳回', async ({ page, request }) => {
    test.setTimeout(120_000);
    const accessToken = await loginHostAdminAccessToken(request, 'vue');
    const assets = await publishApprovalAssets(request, 'vue', accessToken);
    const instance = await startInstance(
      request,
      'vue',
      accessToken,
      assets.versionId,
      'oidc-center rejected'
    );

    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /我的待办/, '工作流');
    await openTodoAndAct(page, instance.id, 'rejected', 'reject');
    await expect.poll(async () =>
      (await getInstance(request, 'vue', accessToken, instance.id)).statusKey
    ).toBe('rejected');
  });

  test('OIDC 中心 Host 上下文可打开后台任务定义页', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /任务定义/, '任务');
    const jobsView = page.locator('.host-jobs-view');
    await expect(jobsView.getByRole('heading', { name: /任务定义/, level: 1 }))
      .toBeVisible({ timeout: 15_000 });
  });

  test('OIDC 中心 Host 上下文可触发后台任务', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const setupOrigin = 'http://localhost:25173';
    const accessToken = await loginHostAdminAccessToken(request, 'vue');
    const stamp = Date.now().toString(36);
    const jobKey = `e2e.oidc.${stamp}`.slice(0, 32);
    const displayName = `E2E OIDC Ping ${stamp}`;
    const definition = await createE2eHostPingJobDefinition(request, {
      jobKey,
      displayName,
      description: 'oidc-center trigger probe'
    });

    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /任务定义/, '任务');
    const jobsView = page.locator('.host-jobs-view');
    const row = jobsView.getByRole('row').filter({ hasText: displayName });
    await expect(row).toBeVisible({ timeout: 15_000 });

    const triggerResponse = page.waitForResponse(response =>
      response.url().includes(`/api/v1/jobs/host-definitions/${definition.id}/trigger`)
      && response.request().method() === 'POST'
    );
    await row.getByTestId('host-jobs-action-trigger').click();
    const response = await triggerResponse;
    expect(response.status()).toBe(201);
    const execution = await response.json();
    expect(typeof execution.id).toBe('string');

    await expect.poll(async () => {
      const listResponse = await request.get(
        `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
        {
          headers: {
            authorization: `Bearer ${accessToken}`,
            origin: setupOrigin
          }
        }
      );
      if (!listResponse.ok()) {
        return false;
      }
      const body = await listResponse.json();
      return (body.items ?? []).some(item => item.id === execution.id);
    }).toBe(true);
  });

  test('OIDC 中心退出后已失效 access token 无法触发后台任务', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const jobKey = `e2e.oidc.out.${stamp}`.slice(0, 32);
    const definition = await createE2eHostPingJobDefinition(request, {
      jobKey,
      displayName: `E2E OIDC Logout Job ${stamp}`,
      description: 'oidc-center post-logout job trigger rejection'
    });

    await loginAdminViaOidcCenter(page, credentials);
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const triggerBeforeLogout = await request.post(
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      {
        headers: buildOidcCenterJsonHeaders(oidcAccessToken),
        data: {}
      }
    );
    expect(triggerBeforeLogout.status()).toBe(201);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    const triggerAfterLogout = await request.post(
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      {
        headers: buildOidcCenterJsonHeaders(oidcAccessToken),
        data: {}
      }
    );
    expect(triggerAfterLogout.status()).toBe(401);
  });

  test('OIDC 中心强制下线后已失效 access token 无法触发后台任务', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const jobKey = `e2e.oidc.rev.${stamp}`.slice(0, 32);
    const definition = await createE2eHostPingJobDefinition(request, {
      jobKey,
      displayName: `E2E OIDC Revoke Job ${stamp}`,
      description: 'oidc-center post-revoke job trigger rejection'
    });

    await loginAdminViaOidcCenter(page, credentials);
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const triggerBeforeRevoke = await request.post(
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      {
        headers: buildOidcCenterJsonHeaders(oidcAccessToken),
        data: {}
      }
    );
    expect(triggerBeforeRevoke.status()).toBe(201);

    await waitForNotificationsRealtimeConnection(page);
    const session = await findActiveOidcCenterSession(request, {
      adminOrigin: ADMIN_OIDC_CENTER_ORIGIN,
      username: credentials.username,
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID
    });
    await revokeOnlineSessionById(request, {
      adminOrigin: ADMIN_OIDC_CENTER_ORIGIN,
      sessionId: session.id
    });
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });

    const triggerAfterRevoke = await request.post(
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      {
        headers: buildOidcCenterJsonHeaders(oidcAccessToken),
        data: {}
      }
    );
    expect(triggerAfterRevoke.status()).toBe(401);
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

  test('OIDC 中心切租户并返回 Host 后 access token 仍可访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心切租户并返回 Host 后 access token 仍可访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/ai/agent-tools`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心切租户并返回 Host 后 access token 仍可访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
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

  test('OIDC 中心切租户后 access token 仍可访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心切租户后 access token 仍可访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(`${resolveApiBase()}/api/v1/ai/agent-tools`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(response.status()).toBe(200);
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心切租户后 access token 仍可访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心退出后无法直接访问受保护路由', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await page.goto('/#/ai/agent-tools');
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('heading', { name: 'Agent 工具', exact: true })).toHaveCount(0);
  });

  test('应用退出后清理本地凭据、中心 Cookie 并拒绝 refresh token', async ({ page, request, context }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

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
    await expectMeEndpointRejectsToken(request, accessToken);
  });

  test('管理员强制下线后客户端收到实时通知并回到登录页', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const refreshCredentialRaw = await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'));
    expect(refreshCredentialRaw).toBeTruthy();
    const refreshCredential = JSON.parse(refreshCredentialRaw);

    await waitForNotificationsRealtimeConnection(page);
    const session = await findActiveOidcCenterSession(request, {
      adminOrigin: ADMIN_OIDC_CENTER_ORIGIN,
      username: credentials.username,
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID
    });
    await revokeOnlineSessionById(request, {
      adminOrigin: ADMIN_OIDC_CENTER_ORIGIN,
      sessionId: session.id
    });
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'))).toBeNull();

    await page.evaluate(credential => {
      sessionStorage.setItem('fullnet.admin.oidc.refresh', credential);
    }, refreshCredentialRaw);
    await page.reload();
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByRole('navigation', { name: '主导航' })).toHaveCount(0);

    await expectRefreshTokenRejects(request, {
      apiBase: resolveApiBase(),
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID,
      refreshToken: refreshCredential.refreshToken
    });
    await expectMeEndpointRejectsToken(request, accessToken);

    const toolsResponse = await request.get(`${resolveApiBase()}/api/v1/ai/agent-tools`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(toolsResponse.status()).toBe(401);

    const todosResponse = await request.get(`${resolveApiBase()}/api/v1/workflow/todos/mine`, {
      headers: buildOidcCenterApiHeaders(accessToken)
    });
    expect(todosResponse.status()).toBe(401);

    const jobsResponse = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(jobsResponse.status()).toBe(401);

    const executionsResponse = await request.get(
      `${resolveApiBase()}/api/v1/jobs/host-executions?page=1&pageSize=20`,
      {
        headers: buildOidcCenterApiHeaders(accessToken)
      }
    );
    expect(executionsResponse.status()).toBe(401);
  });
});