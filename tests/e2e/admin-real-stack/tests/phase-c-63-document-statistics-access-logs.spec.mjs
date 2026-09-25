import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  getHostDocumentStatisticsViaApi,
  listHostDocumentAccessLogsViaApi,
  previewHostDocumentItemViaApi,
  uploadHostDocumentVersionViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 统计页仅在 Vue 管理端交付。');
}

test('API：文档汇总指标与访问日志分页（清单 63 首批指标集）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const stamp = Date.now().toString(36);
  const item = await createHostDocumentItemViaApi(request, clientKind, {
    title: `e2e-doc-stats-${stamp}`
  });
  await uploadHostDocumentVersionViaApi(request, clientKind, item.id, `body-${stamp}`, 'note.txt');

  const previewResponse = await previewHostDocumentItemViaApi(request, clientKind, item.id);
  expect([200, 422]).toContain(previewResponse.status());

  const token = await loginHostAdminAccessToken(request, clientKind);
  const statsResponse = await getHostDocumentStatisticsViaApi(request, clientKind, token);
  expect(statsResponse.ok()).toBeTruthy();
  const stats = await statsResponse.json();
  expect(stats.summary.totalItems).toBeGreaterThanOrEqual(1);
  expect(typeof stats.summary.totalVersions).toBe('number');
  expect(typeof stats.todayAccessCount).toBe('number');
  expect(Array.isArray(stats.byType)).toBe(true);

  const logsResponse = await listHostDocumentAccessLogsViaApi(request, clientKind, {
    documentItemId: item.id
  });
  expect(logsResponse.ok()).toBeTruthy();
  const logs = await logsResponse.json();
  expect(Array.isArray(logs.items)).toBe(true);
  expect(logs.items.some((entry) => entry.documentItemId === item.id)).toBe(true);
});

test('UI：统计与访问日志页签（清单 63，不含热门标签/批量分享）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(90_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /文档统计/);

  const tabs = page.getByTestId('document-statistics-tabs');
  await expect(tabs.getByRole('tab', { name: '统计' })).toBeVisible({ timeout: 20_000 });
  await expect(tabs.getByRole('tab', { name: '访问日志' })).toBeVisible();
  await expect(page.getByTestId('document-statistics-panel')).toBeVisible();

  await tabs.getByRole('tab', { name: '访问日志' }).click();
  await expect(page.getByTestId('document-access-logs-panel')).toBeVisible();
  const emptyLogs = page.getByTestId('document-access-logs-empty');
  const logsTable = page.getByTestId('document-access-logs-table');
  await expect(emptyLogs.or(logsTable)).toBeVisible({ timeout: 20_000 });
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
