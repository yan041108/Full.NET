import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAccessToken,
  loginAccessTokenWithPassword,
  loginAsHostAdmin,
  loginAsHostUser,
  loginAsHostViewer,
  provisionLimitedHostUserViaApi,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const logFileName = 'e2e-observability.log';
const logMarker = 'fullnet-observability-real-stack-marker';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可查看确定性日志尾部并下载同一文件', async ({ page }) => {
  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /运行日志/);

  const view = page.locator('.observability-log-files');
  await expect(view.getByRole('heading', { name: '运行日志', exact: true })).toBeVisible();
  await expect(view.getByRole('heading', { name: logFileName, exact: true })).toBeVisible();
  await expect(view.getByText(logMarker, { exact: false })).toBeVisible();

  const downloadPromise = page.waitForEvent('download');
  await view.getByTestId('observability-log-download').click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe(logFileName);
  const stream = await download.createReadStream();
  const chunks = [];
  for await (const chunk of stream) {
    chunks.push(chunk);
  }
  expect(Buffer.concat(chunks).toString('utf8')).toContain(logMarker);
});

test('只有读取权限的 Host 用户可查看尾部但不能下载', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const stamp = Date.now().toString(36);
  const limited = await provisionLimitedHostUserViaApi(request, clientKind, {
    roleCode: `e2e-observability-reader-${stamp}`,
    username: `e2e-observability-reader-${stamp}`,
    roleName: 'E2E 运行日志只读角色',
    displayName: 'E2E 运行日志只读用户',
    permissionCodes: [
      'platform.dashboard.read',
      'identity.navigation.read',
      'observability.log_files.read'
    ]
  });
  const accessToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    limited.username,
    limited.password
  );
  const listResponse = await request.get(`${apiBaseUrl}/api/v1/observability/log-files`, {
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  expect(listResponse.ok()).toBeTruthy();
  const files = await listResponse.json();
  const logFile = files.find(file => file.fileName === logFileName);
  expect(logFile?.id).toBeTruthy();

  const downloadResponse = await request.get(
    `${apiBaseUrl}/api/v1/observability/log-files/${logFile.id}/download`,
    { headers: { Authorization: `Bearer ${accessToken}`, Origin: origin } }
  );
  expect(downloadResponse.status()).toBe(403);
  expect((await downloadResponse.json()).code).toBe('authorization.permission_denied');

  await loginAsHostUser(page, limited.username, limited.password);
  await clickMainNavLink(page, /运行日志/);
  const view = page.locator('.observability-log-files');
  await expect(view.getByText(logMarker, { exact: false })).toBeVisible();
  await expect(view.getByTestId('observability-log-download')).toHaveCount(0);
});

test('无读取权限的 Host 用户被 API 和客户端路由同时拒绝', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);
  const response = await request.get(`${apiBaseUrl}/api/v1/observability/log-files`, {
    headers: { Authorization: `Bearer ${accessToken}`, Origin: origin }
  });
  expect(response.status()).toBe(403);
  expect((await response.json()).code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /运行日志/ })).toHaveCount(0);
  await page.goto(statusPath(clientKind, 'observability/log-files'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
