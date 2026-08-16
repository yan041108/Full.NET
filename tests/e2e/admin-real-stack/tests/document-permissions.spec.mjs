import { expect, test } from '@playwright/test';
import {
  loginAccessToken,
  loginAsHostAdmin,
  statusPath
} from './support/real-stack-auth.mjs';
import {
  createHostDocumentItemViaApi,
  getHostDocumentPermissionsViaApi
} from './support/document-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'Document 权限页面仅在 Vue 管理端交付。');
}

test('Host 管理员可加载文档权限列表', async ({ page, request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const document = await createHostDocumentItemViaApi(request, clientKind);

  await loginAsHostAdmin(page);
  await page.goto(statusPath(clientKind, 'document/permissions'));
  await expect(page.getByRole('heading', { name: '文档权限', exact: true })).toBeVisible();

  const form = page.getByTestId('document-permissions-form');
  await form.locator('input').fill(document.id);
  await page.getByTestId('document-permissions-load').click();
  await expect(page.getByText('尚未配置权限')).toBeVisible({ timeout: 15_000 });
});

test('受限 Host 账号读取文档权限 API 被拒绝', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  const document = await createHostDocumentItemViaApi(request, clientKind);
  const accessToken = await loginAccessToken(request, clientKind);
  const response = await getHostDocumentPermissionsViaApi(
    request,
    clientKind,
    accessToken,
    document.id
  );
  expect(response.status()).toBe(403);
  const problem = await response.json();
  expect(problem.code).toBe('authorization.permission_denied');
});
