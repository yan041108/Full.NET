import { expect, test } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('真实 API 可进入 Development 种子租户并返回 Host', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();

  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation).toBeVisible();
  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Full.NET Local' })).toBeVisible();

  await page.getByRole('button', { name: '进入租户' }).click();
  await expect(page.getByText('Full.NET Local', { exact: true }).first()).toBeVisible();

  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '返回 Host' }).click();
  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
});
