import { expect, test } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('真实 API 退出后回到登录页', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);

  if (clientKind === 'vue') {
    await page.getByRole('button', { name: '系统管理员' }).click();
    await page.getByRole('button', { name: '退出登录' }).click();
  } else {
    await page.locator('[data-session-logout]').click();
  }

  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
});
