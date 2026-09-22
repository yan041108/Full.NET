import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可打开数据审批请求列表并查看详情', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '数据审批请求页仅在 Vue 交付线验收');
  test.setTimeout(90_000);
  const clientKind = testInfo.project.metadata.clientKind;
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: adminOrigin(clientKind)
  };

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/data-approvals/requests?page=1&pageSize=20`,
    { headers }
  );
  expect(listResponse.status()).toBe(200);
  const listBody = await listResponse.json();

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /数据审批/, '数据审批');

  const view = page.locator('.page-shell').filter({
    has: page.getByRole('heading', { name: '数据审批请求', exact: true })
  });
  await expect(view.getByRole('heading', { name: '数据审批请求', exact: true })).toBeVisible();

  if (listBody.items?.length) {
    await view.getByTestId('data-approval-load').first().click();
    await expect(view.getByTestId('data-approval-detail-status')).toBeVisible({ timeout: 15_000 });
  } else {
    await expect(view.getByText('尚无审批请求', { exact: true })).toBeVisible();
  }
});
