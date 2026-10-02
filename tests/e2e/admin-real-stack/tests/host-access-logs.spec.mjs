import { expect, test } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginHostAdminAccessToken,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可加载访问日志且单次请求只采集一条记录', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const accessLogsUrl = `${apiBaseUrl}/api/v1/auditing/access-logs?page=1&pageSize=20`;
  const baselineResponse = await request.get(accessLogsUrl, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  expect(baselineResponse.ok()).toBeTruthy();
  // Development 显式开启访问入库；用唯一 TraceId 对账单次请求，查询自身产生的记录不干扰断言。
  const traceId = randomBytes(16).toString('hex');
  const spanId = randomBytes(8).toString('hex');
  const enumResponse = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs?page=1&pageSize=1`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        traceparent: `00-${traceId}-${spanId}-01`
      }
    }
  );
  expect(enumResponse.ok()).toBeTruthy();

  const capturedUrl = new URL(accessLogsUrl);
  capturedUrl.searchParams.set('fromUtc', new Date(Date.now() - 3600000).toISOString());
  capturedUrl.searchParams.set('toUtc', new Date(Date.now() + 60000).toISOString());
  capturedUrl.searchParams.set('pathContains', '/api/v1/settings/enum-catalogs');
  await expect.poll(async () => {
    const response = await request.get(capturedUrl.toString(), {
      headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
    });
    expect(response.ok()).toBeTruthy();
    const accessPage = await response.json();
    const rows = accessPage.items.filter(row => row.traceId === traceId);
    if (rows.length === 1) {
      expect(rows[0].httpMethod).toBe('GET');
      expect(rows[0].statusCode).toBe(200);
      expect(rows[0].isAuthenticated).toBe(true);
    }
    return rows.length;
  }, { timeout: 15000 }).toBe(1);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /访问日志/);

  const accessLogsView = clientKind === 'layui'
    ? page.locator('[data-route-view="access-logs"]')
    : page.locator('.access-logs-view');

  await expect(accessLogsView.getByRole('heading', { name: '访问日志', exact: true })).toBeVisible();
  await expect(accessLogsView.locator('.el-table')).toBeVisible();
});

test('受限 Host 账号访问日志 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/auditing/access-logs?page=1&pageSize=20`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /访问日志/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'auditing/access-logs'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
