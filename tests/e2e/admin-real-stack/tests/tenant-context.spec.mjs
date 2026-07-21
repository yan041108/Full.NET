import { expect, test } from '@playwright/test';
import {
  enterDevelopmentTenant,
  expectVisibleCurrentContext,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('真实 API 可进入 Development 种子租户并返回 Host', async ({ page }) => {
  await loginAsHostAdmin(page);
  await enterDevelopmentTenant(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '返回 Host' }).click();
  await expectVisibleCurrentContext(page, 'Full.NET Host');
});
