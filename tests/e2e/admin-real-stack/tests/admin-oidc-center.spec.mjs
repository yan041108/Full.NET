import { expect, test } from '@playwright/test';
import {
  ADMIN_OIDC_CENTER_CLIENT_ID,
  ADMIN_OIDC_CENTER_ORIGIN,
  captureOidcAccessTokenFromOverviewProbe,
  createE2eAiAgentModelConfig,
  createE2eHostPingJobDefinition,
  createOidcCenterQueuedAgentRun,
  ensureAdminOidcCenterClient,
  expectRevokedOidcCenterAgentRunAccessRejected,
  expectOidcApiGetStatus,
  expectOidcApiPostStatus,
  expectOidcCenterLocalCredentialsCleared,
  expectOidcCenterTokensRejected,
  expectProtectedRoutesRedirectToOidcLogin,
  expectStaleOidcRefreshCannotRestoreSession,
  readOidcRefreshCredentialFromPage,
  loginAdminViaOidcCenter,
  logoutAdminShell,
  revokeCurrentOidcCenterSession
} from './support/admin-oidc-center-fixtures.mjs';
import { resolveApiBase } from './support/identity-oidc-fixtures.mjs';
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

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      200
    );
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

  test('OIDC 中心 Host 上下文可打开 Agent 运行页', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /Agent 运行/);
    await expect(page.getByRole('heading', { name: 'Agent 运行', exact: true }))
      .toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('ai-agent-runs-load')).toBeVisible();
    await expect(page.getByTestId('ai-agent-runs-create')).toBeVisible();
  });

  test('OIDC 中心 Host 上下文可通过 Agent 运行页 UI 创建排队运行', async ({ page, request }) => {
    test.setTimeout(90_000);
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC UI Agent ${stamp}`
    });
    const prompt = `oidc-center agent runs ui create ${stamp}`;

    await loginAdminViaOidcCenter(page, credentials);
    await clickMainNavLink(page, /Agent 运行/);
    await expect(page.getByRole('heading', { name: 'Agent 运行', exact: true }))
      .toBeVisible({ timeout: 15_000 });

    const createForm = page.locator('.ai-agent-runs-create-form');
    await createForm.getByPlaceholder('模型配置 ID').fill(model.id);
    await createForm.getByPlaceholder('提示词').fill(prompt);

    const createResponse = page.waitForResponse(response =>
      response.url().includes('/api/v1/ai/agent/runs')
      && response.request().method() === 'POST'
    );
    await page.getByTestId('ai-agent-runs-create').click();
    const response = await createResponse;
    expect(response.status()).toBe(202);
    const created = await response.json();
    expect(created.runId).toBeTruthy();

    await expect(page.getByText('运行摘要')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('.ai-agent-runs-view').getByText('queued', { exact: true }))
      .toBeVisible({ timeout: 30_000 });
  });

  test('OIDC 中心 Host 上下文 access token 可访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      200
    );
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心 Host 上下文 access token 可创建并读取排队 Agent Run', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Agent Run ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, accessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center queued agent run positive probe'
    });

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );
    const body = await response.json();
    expect(body.statusKey).toBe('queued');
  });

  test('OIDC 中心退出后 access token 无法访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      200
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      401
    );
  });

  test('OIDC 中心退出后 access token 无法访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      200
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      401
    );
  });

  test('OIDC 中心退出后 access token 无法访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      200
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      401
    );
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

    const triggerResponse = await expectOidcApiPostStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      201
    );
    const execution = await triggerResponse.json();

    const beforeLogout = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      200
    );
    expect((await beforeLogout.json()).items?.some(item => item.id === execution.id)).toBe(true);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      401
    );
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

    await expectOidcApiPostStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      201
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectOidcApiPostStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      401
    );
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

    await expectOidcApiPostStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      201
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectOidcApiPostStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      401
    );
  });

  test('OIDC 中心退出后已失效 access token 无法读取、取消、恢复或创建 Agent Run', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Logout Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, oidcAccessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center post-logout agent run rejection'
    });

    await expectOidcApiGetStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectRevokedOidcCenterAgentRunAccessRejected(request, oidcAccessToken, {
      apiBase,
      runId: run.runId,
      modelConfigId: model.id
    });
  });

  test('OIDC 中心强制下线后已失效 access token 无法读取、取消、恢复或创建 Agent Run', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Revoke Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, oidcAccessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center post-revoke agent run rejection'
    });

    await expectOidcApiGetStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectRevokedOidcCenterAgentRunAccessRejected(request, oidcAccessToken, {
      apiBase,
      runId: run.runId,
      modelConfigId: model.id
    });
  });

  test('OIDC 中心强制下线后 access token 无法访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      200
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      401
    );
  });

  test('OIDC 中心强制下线后 access token 无法访问工作流待办 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      200
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      401
    );
  });

  test('OIDC 中心强制下线后 access token 无法访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      200
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      401
    );
  });

  test('OIDC 中心强制下线后 access token 无法访问后台任务执行历史 API', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const jobKey = `e2e.oidc.rev.exec.${stamp}`.slice(0, 32);
    const definition = await createE2eHostPingJobDefinition(request, {
      jobKey,
      displayName: `E2E OIDC Revoke Exec ${stamp}`,
      description: 'oidc-center post-revoke executions list rejection'
    });

    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const triggerResponse = await expectOidcApiPostStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
      201
    );
    const execution = await triggerResponse.json();

    const beforeRevoke = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      200
    );
    expect((await beforeRevoke.json()).items?.some(item => item.id === execution.id)).toBe(true);

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
      401
    );
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

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      200
    );
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

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      200
    );
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心切租户并返回 Host 后 access token 仍可访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      200
    );
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心切租户并返回 Host 后 access token 仍可创建并读取排队 Agent Run', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Host Return Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, accessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center host return agent run probe'
    });

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );
    expect((await response.json()).statusKey).toBe('queued');
  });

  test('OIDC 中心切租户并返回 Host 后可通过 Agent 运行页 UI 创建排队运行', async ({ page, request }) => {
    test.setTimeout(90_000);
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Host Return UI Agent ${stamp}`
    });
    const prompt = `oidc-center host return agent runs ui create ${stamp}`;

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await clickMainNavLink(page, /租户上下文/);
    await page.getByRole('button', { name: '返回 Host' }).click();
    await expectVisibleCurrentContext(page, 'Full.NET Host');
    await clickMainNavLink(page, /Agent 运行/);
    await expect(page.getByRole('heading', { name: 'Agent 运行', exact: true }))
      .toBeVisible({ timeout: 15_000 });

    const createForm = page.locator('.ai-agent-runs-create-form');
    await createForm.getByPlaceholder('模型配置 ID').fill(model.id);
    await createForm.getByPlaceholder('提示词').fill(prompt);

    const createResponse = page.waitForResponse(response =>
      response.url().includes('/api/v1/ai/agent/runs')
      && response.request().method() === 'POST'
    );
    await page.getByTestId('ai-agent-runs-create').click();
    const response = await createResponse;
    expect(response.status()).toBe(202);
    const created = await response.json();
    expect(created.runId).toBeTruthy();

    await expect(page.getByText('运行摘要')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('.ai-agent-runs-view').getByText('queued', { exact: true }))
      .toBeVisible({ timeout: 30_000 });
  });

  test('OIDC 中心切租户后可加载租户范围内受保护页面', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    await clickMainNavLink(page, /机构管理/);

    await expect(page.getByRole('heading', { name: '机构管理', exact: true })).toBeVisible();
    await expect(page.getByRole('columnheader', { name: '机构编码' }).first()).toBeVisible();
  });

  test('OIDC 中心切租户后可打开 Agent 运行页', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    await clickMainNavLink(page, /Agent 运行/);
    await expect(page.getByRole('heading', { name: 'Agent 运行', exact: true }))
      .toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('ai-agent-runs-load')).toBeVisible();
    await expect(page.getByTestId('ai-agent-runs-create')).toBeVisible();
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

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/workflow/todos/mine`,
      200
    );
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心切租户后 access token 仍可访问 /api/v1/ai/agent-tools', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/ai/agent-tools`,
      200
    );
    expect(Array.isArray(await response.json())).toBe(true);
  });

  test('OIDC 中心切租户后 access token 仍可访问后台任务定义 API', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${resolveApiBase()}/api/v1/jobs/host-definitions?page=1&pageSize=20`,
      200
    );
    const body = await response.json();
    expect(Array.isArray(body.items)).toBe(true);
  });

  test('OIDC 中心切租户后 access token 仍可创建并读取排队 Agent Run', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Tenant Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, accessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center tenant context agent run probe'
    });

    const response = await expectOidcApiGetStatus(
      request,
      accessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );
    expect((await response.json()).statusKey).toBe('queued');
  });

  test('OIDC 中心切租户后可通过 Agent 运行页 UI 创建排队运行', async ({ page, request }) => {
    test.setTimeout(90_000);
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Tenant UI Agent ${stamp}`
    });
    const prompt = `oidc-center tenant agent runs ui create ${stamp}`;

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    await clickMainNavLink(page, /Agent 运行/);
    await expect(page.getByRole('heading', { name: 'Agent 运行', exact: true }))
      .toBeVisible({ timeout: 15_000 });

    const createForm = page.locator('.ai-agent-runs-create-form');
    await createForm.getByPlaceholder('模型配置 ID').fill(model.id);
    await createForm.getByPlaceholder('提示词').fill(prompt);

    const createResponse = page.waitForResponse(response =>
      response.url().includes('/api/v1/ai/agent/runs')
      && response.request().method() === 'POST'
    );
    await page.getByTestId('ai-agent-runs-create').click();
    const response = await createResponse;
    expect(response.status()).toBe(202);
    const created = await response.json();
    expect(created.runId).toBeTruthy();

    await expect(page.getByText('运行摘要')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('.ai-agent-runs-view').getByText('queued', { exact: true }))
      .toBeVisible({ timeout: 30_000 });
  });

  test('OIDC 中心切租户后创建的排队 Agent Run 在应用退出后绑定失效', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Tenant Logout Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, oidcAccessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center tenant post-logout agent run rejection'
    });

    await expectOidcApiGetStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectRevokedOidcCenterAgentRunAccessRejected(request, oidcAccessToken, {
      apiBase,
      runId: run.runId,
      modelConfigId: model.id
    });
  });

  test('OIDC 中心切租户后创建的排队 Agent Run 在强制下线后绑定失效', async ({ page, request }) => {
    test.setTimeout(90_000);
    const apiBase = resolveApiBase();
    const stamp = Date.now().toString(36);
    const model = await createE2eAiAgentModelConfig(request, {
      displayName: `E2E OIDC Tenant Revoke Agent ${stamp}`
    });

    await loginAdminViaOidcCenter(page, credentials);
    await enterDevelopmentTenant(page);
    await expectVisibleCurrentContext(page, 'Full.NET Local');
    const oidcAccessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const run = await createOidcCenterQueuedAgentRun(request, oidcAccessToken, {
      modelConfigId: model.id,
      prompt: 'oidc-center tenant post-revoke agent run rejection'
    });

    await expectOidcApiGetStatus(
      request,
      oidcAccessToken,
      `${apiBase}/api/v1/ai/agent/runs/${run.runId}`,
      200
    );

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectRevokedOidcCenterAgentRunAccessRejected(request, oidcAccessToken, {
      apiBase,
      runId: run.runId,
      modelConfigId: model.id
    });
  });

  test('OIDC 中心退出后无法直接访问受保护路由', async ({ page }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });

    await expectProtectedRoutesRedirectToOidcLogin(page);
  });

  test('OIDC 中心强制下线后无法直接访问受保护路由', async ({ page, request }) => {
    await loginAdminViaOidcCenter(page, credentials);
    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });

    await expectProtectedRoutesRedirectToOidcLogin(page);
  });

  test('应用退出后清理本地凭据、中心 Cookie 并拒绝 refresh token', async ({ page, request, context }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const { refreshCredential } = await readOidcRefreshCredentialFromPage(page);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
    await expectOidcCenterLocalCredentialsCleared(page, context);
    await expectOidcCenterTokensRejected(request, {
      accessToken,
      refreshToken: refreshCredential.refreshToken
    });
  });

  test('OIDC 中心应用退出后写回 refresh 凭据仍无法恢复会话', async ({ page, request, context }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const { refreshCredentialRaw, refreshCredential } = await readOidcRefreshCredentialFromPage(page);

    await logoutAdminShell(page);
    await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
    await expectOidcCenterLocalCredentialsCleared(page, context);

    await expectStaleOidcRefreshCannotRestoreSession(page, request, refreshCredentialRaw, {
      accessToken,
      refreshToken: refreshCredential.refreshToken
    });
  });

  test('OIDC 中心强制下线后清理本地凭据、中心 Cookie 并拒绝 refresh token', async ({
    page,
    request,
    context
  }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);

    const { refreshCredential } = await readOidcRefreshCredentialFromPage(page);

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });
    await expectOidcCenterLocalCredentialsCleared(page, context);
    await expectOidcCenterTokensRejected(request, {
      accessToken,
      refreshToken: refreshCredential.refreshToken
    });
  });

  test('管理员强制下线后客户端收到实时通知并回到登录页', async ({ page, request, context }) => {
    await loginAdminViaOidcCenter(page, credentials);
    const accessToken = await captureOidcAccessTokenFromOverviewProbe(page);
    const { refreshCredentialRaw, refreshCredential } = await readOidcRefreshCredentialFromPage(page);

    await revokeCurrentOidcCenterSession(page, request, { username: credentials.username });
    await expectOidcCenterLocalCredentialsCleared(page, context);

    await expectStaleOidcRefreshCannotRestoreSession(page, request, refreshCredentialRaw, {
      accessToken,
      refreshToken: refreshCredential.refreshToken
    });
  });
});