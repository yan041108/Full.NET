import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document Host 页面仅在 Vue 管理端交付。');
}

test('Host 管理员可创建文档并绑定新版本', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const title = `e2e-doc-${Date.now().toString(36)}`;

  await loginAsHostAdmin(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation.getByRole('link', { name: /Host 文档库/ })).toBeVisible();
  await navigation.getByRole('link', { name: /Host 文档库/ }).click();

  const view = page.locator('.host-document-items-view');
  await expect(view.getByRole('heading', { name: 'Host 文档库', exact: true })).toBeVisible();

  await view.getByTestId('host-document-item-title').fill(title);
  await view.getByTestId('host-document-item-create').click();
  await expect(view.getByText(title, { exact: true })).toBeVisible();

  const row = view.locator('.el-table__row').filter({ hasText: title });
  await row.getByTestId('host-document-item-version-file').setInputFiles({
    name: `${title}.txt`,
    mimeType: 'text/plain',
    buffer: Buffer.from(`document ${clientKind}`)
  });
  await row.getByTestId('host-document-item-upload-version').click();
  await expect(row.getByText('当前版本: 1')).toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号访问文档 API 被拒绝且导航裁剪', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  const origin = adminOrigin(clientKind);
  const accessToken = await loginAccessToken(request, clientKind);

  const response = await request.get(
    `${apiBaseUrl}/api/v1/document/host/items?page=1&pageSize=20`,
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
  await expect(navigation.getByRole('link', { name: /Host 文档库/ })).toHaveCount(0);

  await page.goto(statusPath(clientKind, 'document/host-items'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
