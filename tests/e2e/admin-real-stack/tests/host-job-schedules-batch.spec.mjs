import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
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

async function createPingDefinition(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const jobKey = `e2e.batch.${stamp}`.slice(0, 32);
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey,
      handlerKind: 'ping',
      args: null,
      displayName: `E2E Batch ${stamp}`,
      description: 'checklist 34',
      groupName: 'e2e',
      allowConcurrentExecutions: true
    },
    headers: authHeaders(token, origin)
  });
  expect(createResponse.status()).toBe(201);
  const definition = await createResponse.json();
  return { token, origin, definition, stamp };
}

async function createCronSchedule(request, token, origin, definitionId, cronExpression) {
  const response = await request.post(`${apiBaseUrl}/api/v1/jobs/host-schedules`, {
    data: {
      jobDefinitionId: definitionId,
      triggerKind: 'cron',
      cronExpression,
      timeZoneId: 'UTC',
      oneTimeAtUtc: null,
      misfirePolicy: 'fire_once',
      startTime: null,
      endTime: null,
      args: null
    },
    headers: authHeaders(token, origin)
  });
  expect(response.status()).toBe(201);
  return response.json();
}

async function reloadSchedule(request, token, origin, scheduleId) {
  const response = await request.get(`${apiBaseUrl}/api/v1/jobs/host-schedules/${scheduleId}`, {
    headers: { Authorization: `Bearer ${token}`, Origin: origin }
  });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

test('Host 管理员可批量暂停与恢复任务计划（清单 34）', async ({ request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '任务计划批量操作仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, definition } = await createPingDefinition(request, clientKind);
  const scheduleA = await createCronSchedule(
    request,
    token,
    origin,
    definition.id,
    '@hourly'
  );
  const scheduleB = await createCronSchedule(
    request,
    token,
    origin,
    definition.id,
    '@daily'
  );
  expect(scheduleA.isEnabled).toBe(true);
  expect(scheduleB.isEnabled).toBe(true);

  const pauseResponse = await request.post(`${apiBaseUrl}/api/v1/jobs/host-schedules/batch-pause`, {
    headers: authHeaders(token, origin),
    data: {
      items: [
        { scheduleId: scheduleA.id, version: scheduleA.version },
        { scheduleId: scheduleB.id, version: scheduleB.version }
      ]
    }
  });
  expect(pauseResponse.ok()).toBeTruthy();
  const paused = await pauseResponse.json();
  expect(paused.succeededCount).toBe(2);
  expect(paused.results.every(item => item.succeeded)).toBe(true);
  expect(paused.results.every(item => item.schedule?.isEnabled === false)).toBe(true);

  const reloadedA = await reloadSchedule(request, token, origin, scheduleA.id);
  const reloadedB = await reloadSchedule(request, token, origin, scheduleB.id);

  const resumeResponse = await request.post(`${apiBaseUrl}/api/v1/jobs/host-schedules/batch-resume`, {
    headers: authHeaders(token, origin),
    data: {
      items: [
        { scheduleId: reloadedA.id, version: reloadedA.version },
        { scheduleId: reloadedB.id, version: reloadedB.version }
      ]
    }
  });
  expect(resumeResponse.ok()).toBeTruthy();
  const resumed = await resumeResponse.json();
  expect(resumed.succeededCount).toBe(2);
  expect(resumed.results.every(item => item.schedule?.isEnabled === true)).toBe(true);
});

test('批量暂停对已暂停计划返回逐项失败（清单 34）', async ({ request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '任务计划批量操作仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, definition } = await createPingDefinition(request, clientKind);
  const enabled = await createCronSchedule(request, token, origin, definition.id, '@hourly');
  const pausedSeed = await createCronSchedule(request, token, origin, definition.id, '@daily');

  const singlePause = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-schedules/${pausedSeed.id}/pause`,
    {
      headers: authHeaders(token, origin),
      data: { version: pausedSeed.version }
    }
  );
  expect(singlePause.ok()).toBeTruthy();
  const alreadyPaused = await singlePause.json();

  const batchPause = await request.post(`${apiBaseUrl}/api/v1/jobs/host-schedules/batch-pause`, {
    headers: authHeaders(token, origin),
    data: {
      items: [
        { scheduleId: enabled.id, version: enabled.version },
        { scheduleId: alreadyPaused.id, version: alreadyPaused.version }
      ]
    }
  });
  expect(batchPause.ok()).toBeTruthy();
  const body = await batchPause.json();
  expect(body.succeededCount).toBe(1);
  expect(body.results[0].succeeded).toBe(true);
  expect(body.results[1].succeeded).toBe(false);
  expect(body.results[1].errorCode).toBeTruthy();
});

test('Vue 任务计划页可批量暂停选中项（清单 34）', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '任务计划批量 UI 仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, definition } = await createPingDefinition(request, clientKind);
  await createCronSchedule(request, token, origin, definition.id, '@hourly');

  await loginAsHostAdmin(page);
  await page.goto('/#/jobs/host-schedules');
  const view = page.locator('.host-job-schedules-view');
  await expect(view.getByRole('heading', { name: /任务计划/, level: 1 })).toBeVisible({
    timeout: 15_000
  });

  await view.getByTestId('host-job-schedules-filter-search').fill(definition.displayName);
  await view.getByTestId('host-job-schedules-apply-filters').click();

  const row = view.locator('.host-job-schedules-list li').filter({
    hasText: definition.displayName
  });
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('host-job-schedules-select').click();
  await view.getByTestId('host-job-schedules-batch-pause').click();
  await expect(page.getByText(/已暂停 .*\/.* 个计划/u)).toBeVisible({ timeout: 10_000 });
});

test('无暂停权限的 Host 用户不能批量暂停', async ({ request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '任务计划批量操作仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const { token, definition } = await createPingDefinition(request, clientKind);
  const schedule = await createCronSchedule(request, token, origin, definition.id, '@hourly');

  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'jobs.schedules.read'
    ]
  });
  const limitedToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );

  const denied = await request.post(`${apiBaseUrl}/api/v1/jobs/host-schedules/batch-pause`, {
    headers: {
      Authorization: `Bearer ${limitedToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    },
    data: {
      items: [{ scheduleId: schedule.id, version: schedule.version }]
    }
  });
  expect(denied.status()).toBe(403);
});
