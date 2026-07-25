import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath,
  uploadHostFileViaApi
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function uniqueFileName(clientKind, prefix) {
  // 双端并行时用时间戳与客户端后缀保证 Host 全局唯一文件名。
  const stamp = Date.now().toString(36);
  const suffix = clientKind === 'layui' ? 'l' : 'v';
  return `${prefix}-${stamp}-${suffix}.txt`;
}

test('Host 管理员可从真实 API 加载文件列表', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const fileName = uniqueFileName(clientKind, 'e2e-file');
  await uploadHostFileViaApi(request, clientKind, {
    fileName,
    content: `real-stack ${clientKind}`,
    contentType: 'text/plain'
  });

  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /文件管理/ })).toBeVisible();
  await navigation.getByRole('link', { name: /文件管理/ }).click();

  const hostFilesView = clientKind === 'layui'
    ? page.locator('[data-route-view="host-files"]')
    : page.locator('.host-files-view');

  await expect(hostFilesView.getByRole('heading', { name: '文件管理', exact: true })).toBeVisible();
  await expect(hostFilesView.getByText(fileName, { exact: true })).toBeVisible();
});

test('受限 Host 账号访问文件 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/files/host-files?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /文件管理/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'files/host-files'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
