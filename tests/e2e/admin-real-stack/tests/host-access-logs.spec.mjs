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

test('Host 管理员可从真实 API 加载访问日志', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);

  // 先产生一条可检索的访问审计，避免仅依赖登录瞬间的异步落库时序。
  const enumResponse = await request.get(
    `${apiBaseUrl}/api/v1/settings/enum-catalogs?page=1&pageSize=1`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(enumResponse.ok()).toBeTruthy();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /访问日志/);

  const accessLogsView = clientKind === 'layui'
    ? page.locator('[data-route-view="access-logs"]')
    : page.locator('.access-logs-view');

  await expect(accessLogsView.getByRole('heading', { name: '访问日志', exact: true })).toBeVisible();
  // 访问日志属于有界异步写入；页面需重新取数，不能只等待首次空快照自行变化。
  await expect(async () => {
    await page.reload();
    await expect(accessLogsView.getByText('/api/v1/settings/enum-catalogs', { exact: false }).first()).toBeVisible({
      timeout: 1_000
    });
  }).toPass({
    timeout: 15_000,
    intervals: [250, 500, 1_000]
  });
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
