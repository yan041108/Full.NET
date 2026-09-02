import { expect, test } from '@playwright/test';
import {
  loginAccessToken,
  loginAsHostAdmin,
  loginAsHostViewer,
  statusPath
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  deleteHostDocumentItemViaApi,
  purgeRecycleBinItemViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 回收站页面仅在 Vue 管理端交付。');
}

test('Host 管理员可在回收站恢复已删除文档', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const document = await createHostDocumentItemViaApi(request, clientKind);
  await deleteHostDocumentItemViaApi(request, clientKind, document);

  await loginAsHostAdmin(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /文档回收站/ }).click();
  await expect(page.getByRole('heading', { name: '文档回收站', exact: true })).toBeVisible();

  const row = page.locator('.el-table__row').filter({ hasText: document.title });
  await expect(row).toBeVisible();
  await row.getByTestId('document-recycle-restore').click();
  await expect(page.getByText('文档已恢复')).toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号无法彻底删除回收站文档', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const document = await createHostDocumentItemViaApi(request, clientKind);
  await deleteHostDocumentItemViaApi(request, clientKind, document);

  const accessToken = await loginAccessToken(request, clientKind);
  const response = await purgeRecycleBinItemViaApi(
    request,
    clientKind,
    accessToken,
    document.id
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');

  await loginAsHostViewer(page);
  await page.goto(statusPath(clientKind, 'document/recycle-bin'));
  await expect(page.getByRole('heading', { name: '文档回收站', exact: true })).toBeVisible();
  await expect(page.getByTestId('document-recycle-purge')).toHaveCount(0);
});
