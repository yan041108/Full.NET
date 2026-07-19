import { expect, test } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('真实 API 退出后回到登录页', async ({ page }) => {
  await loginAsHostAdmin(page);

  await page.getByRole('button', { name: '退出登录' }).click();
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
});
