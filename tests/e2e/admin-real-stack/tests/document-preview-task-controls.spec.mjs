import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import path from 'node:path';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import { createHostDocumentItemViaApi, createHostDocumentPreviewTaskViaApi, uploadHostDocumentOfficeVersionViaApi } from './support/document-real-stack.mjs';

test('真实预览任务确认取消，刷新终态后人工重试产生新任务', async ({ page, request }) => {
  test.setTimeout(150_000); const evidence = process.env.FULLNET_PREVIEW_CONTROLS_UI_EVIDENCE;
  const title = `Preview controls ${Date.now().toString(36)}`; const document = await createHostDocumentItemViaApi(request, 'vue', { title });
  const uploaded = await uploadHostDocumentOfficeVersionViaApi(request, 'vue', document.id); expect(uploaded.status(), await uploaded.text()).toBe(200);
  const created = await createHostDocumentPreviewTaskViaApi(request, 'vue', document.id); expect(created.status(), await created.text()).toBe(201); const original = await created.json();
  await page.addInitScript(() => localStorage.setItem('fullnet.admin.locale', 'zh-CN')); await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAsHostAdmin(page); await clickMainNavLink(page, /预览任务/, '文档');
  const row = page.locator('.document-preview-tasks-view .el-table__row').filter({ hasText: title }); await expect(row).toBeVisible();
  await row.getByTestId('document-preview-task-cancel').click(); const confirmation = page.getByRole('dialog', { name: '确认取消预览转换', exact: true });
  await expect(confirmation).toBeVisible(); await expect(confirmation).toContainText(title); await expect(confirmation).toContainText('临时文件由后台释放');
  // 先退出一次确认，证明点击入口本身不会发送取消写入。
  await confirmation.getByRole('button', { name: '取消', exact: true }).click(); await expect(row.getByText('待处理', { exact: true })).toBeVisible();
  await row.getByTestId('document-preview-task-cancel').click(); const canceled = page.waitForResponse(response => new URL(response.url()).pathname === `/api/v1/document/host/preview-tasks/${original.id}/cancel`);
  await confirmation.getByTestId('document-preview-confirm-cancel').click(); const response = await canceled;
  expect(response.status(), await response.text()).toBe(200); expect((await response.json()).statusKey).toBe('cancelled');
  await page.getByTestId('document-preview-refresh').click(); await expect(row.getByText('已取消', { exact: true })).toBeVisible();
  await expect(row.getByTestId('document-preview-task-cancel')).toHaveCount(0); await expect(row.getByTestId('document-preview-task-open-pdf')).toHaveCount(0);
  const retry = page.waitForResponse(result => new URL(result.url()).pathname === `/api/v1/document/host/preview-tasks/${original.id}/retry` && result.request().method() === 'POST');
  await row.getByTestId('document-preview-task-retry').click(); const retryResponse = await retry; expect(retryResponse.status(), await retryResponse.text()).toBe(200);
  const next = await retryResponse.json(); expect(next.id).not.toBe(original.id); expect(next.documentItemId).toBe(document.id); expect(next.statusKey).toBe('pending');
  await expect(page.locator('.document-preview-tasks-view .el-table__row').filter({ hasText: title })).toHaveCount(2);
  await page.setViewportSize({ width: 390, height: 844 }); await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await expect(page.locator('.el-message')).toHaveCount(0); const accessibility = await new AxeBuilder({ page }).include('.document-preview-tasks-view').withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(accessibility.violations.filter(item => ['critical', 'serious'].includes(item.impact))).toEqual([]);
  await page.screenshot({ path: path.join(evidence, 'preview-controls-mobile.png'), animations: 'disabled' });
  await page.setViewportSize({ width: 1440, height: 1000 }); await page.screenshot({ path: path.join(evidence, 'preview-controls-desktop.png'), animations: 'disabled' });
});
