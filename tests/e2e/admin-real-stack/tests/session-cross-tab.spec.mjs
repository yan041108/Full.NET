import { expect, test } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('双 Tab 共享 Refresh Cookie 可进入控制台', async ({ page, context }) => {
  const secondTab = await context.newPage();
  await loginAsHostAdmin(page);
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();

  await secondTab.goto('/');
  const navigation = secondTab.getByRole('navigation', { name: '主导航' });
  await expect(navigation).toBeVisible({ timeout: 15_000 });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();

  await secondTab.close();
});
