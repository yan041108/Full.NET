import { expect, test } from '@playwright/test';
import {
  expectVisibleCurrentContext,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ context }) => {
  await context.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('双 Tab 共享 Refresh Cookie 可进入控制台', async ({ page, context }) => {
  await loginAsHostAdmin(page);
  await expectVisibleCurrentContext(page, 'Full.NET Host');

  const secondTab = await context.newPage();
  await secondTab.goto('/');
  const navigation = secondTab.getByRole('navigation', { name: '主导航' });
  await expect(navigation).toBeVisible({ timeout: 30_000 });
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();

  await secondTab.close();
});
