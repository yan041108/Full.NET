import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAsHostAdmin,
  loginAccessTokenWithPassword,
  loginHostAdminAccessToken,
  provisionLimitedHostUserViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可加载工作台汇总含趋势与待办入口（清单 41 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/platform/host-dashboard-summary`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(response.ok()).toBeTruthy();
  const summary = await response.json();

  if (summary.activeTenantCount != null) {
    expect(summary.activeTenantCount).toBeGreaterThanOrEqual(0);
  }
  if (summary.onlineSessionCount != null) {
    expect(summary.onlineSessionCount).toBeGreaterThanOrEqual(0);
  }
  if (summary.accessTrafficTrend != null) {
    expect(Array.isArray(summary.accessTrafficTrend.buckets)).toBe(true);
  }
  expect(Array.isArray(summary.businessEntries)).toBe(true);
  for (const entry of summary.businessEntries) {
    expect(entry.entryKey).toBeTruthy();
    expect(entry.routePath).toMatch(/^\//u);
    expect(typeof entry.count).toBe('number');
  }
});

test('无 dashboard.read 的主体无法读取工作台汇总（清单 41）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    permissionCodes: ['identity.users.read']
  });
  const token = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const response = await request.get(
    `${apiBaseUrl}/api/v1/platform/host-dashboard-summary`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(response.status()).toBe(403);
  expect((await response.json()).code).toBe('authorization.permission_denied');
});

test('Host 管理员工作台展示指标、趋势图与待办分区（清单 41 UI）', async ({
  page
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作台 Vue 布局仅验收 Vue');
  const summaryReady = page.waitForResponse(response =>
    response.url().includes('/api/v1/platform/host-dashboard-summary')
    && response.ok()
  );

  await loginAsHostAdmin(page);
  await page.goto('/#/');
  await summaryReady;

  await expect(page.getByTestId('metric-grid')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('traffic-chart')).toBeVisible();
  await expect(page.getByRole('heading', { name: '运行态势', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '待办事项', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '最近活动', exact: true })).toBeVisible();
});
