import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

test('Host 管理员可创建非并发任务、查看执行历史与 Cron 预览；重叠开关在创建表单可见', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'Jobs B1 收口仅验收 Vue');
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const jobKey = `e2e.ping.${stamp}`.slice(0, 32);

  const createResponse = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey,
      handlerKind: 'ping',
      args: null,
      displayName: `E2E Ping ${stamp}`,
      description: 'B1 real-stack overlap',
      groupName: 'e2e',
      allowConcurrentExecutions: false
    },
    headers: authHeaders(token, origin)
  });
  expect(createResponse.status()).toBe(201);
  const definition = await createResponse.json();
  expect(definition.allowConcurrentExecutions).toBe(false);

  const triggerResponse = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
    { data: {}, headers: authHeaders(token, origin) }
  );
  expect(triggerResponse.status()).toBe(201);
  const execution = await triggerResponse.json();
  expect(typeof execution.id).toBe('string');

  const listExecutions = await request.get(
    `${apiBaseUrl}/api/v1/jobs/host-executions?page=1&pageSize=50&jobDefinitionId=${definition.id}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(listExecutions.status()).toBe(200);
  const executions = await listExecutions.json();
  expect(executions.items.some(item => item.id === execution.id)).toBeTruthy();

  const previewResponse = await request.get(
    `${apiBaseUrl}/api/v1/jobs/host-schedules/cron-preview?cronExpression=${encodeURIComponent('@hourly')}&timeZoneId=UTC&occurrenceCount=3`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(previewResponse.status()).toBe(200);
  expect((await previewResponse.json()).nextOccurrencesUtc.length).toBeGreaterThan(0);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /任务定义/, '任务');
  const jobsView = page.locator('.host-jobs-view');
  await expect(jobsView.getByRole('heading', { name: /任务定义/, level: 1 })).toBeVisible();
  await jobsView.getByTestId('host-jobs-action-create').click();
  await expect(page.getByTestId('host-jobs-editor-form')).toBeVisible();
  await expect(page.getByTestId('host-jobs-allow-concurrent')).toBeAttached();
  await page.keyboard.press('Escape');

  await page.goto('/#/jobs/host-executions');
  await expect(page.locator('.host-job-executions-view').getByRole('heading', {
    name: '执行历史',
    exact: true
  })).toBeVisible();

  await page.goto('/#/jobs/host-schedules');
  const schedulesView = page.locator('.host-job-schedules-view');
  await expect(schedulesView.getByRole('heading', { name: /任务计划/, level: 1 })).toBeVisible();
  await schedulesView.getByTestId('host-job-schedules-cron').fill('@hourly');
  await expect(schedulesView.getByTestId('host-job-schedules-cron-preview')).toBeVisible({
    timeout: 15_000
  });
});

test('Host 管理员可打开集群健康页；受限账号 API 403 且导航不可见', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'Jobs health 仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);

  await loginAsHostAdmin(page);
  await page.goto('/#/jobs/host-health');
  await expect(page.locator('.host-job-health-view').getByRole('heading', {
    name: '集群健康',
    exact: true
  })).toBeVisible();

  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'jobs.definitions.read'
    ]
  });
  const limitedToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const denied = await request.get(`${apiBaseUrl}/api/v1/jobs/host-health`, {
    headers: { Authorization: `Bearer ${limitedToken}`, Origin: origin }
  });
  expect(denied.status()).toBe(403);
  expect((await denied.json()).code).toBe('authorization.permission_denied');

  await loginAsHostUser(page, limited.username, limited.password);
  await expect(
    page.getByRole('navigation', { name: '主导航' }).first().getByRole('link', { name: /集群健康/ })
  ).toHaveCount(0);
  await page.goto(statusPath(clientKind, 'jobs/host-health'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
});

test('HTTP 任务拒绝危险 Header；私网 URL 触发后执行失败；无 create 权限时创建按钮不可见', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'HTTP Job 仅验收 Vue');
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);

  const dangerous = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey: `e2e.bad.${stamp}`.slice(0, 32),
      handlerKind: 'http',
      args: {
        url: 'https://example.com/',
        method: 'GET',
        headers: { Authorization: 'Bearer plaintext' }
      },
      displayName: `E2E Bad Header ${stamp}`,
      description: null,
      groupName: 'e2e',
      allowConcurrentExecutions: false
    },
    headers: authHeaders(token, origin)
  });
  expect(dangerous.status()).toBe(400);

  const createHttp = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey: `e2e.http.${stamp}`.slice(0, 32),
      handlerKind: 'http',
      args: { url: 'http://127.0.0.1/', method: 'GET' },
      displayName: `E2E SSRF ${stamp}`,
      description: null,
      groupName: 'e2e',
      allowConcurrentExecutions: false
    },
    headers: authHeaders(token, origin)
  });
  expect([201, 400]).toContain(createHttp.status());
  if (createHttp.status() === 201) {
    const httpJob = await createHttp.json();
    const trigger = await request.post(
      `${apiBaseUrl}/api/v1/jobs/host-definitions/${httpJob.id}/trigger`,
      { data: {}, headers: authHeaders(token, origin) }
    );
    expect(trigger.status()).toBe(201);
    expect((await trigger.json()).status).toBe('failed');
  }

  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'jobs.definitions.read'
    ]
  });
  await loginAsHostUser(page, limited.username, limited.password);
  await page.goto('/#/jobs/host-definitions');
  const jobsView = page.locator('.host-jobs-view');
  await expect(jobsView.getByRole('heading', { name: /任务定义/, level: 1 })).toBeVisible();
  await expect(jobsView.getByTestId('host-jobs-action-create')).toHaveCount(0);

  const viewerToken = await loginAccessToken(request, clientKind);
  const viewerCreate = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey: `e2e.deny.${stamp}`.slice(0, 32),
      handlerKind: 'ping',
      args: null,
      displayName: 'denied',
      description: null,
      groupName: null,
      allowConcurrentExecutions: false
    },
    headers: authHeaders(viewerToken, origin)
  });
  expect(viewerCreate.status()).toBe(403);
  expect((await viewerCreate.json()).code).toBe('authorization.permission_denied');
});
