import { expect, test } from '@playwright/test';
import {
  loginAsHostAdmin,
  statusPath
} from './support/real-stack-auth.mjs';
import {
  accessDocumentShareViaApi,
  createHostDocumentItemViaApi,
  createHostDocumentShareViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 分享页面仅在 Vue 管理端交付。');
}

test('Host 管理员可创建分享并在禁用后拒绝匿名访问', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const document = await createHostDocumentItemViaApi(request, clientKind);
  const share = await createHostDocumentShareViaApi(request, clientKind, document.id);

  const enabledAccess = await accessDocumentShareViaApi(request, share.shareCode);
  expect(enabledAccess.ok()).toBeTruthy();

  await loginAsHostAdmin(page);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /文档分享/ }).click();
  await expect(page.getByRole('heading', { name: '文档分享', exact: true })).toBeVisible();

  const row = page.locator('.el-table__row').filter({ hasText: share.shareCode });
  await expect(row).toBeVisible();
  await row.getByTestId('document-share-toggle').click();
  await expect(page.getByText('分享状态已更新')).toBeVisible({ timeout: 15_000 });

  const disabledAccess = await accessDocumentShareViaApi(request, share.shareCode);
  expect(disabledAccess.ok()).toBeFalsy();
});

test('Host 管理员可通过 UI 创建分享', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const document = await createHostDocumentItemViaApi(request, clientKind);

  await loginAsHostAdmin(page);
  await page.goto(statusPath(clientKind, 'document/shares'));
  await expect(page.getByRole('heading', { name: '文档分享', exact: true })).toBeVisible();

  await page.getByTestId('document-share-create').click();
  await page.getByTestId('document-share-editor-form').locator('input').first().fill(document.id);
  await page.getByTestId('document-share-editor-submit').click();
  await expect(page.getByText('分享已创建')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.el-table__row').filter({ hasText: document.id })).toBeVisible();
});
