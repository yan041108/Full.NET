import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test('Host 管理员可在真实页面查看认证事件和详情', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const token = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.get(
    `${apiBaseUrl}/api/v1/identity/authentication-events?pageSize=20&eventType=login`,
    { headers: { Authorization: `Bearer ${token}`, Origin: adminOrigin(clientKind) } }
  );
  expect(response.ok()).toBeTruthy();
  const events = await response.json();
  expect(events.items.some(item => item.eventType === 'login' && item.succeeded)).toBeTruthy();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /认证事件日志/);
  const view = page.locator('.authentication-events-view');
  await expect(view.getByRole('heading', { name: '认证事件日志' })).toBeVisible();
  await expect(view.getByRole('alert')).toHaveCount(0);
  await expect(view.locator('.el-table__row').first()).toBeVisible();
  await view.getByRole('button', { name: '查看详情' }).first().click();
  await expect(page.getByRole('dialog')).toBeVisible();
});

test('新筛选结果不会被较慢的旧查询覆盖', async ({ page }) => {
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /认证事件日志/);
  const view = page.locator('.authentication-events-view');
  await expect(view.locator('.el-table__row').first()).toBeVisible();

  let signalOldRequest;
  let releaseOldResponse;
  const oldRequest = new Promise(resolve => { signalOldRequest = resolve; });
  const release = new Promise(resolve => { releaseOldResponse = resolve; });
  await page.route('**/api/v1/identity/authentication-events?*', async route => {
    const eventType = new URL(route.request().url()).searchParams.get('eventType');
    if (eventType !== 'login') {
      await route.continue();
      return;
    }
    const response = await route.fetch();
    signalOldRequest();
    await release;
    await route.fulfill({ response });
  });

  const eventTypeInput = view.getByPlaceholder('login / logout / refresh');
  await eventTypeInput.fill('login');
  await view.getByRole('button', { name: '查询' }).click();
  await oldRequest;
  await eventTypeInput.fill('event-that-does-not-exist');
  const latestResponse = page.waitForResponse(response =>
    new URL(response.url()).searchParams.get('eventType') === 'event-that-does-not-exist'
    && response.ok());
  await view.getByRole('button', { name: '查询' }).click();
  await latestResponse;
  await expect(view.locator('.el-table__row')).toHaveCount(0);

  const oldBrowserResponse = page.waitForResponse(response =>
    new URL(response.url()).searchParams.get('eventType') === 'login');
  releaseOldResponse();
  await oldBrowserResponse;
  await page.waitForTimeout(150);
  await expect(view.locator('.el-table__row')).toHaveCount(0);
  await expect(view.getByRole('alert')).toHaveCount(0);
});
