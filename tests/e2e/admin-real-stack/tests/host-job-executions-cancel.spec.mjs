import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
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

async function createPingDefinition(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const stamp = Date.now().toString(36);
  const jobKey = `e2e.cancel.${stamp}`.slice(0, 32);
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/jobs/host-definitions`, {
    data: {
      jobKey,
      handlerKind: 'ping',
      args: null,
      displayName: `E2E Cancel ${stamp}`,
      description: 'checklist 33',
      groupName: 'e2e',
      allowConcurrentExecutions: true
    },
    headers: authHeaders(token, origin)
  });
  expect(createResponse.status()).toBe(201);
  const definition = await createResponse.json();
  return { token, origin, definition };
}

async function readExecution(request, token, origin, executionId) {
  const response = await request.get(
    `${apiBaseUrl}/api/v1/jobs/host-executions/${executionId}`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(response.ok()).toBeTruthy();
  return response.json();
}

test('Host 管理员可请求取消待处理/运行中执行，终态执行返回冲突（清单 33）', async ({
  request
}, testInfo) => {
  test.setTimeout(60_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, definition } = await createPingDefinition(request, clientKind);

  const triggerResponse = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
    { data: {}, headers: authHeaders(token, origin) }
  );
  expect(triggerResponse.status()).toBe(201);
  const triggered = await triggerResponse.json();

  const cancelImmediate = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-executions/${triggered.id}/cancel`,
    { headers: authHeaders(token, origin) }
  );

  if (cancelImmediate.ok()) {
    const body = await cancelImmediate.json();
    expect(['cancelled', 'cancelling']).toContain(body.status);
    return;
  }

  expect(cancelImmediate.status()).toBe(409);

  const terminal = await readExecution(request, token, origin, triggered.id);
  expect(['succeeded', 'failed', 'cancelled']).toContain(terminal.status);

  const cancelAgain = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-executions/${triggered.id}/cancel`,
    { headers: authHeaders(token, origin) }
  );
  expect(cancelAgain.status()).toBe(409);
  const problem = await cancelAgain.json();
  expect(problem.code).toBeTruthy();
});

test('Vue 执行历史页可对可取消执行提交取消（清单 33）', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '执行历史取消仅验收 Vue');
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, definition } = await createPingDefinition(request, clientKind);

  const triggerResponse = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
    { data: {}, headers: authHeaders(token, origin) }
  );
  expect(triggerResponse.status()).toBe(201);
  const execution = await triggerResponse.json();

  await loginAsHostAdmin(page);
  await page.goto('/#/jobs/host-executions');
  const view = page.locator('.host-job-executions-view');
  await expect(view.getByRole('heading', { name: '执行历史', exact: true })).toBeVisible({
    timeout: 15_000
  });

  await view.getByTestId('host-job-executions-filter-definition').click();
  await page.getByRole('option', { name: definition.displayName, exact: true }).click();
  await view.getByTestId('host-job-executions-search').click();

  const cancelButton = view.getByTestId('host-job-executions-cancel').first();
  if (await cancelButton.count()) {
    await cancelButton.click();
    await expect(page.getByText('已提交取消请求', { exact: false })).toBeVisible({
      timeout: 10_000
    });
  } else {
    await expect(view.getByText(/不支持|取消中/u).first()).toBeVisible({ timeout: 10_000 });
  }
});

test('无取消权限的 Host 用户调用取消 API 被拒绝', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const { token, definition } = await createPingDefinition(request, clientKind);
  const triggerResponse = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-definitions/${definition.id}/trigger`,
    { data: {}, headers: authHeaders(token, origin) }
  );
  const execution = await triggerResponse.json();

  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'jobs.executions.read'
    ]
  });
  const limitedToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );

  const denied = await request.post(
    `${apiBaseUrl}/api/v1/jobs/host-executions/${execution.id}/cancel`,
    {
      headers: {
        Authorization: `Bearer ${limitedToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(denied.status()).toBe(403);
});

test('受限 Host 账号访问执行历史 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'Jobs 执行历史仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/jobs/host-executions?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);

  await loginAsHostViewer(page);
  await page.goto(statusPath(clientKind, 'jobs/host-executions'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
});
