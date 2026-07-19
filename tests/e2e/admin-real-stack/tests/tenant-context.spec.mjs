import { expect, test } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('真实 API 可进入 Development 种子租户并返回 Host', async ({ page }) => {
  await loginAsHostAdmin(page);

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Full.NET Local' })).toBeVisible();

  await page.getByRole('button', { name: '进入租户' }).click();
  await expect(page.getByText('Full.NET Local', { exact: true }).first()).toBeVisible();

  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '返回 Host' }).click();
  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
});
