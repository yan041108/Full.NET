import { expect, test } from '@playwright/test';
import { loginAsHostAdmin, statusPath } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('已登录用户可访问 403 状态页并保持稳定文案', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await loginAsHostAdmin(page);
  await page.goto(statusPath(clientKind, '403'));
  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});
