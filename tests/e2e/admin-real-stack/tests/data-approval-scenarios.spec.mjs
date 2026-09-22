import { expect, test } from '@playwright/test';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可加载数据审批场景目录', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '数据审批场景页仅在 Vue 交付线验收');
  test.setTimeout(60_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /审批场景/, '数据审批');

  const view = page.locator('.page-shell').filter({ has: page.getByRole('heading', { name: '审批场景配置' }) });
  await expect(view.getByRole('heading', { name: '审批场景配置', exact: true })).toBeVisible();
  await expect(view.getByText('流水号规则更新', { exact: true })).toBeVisible();
  await expect(view.getByText('流水号规则禁用', { exact: true })).toBeVisible();
  await view.getByText('流水号规则更新', { exact: true }).click();
  await expect(view.getByTestId('data-approval-scenario-enabled')).toBeVisible();
});
