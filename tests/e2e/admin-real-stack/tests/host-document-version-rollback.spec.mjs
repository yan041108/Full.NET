import { createHash } from 'node:crypto';
import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  listHostDocumentVersionsViaApi,
  rollbackHostDocumentVersionViaApi,
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

test('API：Host 文档版本回滚切换当前指针（清单 47）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const stamp = Date.now().toString(36);
  const item = await createHostDocumentItemViaApi(request, clientKind, {
    title: `e2e-rollback-${stamp}`
  });
  const v1Payload = `v1-${stamp}`;
  const afterV1 = await uploadHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v1Payload,
    'v1.txt'
  );
  const v1Id = afterV1.currentVersion.id;
  const v2Payload = `v2-${stamp}`;
  const afterV2 = await uploadHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v2Payload,
    'v2.txt'
  );
  expect(afterV2.currentVersion.versionNumber).toBe(2);

  const rollbackResponse = await rollbackHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v1Id,
    afterV2.version
  );
  expect(rollbackResponse.ok()).toBeTruthy();
  const rolledBack = await rollbackResponse.json();
  expect(rolledBack.currentVersion.id).toBe(v1Id);
  expect(rolledBack.currentVersion.versionNumber).toBe(1);
  const expectedHash = createHash('sha256').update(v1Payload).digest('hex');
  expect(rolledBack.currentVersion.contentHash).toBe(expectedHash);

  const versions = await listHostDocumentVersionsViaApi(request, clientKind, item.id);
  expect(versions.length).toBeGreaterThanOrEqual(2);

  const repeatRollback = await rollbackHostDocumentVersionViaApi(
    request,
    clientKind,
    item.id,
    v1Id,
    rolledBack.version
  );
  expect(repeatRollback.status()).toBe(409);
  expect((await repeatRollback.json()).code).toBe('document.host_document.version_already_current');
});

test('UI：版本历史回滚到较早版本（清单 47）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  const title = `e2e-ui-rollback-${Date.now().toString(36)}`;

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /Host 文档库/);
  const view = page.locator('.host-document-items-view');

  await view.getByTestId('host-document-item-title').fill(title);
  await view.getByTestId('host-document-item-create').click();
  await expect(view.getByText(title, { exact: true })).toBeVisible();

  const row = view.locator('.el-table__row').filter({ hasText: title });
  await row.getByTestId('host-document-item-version-file').setInputFiles({
    name: 'v1.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('rollback-v1')
  });
  await row.getByTestId('host-document-item-upload-version').click();
  await expect(row.locator('td').filter({ hasText: /^1$/ })).toBeVisible({ timeout: 15_000 });

  await row.getByTestId('host-document-item-version-file').setInputFiles({
    name: 'v2.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('rollback-v2')
  });
  await row.getByTestId('host-document-item-upload-version').click();
  await expect(row.locator('td').filter({ hasText: /^2$/ })).toBeVisible({ timeout: 15_000 });

  await row.getByTestId('host-document-item-version-history').click();
  const historyDialog = page.getByRole('dialog');
  await expect(historyDialog).toBeVisible();

  const v1Row = historyDialog.locator('tr').filter({ hasText: /^1\b/ }).first();
  await v1Row.getByTestId('host-document-item-version-rollback').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(page.getByText('当前版本已回滚')).toBeVisible({ timeout: 15_000 });
  await expect(row.locator('td').filter({ hasText: /^1$/ })).toBeVisible({ timeout: 15_000 });
});
