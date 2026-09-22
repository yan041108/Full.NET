import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可切换待办与已办历史页签', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作流待办仅在 Vue 交付线验收');
  test.setTimeout(60_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我的待办/, '工作流');

  const view = page.locator('.workflow-todos');
  await expect(view.getByRole('heading', { name: '我的工作流待办', exact: true })).toBeVisible();
  const tabs = view.getByRole('tablist');
  await tabs.getByRole('tab', { name: '已办', exact: true }).click();
  await expect(view.getByText('当前没有已办记录', { exact: true })).toBeVisible({ timeout: 15_000 });
  await tabs.getByRole('tab', { name: '待办', exact: true }).click();
});
