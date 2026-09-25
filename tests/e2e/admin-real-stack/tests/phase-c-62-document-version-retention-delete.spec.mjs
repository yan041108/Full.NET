import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  deleteHostDocumentVersionViaApi,
  getHostDocumentVersionRetentionViaApi,
  listHostDocumentVersionsViaApi,
  uploadHostDocumentVersionViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document Host 页面仅在 Vue 管理端交付。');
}

test('API：历史版本授权删除与当前版本保护（清单 62，依赖 47）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const retentionResponse = await getHostDocumentVersionRetentionViaApi(request, clientKind);
  expect(retentionResponse.ok()).toBeTruthy();
  const retention = await retentionResponse.json();
  expect(retention.minimumRetainedVersionsPerItem).toBeGreaterThanOrEqual(1);

  const stamp = Date.now().toString(36);
  const item = await createHostDocumentItemViaApi(request, clientKind, {
    title: `e2e-delete-version-${stamp}`
  });
  const afterV1 = await uploadHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    `v1-${stamp}`,
    'v1.txt'
  );
  const v1Id = afterV1.currentVersion.id;
  const afterV2 = await uploadHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    `v2-${stamp}`,
    'v2.txt'
  );
  const v2Id = afterV2.currentVersion.id;

  const deleteHistory = await deleteHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v1Id,
    afterV2.version
  );
  expect(deleteHistory.ok()).toBeTruthy();
  const updated = await deleteHistory.json();
  expect(updated.currentVersion.id).toBe(v2Id);

  const versions = await listHostDocumentVersionsViaApi(request, clientKind, item.id);
  expect(versions).toHaveLength(1);
  expect(versions[0].versionNumber).toBe(2);

  const deleteCurrent = await deleteHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v2Id,
    updated.version
  );
  expect(deleteCurrent.status()).toBe(409);
  expect((await deleteCurrent.json()).code).toBe('document.host_document.version_already_current');
});

test('UI：版本保留策略与历史版本删除入口（清单 62）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  const title = `e2e-ui-delete-version-${Date.now().toString(36)}`;

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /文档统计/);
  await page.getByRole('tab', { name: '版本保留' }).click();
  await expect(page.getByTestId('document-version-retention-panel')).toBeVisible({
    timeout: 20_000
  });

  await clickMainNavLink(page, /Host 文档库/);
  const view = page.locator('.host-document-items-view');
  await view.getByTestId('host-document-item-title').fill(title);
  await view.getByTestId('host-document-item-create').click();
  await expect(view.getByText(title, { exact: true })).toBeVisible();

  const row = view.locator('.el-table__row').filter({ hasText: title });
  await row.getByTestId('host-document-item-version-file').setInputFiles({
    name: 'v1.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('delete-v1')
  });
  await row.getByTestId('host-document-item-upload-version').click();
  await expect(row.locator('td').filter({ hasText: /^1$/ })).toBeVisible({ timeout: 15_000 });

  await row.getByTestId('host-document-item-version-file').setInputFiles({
    name: 'v2.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('delete-v2')
  });
  await row.getByTestId('host-document-item-upload-version').click();
  await expect(row.locator('td').filter({ hasText: /^2$/ })).toBeVisible({ timeout: 15_000 });

  await row.getByTestId('host-document-item-version-history').click();
  const historyDialog = page.getByRole('dialog');
  await expect(historyDialog).toBeVisible();

  const v1Row = historyDialog.locator('tr').filter({ hasText: /^1\b/ }).first();
  await expect(v1Row.getByTestId('host-document-item-version-delete')).toBeVisible();
  await expect(
    historyDialog.locator('tr').filter({ hasText: /^2\b/ }).first().getByTestId('host-document-item-version-delete')
  ).toHaveCount(0);

  await v1Row.getByTestId('host-document-item-version-delete').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(page.getByText('历史版本已删除')).toBeVisible({ timeout: 15_000 });
  await expect(historyDialog.locator('tr').filter({ hasText: /^1\b/ })).toHaveCount(0);
});
