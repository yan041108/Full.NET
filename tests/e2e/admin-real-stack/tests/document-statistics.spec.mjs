import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAccessToken,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  getHostDocumentStatisticsViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 统计页面仅在 Vue 管理端交付。');
}

test('Host 管理员可查看文档统计面板', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  await createHostDocumentItemViaApi(request, clientKind);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /文档统计/);
  await expect(page.getByRole('heading', { name: '文档统计', exact: true })).toBeVisible();
  await expect(page.getByTestId('document-statistics-panel')).toBeVisible({ timeout: 15_000 });
});

test('Host 管理员统计 API 返回 200', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await createHostDocumentItemViaApi(request, clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await getHostDocumentStatisticsViaApi(request, clientKind, accessToken);
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(body.summary.totalItems).toBeGreaterThanOrEqual(1);
});
