import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可打开工作流实例列表与我发起的页签', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '工作流实例列表仅在 Vue 交付线验收');
  test.setTimeout(60_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /工作流实例/, '工作流');

  const view = page.locator('.workflow-instances');
  await expect(view.getByRole('heading', { name: '工作流实例', exact: true })).toBeVisible();
  const tabs = view.getByTestId('workflow-instance-tabs');
  await expect(tabs).toBeVisible();
  await tabs.getByRole('tab', { name: '我发起的' }).click();
  await expect(view.getByTestId('workflow-instance-pagination')).toBeVisible();
  await tabs.getByRole('tab', { name: '全部实例' }).click();
  await expect(view.getByTestId('workflow-instance-filter-apply')).toBeVisible();
});
