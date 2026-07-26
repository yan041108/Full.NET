import { expect, test } from '@playwright/test';
import {
  expectVisibleCurrentContext,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('刷新页面后通过 Refresh Cookie 恢复会话', async ({ page }) => {
  await loginAsHostAdmin(page);
  await expectVisibleCurrentContext(page, 'Full.NET Host');

  await page.reload();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
  await expectVisibleCurrentContext(page, 'Full.NET Host');
});
