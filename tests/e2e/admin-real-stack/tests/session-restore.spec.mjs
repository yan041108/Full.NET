import { expect, test } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('刷新页面后通过 Refresh Cookie 恢复会话', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();

  await page.reload();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
});
