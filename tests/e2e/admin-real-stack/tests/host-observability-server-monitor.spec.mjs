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

const sensitivePatterns = [
  /connectionstring/i,
  /password\s*=/i,
  /clientsecret/i,
  /apikey\s*=/i
];

function assertNoSensitiveLeak(serialized) {
  for (const pattern of sensitivePatterns) {
    expect(serialized).not.toMatch(pattern);
  }
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可列出服务器实例并读取当前实例运行时（清单 28）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${accessToken}`,
    Origin: origin
  };

  const listResponse = await request.get(`${apiBaseUrl}/api/v1/observability/server-instances`, {
    headers
  });
  expect(listResponse.ok()).toBeTruthy();
  const instances = await listResponse.json();
  expect(Array.isArray(instances)).toBe(true);
  expect(instances.length).toBeGreaterThan(0);
  assertNoSensitiveLeak(JSON.stringify(instances));

  const current = instances.find(entry => entry.isCurrent) ?? instances[0];
  expect(current?.instanceKey).toBeTruthy();

  const runtimeResponse = await request.get(
    `${apiBaseUrl}/api/v1/observability/server-instances/${encodeURIComponent(current.instanceKey)}/runtime`,
    { headers }
  );
  expect(runtimeResponse.ok()).toBeTruthy();
  const runtime = await runtimeResponse.json();
  expect(runtime.instanceKey).toBe(current.instanceKey);
  expect(typeof runtime.frameworkDescription).toBe('string');
  expect(runtime.frameworkDescription.length).toBeGreaterThan(0);
  expect(typeof runtime.applicationVersion).toBe('string');
  expect(Array.isArray(runtime.metrics)).toBe(true);
  expect(runtime.metrics.length).toBeGreaterThan(0);
  assertNoSensitiveLeak(JSON.stringify(runtime));
});

test('Vue 服务器监控页展示实例目录与运行时摘要（清单 28）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '服务器监控页仅验收 Vue');
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /服务器监控/);

  const view = page.locator('.observability-server-monitor');
  await expect(view.getByRole('heading', { name: '服务器监控', exact: true })).toBeVisible({
    timeout: 15_000
  });
  await expect(view.locator('.observability-server-monitor__runtime')).toBeVisible({
    timeout: 15_000
  });
  await expect(view.getByText(/\.NET/u).first()).toBeVisible();
});

test('受限 Host 账号访问服务器监控 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(`${apiBaseUrl}/api/v1/observability/server-instances`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /服务器监控/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'observability/server-monitor'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
