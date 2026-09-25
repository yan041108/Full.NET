import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  enterDevelopmentTenant,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginTenantAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function utcWindow(hoursFromNow = 2, durationHours = 1) {
  const start = new Date(Date.now() + hoursFromNow * 60 * 60 * 1000);
  start.setUTCSeconds(0, 0);
  const end = new Date(start.getTime() + durationHours * 60 * 60 * 1000);
  return { start: start.toISOString(), end: end.toISOString() };
}

test('租户管理员可通过 API 管理个人日程（清单 35）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
  const stamp = Date.now().toString(36);
  const { start, end } = utcWindow();

  const createResponse = await request.post(`${apiBaseUrl}/api/v1/calendar/my-personal-schedules`, {
    headers,
    data: {
      content: `E2E 个人日程 ${stamp}`,
      startAtUtc: start,
      endAtUtc: end
    }
  });
  expect(createResponse.status()).toBe(201);
  const created = await createResponse.json();
  expect(created.status).toBe('pending');
  expect(created.content).toContain(stamp);

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules`
      + `?page=1&pageSize=20&fromUtc=${encodeURIComponent(start)}`
      + `&toUtc=${encodeURIComponent(end)}`,
    { headers: { Authorization: `Bearer ${accessToken}`, Origin: origin } }
  );
  expect(listResponse.ok()).toBeTruthy();
  const list = await listResponse.json();
  expect(list.items.some(item => item.id === created.id)).toBe(true);

  const statusResponse = await request.post(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules/${created.id}/status`,
    {
      headers,
      data: { status: 'completed', version: created.version }
    }
  );
  expect(statusResponse.ok()).toBeTruthy();
  const completed = await statusResponse.json();
  expect(completed.status).toBe('completed');

  const updatedContent = `E2E 更新 ${stamp}`;
  const updateResponse = await request.put(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules/${created.id}`,
    {
      headers,
      data: {
        content: updatedContent,
        startAtUtc: start,
        endAtUtc: end,
        version: completed.version
      }
    }
  );
  expect(updateResponse.ok()).toBeTruthy();
  const updated = await updateResponse.json();
  expect(updated.content).toBe(updatedContent);

  const deleteResponse = await request.post(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules/${created.id}/delete`,
    {
      headers,
      data: { version: updated.version }
    }
  );
  expect(deleteResponse.status()).toBe(204);

  const getResponse = await request.get(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules/${created.id}`,
    { headers: { Authorization: `Bearer ${accessToken}`, Origin: origin } }
  );
  expect(getResponse.status()).toBe(404);
});

test('Vue 个人日程页可打开创建对话框（清单 35）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '个人日程页仅验收 Vue');
  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);
  await page.goto('/#/calendar/personal-schedules');

  const view = page.locator('.personal-schedules-view');
  await expect(view.getByRole('heading', { name: '个人日程', exact: true })).toBeVisible({
    timeout: 15_000
  });
  await expect(view.getByTestId('personal-schedules-month-grid')).toBeVisible();

  await view.getByTestId('personal-schedules-create').click();
  await expect(page.getByTestId('personal-schedules-dialog')).toBeVisible();
  await expect(page.getByText('新建个人日程', { exact: true })).toBeVisible();
});

test('受限 Host 账号在租户上下文中访问个人日程 API 被拒绝', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '租户上下文仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const hostToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/calendar/my-personal-schedules?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${hostToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);

  await loginAsHostViewer(page);
  await enterDevelopmentTenant(page);
  await page.goto(statusPath(clientKind, 'calendar/personal-schedules'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
});
