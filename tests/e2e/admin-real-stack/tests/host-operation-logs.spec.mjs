import { expect, test } from '@playwright/test';
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

test('Host 管理员可从真实 API 加载操作日志', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const adminToken = await loginHostAdminAccessToken(request, clientKind);
  await request.post(`${apiBaseUrl}/api/v1/settings/config-entries`, {
    headers: {
      Authorization: `Bearer ${adminToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    },
    data: {
      configKey: `e2e.op.${Date.now()}`,
      displayName: 'E2E 操作日志探针',
      description: null,
      valueKind: 'string',
      value: '1',
      displayOrder: 1
    }
  });

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /操作日志/);

  const operationLogsView = clientKind === 'layui'
    ? page.locator('[data-route-view="operation-logs"]')
    : page.locator('.operation-logs-view');

  await expect(operationLogsView.getByRole('heading', { name: '操作日志', exact: true })).toBeVisible();
  await expect(operationLogsView.getByText('/api/v1/settings/config-entries', { exact: false }).first())
    .toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号访问操作日志 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/auditing/operation-logs?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /操作日志/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'auditing/operation-logs'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

function last24HoursUtcRange() {
  const toUtc = new Date().toISOString();
  const fromUtc = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
  return { fromUtc, toUtc };
}

test('Host 管理员可查询操作日志趋势与域变更差异（清单 26）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };
  const { fromUtc, toUtc } = last24HoursUtcRange();

  const trendResponse = await request.get(
    `${apiBaseUrl}/api/v1/auditing/operation-logs/trends`
      + `?fromUtc=${encodeURIComponent(fromUtc)}`
      + `&toUtc=${encodeURIComponent(toUtc)}`
      + '&bucketMinutes=60',
    { headers }
  );
  expect(trendResponse.ok()).toBeTruthy();
  const trend = await trendResponse.json();
  expect(Array.isArray(trend.buckets)).toBe(true);
  expect(typeof trend.totalCount).toBe('number');

  const diffResponse = await request.get(
    `${apiBaseUrl}/api/v1/auditing/domain-change-diffs?traceId=e2e-empty-trace-id`,
    { headers }
  );
  expect(diffResponse.ok()).toBeTruthy();
  const diff = await diffResponse.json();
  expect(diff.traceId).toBe('e2e-empty-trace-id');
  expect(Array.isArray(diff.entries)).toBe(true);
});

test('Vue 操作日志页展示趋势面板（清单 26）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '趋势面板仅验收 Vue');
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /操作日志/);
  const operationLogsView = page.locator('.operation-logs-view');
  await expect(operationLogsView.getByRole('heading', { name: '最近 24 小时趋势' })).toBeVisible({
    timeout: 15_000
  });
});

test('Host 管理员可导出操作日志工作簿（清单 27）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const { fromUtc, toUtc } = last24HoursUtcRange();

  const response = await request.post(`${apiBaseUrl}/api/v1/auditing/operation-logs/exports`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    },
    data: { fromUtc, toUtc }
  });
  expect(response.ok()).toBeTruthy();
  const exportHeaders = response.headers();
  expect(exportHeaders['x-fullnet-export-row-count']).toBeTruthy();
  const body = await response.body();
  expect(body.byteLength).toBeGreaterThan(100);
});

test('Vue 操作日志页可打开导出对话框（清单 27）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '导出对话框仅验收 Vue');
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /操作日志/);
  const operationLogsView = page.locator('.operation-logs-view');
  await operationLogsView.getByTestId('operation-logs-action-export').click();
  await expect(page.getByRole('dialog')).toBeVisible({ timeout: 10_000 });
  await page.getByRole('button', { name: '取消', exact: true }).click();
});
